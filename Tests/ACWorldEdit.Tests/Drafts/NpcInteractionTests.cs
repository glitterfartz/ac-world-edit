using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// NpcInteraction.Build view assembly (menus, vendors, trainers, edited marks) and the
/// InteractionRules server loading rules, over in-memory rows and a fake lookup.
/// </summary>
public class NpcInteractionTests
{
    const uint Entry = 42;

    sealed class Lookup : IInteractionLookup
    {
        public HashSet<uint> Items { get; } = [2589];
        public HashSet<uint> Quests { get; } = [7];
        public HashSet<uint> Spells { get; } = [133];
        public HashSet<uint> ExtendedCosts { get; } = [];
        public HashSet<uint> Pois { get; } = [];
        public HashSet<uint> Menus { get; } = [];
        public HashSet<uint> Texts { get; } = [];
        public HashSet<uint> Trainers { get; } = [];
        public Dictionary<uint, (string, string)> Broadcast { get; } = [];
        public Dictionary<uint, List<Row>> References { get; } = [];

        public ItemInfo? Item(uint id) => Items.Contains(id) ? new(id, "Linen Cloth", 1, 10, 1) : null;
        public QuestInfo? Quest(uint id) => Quests.Contains(id) ? new(id, "Known quest", 1, 1, "", "", 0, []) : null;
        public string? SpellName(uint id) => Spells.Contains(id) ? "Fireball" : null;
        public bool ExtendedCostExists(uint id) => ExtendedCosts.Contains(id);
        public (string Male, string Female)? BroadcastText(uint id) => Broadcast.TryGetValue(id, out var text) ? text : null;
        public bool PoiExists(uint id) => Pois.Contains(id);
        public bool MenuExists(uint menuId) => Menus.Contains(menuId);
        public bool TextExists(uint textId) => Texts.Contains(textId);
        public bool TrainerExists(uint id) => Trainers.Contains(id);
        public IReadOnlyList<Row> ReferenceVendor(uint entry) => References.TryGetValue(entry, out var rows) ? rows : [];
        public int MenuUsers(uint menuId) => 1;
        public int TextUsers(uint textId) => 1;
        public int TrainerUsers(uint trainerId) => 1;
    }

    static Row Template(uint npcflag = 0, uint menu = 0) => Row.Of(("entry", Entry), ("npcflag", npcflag), ("gossip_menu_id", menu), ("subname", ""));

    static RowSet Rows(Row template)
    {
        var rows = new RowSet();
        rows.Set(InteractionTables.CreatureTemplate, template);
        return rows;
    }

    static NpcInteraction Build(RowSet rows, Lookup lookup, RowSet? snapshot = null) =>
        NpcInteraction.Build(Entry, 1, "Test NPC", "", "", 1, snapshot ?? rows.Clone(), rows, lookup, [], new Dictionary<int, int>());

    static Row Option(uint menu, uint id, uint action = 0, uint type = 1, uint icon = 0, uint flag = 1, uint poi = 0, int broadcast = 0, string text = "Hi") =>
        Row.Of(("MenuID", menu), ("OptionID", id), ("OptionIcon", icon), ("OptionText", text), ("OptionBroadcastTextID", broadcast), ("OptionType", type), ("OptionNpcFlag", flag), ("ActionMenuID", action), ("ActionPoiID", poi), ("BoxCoded", 0u), ("BoxMoney", 0u), ("BoxText", ""));

    static Row MenuText(uint menu, uint text) => Row.Of(("MenuID", menu), ("TextID", text));

    static Row Text(uint id, string male = "Hello", int broadcast = 0) => Row.Of(("ID", id), ("text0_0", male), ("text0_1", "Hello f"), ("BroadcastTextID0", broadcast), ("Probability0", 1f));

    static Row Vendor(uint entry, int item, uint cost = 0, int slot = 0, uint max = 0, uint incr = 0) => Row.Of(("entry", entry), ("item", item), ("ExtendedCost", cost), ("slot", slot), ("maxcount", max), ("incrtime", incr));

    static void AssertError(NpcInteraction view, string fragment) =>
        Assert.Contains(view.Issues, i => i.Error && i.Message.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public void MissingTemplateThrows()
    {
        Assert.Throws<InvalidDataException>(() => Build(new RowSet(), new Lookup()));
    }

    [Fact]
    public void PlainTemplateHasNoIssues()
    {
        var view = Build(Rows(Template()), new Lookup());
        Assert.Empty(view.Issues);
        Assert.Empty(view.Menus);
        Assert.Null(view.Trainer);
        Assert.Equal(0u, view.NpcFlag);
    }

    [Fact]
    public void MenusFollowActionMenusOnceAndStopAtTheBound()
    {
        var rows = Rows(Template(NpcFlags.Gossip, 1));
        rows.Set(InteractionTables.GossipOption, Option(1, 0, action: 2));
        rows.Set(InteractionTables.GossipOption, Option(2, 0, action: 1));
        rows.Set(InteractionTables.GossipMenu, MenuText(1, 10));
        rows.Set(InteractionTables.NpcText, Text(10));
        var lookup = new Lookup();
        lookup.Menus.Add(2);
        var view = Build(rows, lookup);
        Assert.Equal([1u, 2u], view.Menus.Keys.Order());

        // A chain longer than the bound is cut, never looped.
        var long_ = Rows(Template(NpcFlags.Gossip, 1));
        for (uint i = 1; i <= NpcInteraction.MaxMenus + 10; i++)
        {
            long_.Set(InteractionTables.GossipOption, Option(i, 0, action: i + 1));
        }

        Assert.Equal(NpcInteraction.MaxMenus, Build(long_, new Lookup()).Menus.Count);
    }

    [Fact]
    public void OptionsAreOrderedAndFlagMismatchHidesThem()
    {
        var rows = Rows(Template(NpcFlags.Gossip, 1));
        rows.Set(InteractionTables.GossipOption, Option(1, 2, flag: NpcFlags.Gossip));
        rows.Set(InteractionTables.GossipOption, Option(1, 1, flag: NpcFlags.Vendor));
        var options = Build(rows, new Lookup()).Menus[1].Options;
        Assert.Equal([1u, 2u], options.Select(o => o.OptionId));
        Assert.True(options[0].HiddenByFlags);
        Assert.False(options[1].HiddenByFlags);
    }

    [Fact]
    public void BroadcastTextOverridesNpcTextAndOptionText()
    {
        var rows = Rows(Template(NpcFlags.Gossip, 1));
        rows.Set(InteractionTables.GossipMenu, MenuText(1, 10));
        rows.Set(InteractionTables.NpcText, Text(10, "raw", 500));
        rows.Set(InteractionTables.GossipOption, Option(1, 0, broadcast: 600, text: "raw option"));
        var lookup = new Lookup();
        lookup.Broadcast[500] = ("from broadcast", "f");
        lookup.Broadcast[600] = ("option broadcast", "f");
        var menu = Build(rows, lookup).Menus[1];
        Assert.Equal("from broadcast", menu.Texts[0].Male);
        Assert.True(menu.Texts[0].FromBroadcast);
        Assert.Equal("option broadcast", menu.Options[0].Text);
        Assert.True(menu.Options[0].FromBroadcast);

        // An unresolvable broadcast ID falls back to the row text.
        lookup.Broadcast.Clear();
        menu = Build(rows, lookup).Menus[1];
        Assert.Equal("raw", menu.Texts[0].Male);
        Assert.False(menu.Texts[0].FromBroadcast);
    }

    [Fact]
    public void UnloadedNpcTextIsShownAndReportedUnlessTheDatabaseHasIt()
    {
        var rows = Rows(Template(NpcFlags.Gossip, 1));
        rows.Set(InteractionTables.GossipMenu, MenuText(1, 99));
        var view = Build(rows, new Lookup());
        Assert.Contains("not loaded", view.Menus[1].Texts[0].Male);
        AssertError(view, "npc_text 99");
        var lookup = new Lookup();
        lookup.Texts.Add(99);
        Assert.DoesNotContain(Build(rows, lookup).Issues, i => i.Error);
    }

    [Fact]
    public void EditedMarksCompareAgainstTheSnapshot()
    {
        var snapshot = Rows(Template(NpcFlags.Gossip, 1));
        snapshot.Set(InteractionTables.GossipMenu, MenuText(1, 10));
        snapshot.Set(InteractionTables.NpcText, Text(10));
        snapshot.Set(InteractionTables.GossipOption, Option(1, 0));
        var rows = snapshot.Clone();
        var lookup = new Lookup();
        Assert.False(Build(rows, lookup, snapshot).Menus[1].Edited);
        rows.Set(InteractionTables.GossipOption, Option(1, 0, text: "changed"));
        var view = Build(rows, lookup, snapshot).Menus[1];
        Assert.True(view.Edited);
        Assert.True(view.Options[0].Edited);
        Assert.False(view.Texts[0].Edited);
        rows.Set(InteractionTables.NpcText, Text(10, "changed"));
        Assert.True(Build(rows, lookup, snapshot).Menus[1].Texts[0].Edited);
    }

    [Theory]
    [InlineData(21u, 0u, 0u, "option type 21 is unknown")]
    [InlineData(0u, 20u, 0u, "")]
    [InlineData(0u, 21u, 0u, "icon 21 is invalid")]
    [InlineData(0u, 14u, 0u, "icon 14 is invalid")]
    [InlineData(0u, 15u, 0u, "icon 15 is invalid")]
    [InlineData(0u, 0u, 77u, "point of interest 77")]
    public void OptionRules(uint type, uint icon, uint poi, string fragment)
    {
        var rows = Rows(Template(NpcFlags.Gossip, 1));
        rows.Set(InteractionTables.GossipOption, Option(1, 0, type: type == 0 ? 1 : type, icon: icon, poi: poi));
        var view = Build(rows, new Lookup());
        if (fragment.Length == 0)
        {
            Assert.DoesNotContain(view.Issues, i => i.Error);
            return;
        }

        AssertError(view, fragment);
    }

    [Fact]
    public void SubmenuMustExistInRowsOrDatabase()
    {
        var rows = Rows(Template(NpcFlags.Gossip, 1));
        rows.Set(InteractionTables.GossipOption, Option(1, 0, action: 5));
        AssertError(Build(rows, new Lookup()), "submenu 5");
        var lookup = new Lookup();
        lookup.Menus.Add(5);
        Assert.DoesNotContain(Build(rows, lookup).Issues, i => i.Error);
    }

    [Fact]
    public void QuestLinksAreSortedAndMissingQuestsAreErrors()
    {
        var rows = Rows(Template(NpcFlags.QuestGiver));
        rows.Set(InteractionTables.QuestStarter, Row.Of(("id", Entry), ("quest", 9u)));
        rows.Set(InteractionTables.QuestStarter, Row.Of(("id", Entry), ("quest", 7u)));
        rows.Set(InteractionTables.QuestStarter, Row.Of(("id", 99u), ("quest", 3u)));
        rows.Set(InteractionTables.QuestEnder, Row.Of(("id", Entry), ("quest", 7u)));
        var view = Build(rows, new Lookup());
        Assert.Equal([7u, 9u], view.Starts.Select(q => q.Quest));
        Assert.Single(view.Ends);
        Assert.Single(view.Issues.Where(i => i.Error));
        AssertError(view, "Quest 9 does not exist");
    }

    [Fact]
    public void VendorRowsAreOrderedAndReferencesExpand()
    {
        var rows = Rows(Template(NpcFlags.Vendor));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, 2589, slot: 2));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, -500, slot: 1));
        var lookup = new Lookup();
        lookup.References[500] = [Vendor(500, 2589), Vendor(500, -3)];
        var view = Build(rows, lookup);
        Assert.Equal(2, view.Vendor.Length);
        Assert.Equal(500u, view.Vendor[0].FromReference);
        Assert.Null(view.Vendor[0].Row);
        Assert.Equal(0u, view.Vendor[1].FromReference);
        Assert.Empty(view.Issues);

        // Referenced items are edited elsewhere, so they are not validated here.
        lookup.Items.Clear();
        Assert.Single(Build(rows, lookup).Issues);
    }

    [Fact]
    public void EmptyVendorReferenceIsAnError()
    {
        var rows = Rows(Template(NpcFlags.Vendor));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, -500));
        AssertError(Build(rows, new Lookup()), "reference 500 has no items");
    }

    [Theory]
    [InlineData(0u, 0u, 0u, false, "")]
    [InlineData(5u, 0u, 0u, true, "extended cost 5")]
    [InlineData(0u, 5u, 5u, false, "")]
    [InlineData(0u, 5u, 0u, true, "restock time")]
    [InlineData(0u, 0u, 60u, true, "limited stock")]
    public void VendorItemRules(uint cost, uint max, uint incr, bool error, string fragment)
    {
        var rows = Rows(Template(NpcFlags.Vendor));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, 2589, cost: cost, max: max, incr: incr));
        var lookup = new Lookup();
        lookup.ExtendedCosts.Add(0);
        var view = Build(rows, lookup);
        Assert.Equal(error, view.Issues.Any(i => i.Error));
        if (error)
        {
            AssertError(view, fragment);
        }
    }

    [Fact]
    public void UnknownVendorItemIsAnError()
    {
        var rows = Rows(Template(NpcFlags.Vendor));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, 31337));
        AssertError(Build(rows, new Lookup()), "item 31337");
    }

    [Fact]
    public void TrainerViewAndRules()
    {
        var rows = Rows(Template(NpcFlags.Trainer));
        rows.Set(InteractionTables.DefaultTrainer, Row.Of(("CreatureId", Entry), ("TrainerId", 8u)));
        var lookup = new Lookup();
        AssertError(Build(rows, lookup), "Trainer 8 does not exist");
        rows.Set(InteractionTables.Trainer, Row.Of(("Id", 8u), ("Type", 1u), ("Requirement", 0u), ("Greeting", "Hi")));
        rows.Set(InteractionTables.TrainerSpell, Row.Of(("TrainerId", 8u), ("SpellId", 133u), ("MoneyCost", 10u), ("ReqSkillLine", 0u), ("ReqSkillRank", 0u), ("ReqLevel", 5u)));
        rows.Set(InteractionTables.TrainerSpell, Row.Of(("TrainerId", 8u), ("SpellId", 134u), ("MoneyCost", 10u), ("ReqSkillLine", 0u), ("ReqSkillRank", 0u), ("ReqLevel", 1u)));
        var view = Build(rows, lookup);
        Assert.Equal("Hi", view.Trainer!.Greeting);
        Assert.True(view.Trainer.Exists);
        Assert.Equal([134u, 133u], view.Trainer.Spells.Select(s => s.SpellId));
        Assert.Equal("Spell 134", view.Trainer.Spells[0].Name);
        AssertError(view, "Trainer spell 134");
        Assert.Single(view.Issues);
    }

    [Fact]
    public void TrainerExistsViaDatabaseWhenNoRowIsLoaded()
    {
        var rows = Rows(Template(NpcFlags.Trainer));
        rows.Set(InteractionTables.DefaultTrainer, Row.Of(("CreatureId", Entry), ("TrainerId", 8u)));
        var lookup = new Lookup();
        lookup.Trainers.Add(8);
        var view = Build(rows, lookup);
        Assert.True(view.Trainer!.Exists);
        Assert.Empty(view.Issues);
    }

    [Fact]
    public void SpellClickMustReferenceAKnownSpell()
    {
        var rows = Rows(Template(NpcFlags.SpellClick));
        rows.Set(InteractionTables.SpellClick, Row.Of(("npc_entry", Entry), ("spell_id", 134u), ("cast_flags", 1u), ("user_type", 0u)));
        AssertError(Build(rows, new Lookup()), "Spell click 134");
        rows.Set(InteractionTables.SpellClick, Row.Of(("npc_entry", Entry), ("spell_id", 133u), ("cast_flags", 1u), ("user_type", 0u)));
        Assert.Equal(2, Build(rows, new Lookup()).SpellClicks.Length);
    }

    [Theory]
    [InlineData("quest", NpcFlags.QuestGiver)]
    [InlineData("vendor", NpcFlags.Vendor)]
    [InlineData("trainer", NpcFlags.Trainer)]
    [InlineData("spellclick", NpcFlags.SpellClick)]
    [InlineData("gossip", NpcFlags.Gossip)]
    public void MissingNpcFlagWarnsWithAFixThatSetsTheBit(string feature, uint bit)
    {
        var rows = Rows(Template(0));
        var lookup = new Lookup();
        lookup.Trainers.Add(8);
        switch (feature)
        {
            case "quest":
                rows.Set(InteractionTables.QuestStarter, Row.Of(("id", Entry), ("quest", 7u)));
                break;
            case "vendor":
                rows.Set(InteractionTables.Vendor, Vendor(Entry, 2589));
                break;
            case "trainer":
                rows.Set(InteractionTables.DefaultTrainer, Row.Of(("CreatureId", Entry), ("TrainerId", 8u)));
                break;
            case "spellclick":
                rows.Set(InteractionTables.SpellClick, Row.Of(("npc_entry", Entry), ("spell_id", 133u), ("cast_flags", 1u), ("user_type", 0u)));
                break;
            default:
                rows.Set(InteractionTables.CreatureTemplate, Template(0, 3));
                break;
        }

        var view = Build(rows, lookup);
        var warning = Assert.Single(view.Issues, i => !i.Error);
        var fix = Assert.Single(warning.Fix!);
        Assert.Equal("creature_template", fix.Table);
        Assert.Equal(bit, fix.After!.U("npcflag") & bit);
        Assert.Equal(view.Template, fix.Before);
    }

    [Fact]
    public void FlagFixPreservesExistingBits()
    {
        var rows = Rows(Template(NpcFlags.Repair));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, 2589));
        var warning = Assert.Single(Build(rows, new Lookup()).Issues, i => !i.Error);
        Assert.Equal(NpcFlags.Repair | NpcFlags.Vendor, warning.Fix![0].After!.U("npcflag"));
    }

    [Theory]
    [InlineData(NpcFlags.Vendor)]
    [InlineData(NpcFlags.VendorFood)]
    [InlineData(NpcFlags.VendorReagent)]
    public void AnyVendorVariantSatisfiesTheVendorFlagRule(uint flag)
    {
        var rows = Rows(Template(flag));
        rows.Set(InteractionTables.Vendor, Vendor(Entry, 2589));
        Assert.Empty(Build(rows, new Lookup()).Issues);
    }

    [Theory]
    [InlineData(NpcFlags.Trainer)]
    [InlineData(NpcFlags.TrainerClass)]
    [InlineData(NpcFlags.TrainerProfession)]
    public void AnyTrainerVariantSatisfiesTheTrainerFlagRule(uint flag)
    {
        var rows = Rows(Template(flag));
        rows.Set(InteractionTables.DefaultTrainer, Row.Of(("CreatureId", Entry), ("TrainerId", 8u)));
        var lookup = new Lookup();
        lookup.Trainers.Add(8);
        Assert.Empty(Build(rows, lookup).Issues);
    }

    [Fact]
    public void FlagNamesAreUniqueSingleBitsAndListedByBit()
    {
        Assert.All(NpcFlags.All, f => Assert.Equal(1, System.Numerics.BitOperations.PopCount(f.Bit)));
        Assert.Equal(NpcFlags.All.Length, NpcFlags.All.Select(f => f.Bit).Distinct().Count());
        Assert.Equal(["Gossip", "Quest giver"], NpcFlags.Names(3));
        Assert.Empty(NpcFlags.Names(0));
        Assert.Equal(NpcFlags.Vendor | NpcFlags.VendorAmmo | NpcFlags.VendorFood | NpcFlags.VendorPoison | NpcFlags.VendorReagent, NpcFlags.VendorMask);
    }

    [Theory]
    [InlineData("Hello $N", "Hello <name>")]
    [InlineData("Line$Bbreak", "Line\nbreak")]
    [InlineData("$C of the $R", "<class> of the <race>")]
    [InlineData("Greetings, $g sir : madam;!", "Greetings, sir !")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void RenderExpandsClientPlaceholders(string raw, string expected)
    {
        Assert.Equal(expected, NpcInteraction.Render(raw));
    }

    [Fact]
    public void RenderPicksTheMaleFormOfGenderedText()
    {
        Assert.Equal("brother", NpcInteraction.Render("$g brother:sister;"));
        Assert.Equal("brother", NpcInteraction.Render("$G brother:sister;"));
        Assert.Equal("Hello brother, friend", NpcInteraction.Render("Hello $gbrother:sister;, friend"));
    }
}
