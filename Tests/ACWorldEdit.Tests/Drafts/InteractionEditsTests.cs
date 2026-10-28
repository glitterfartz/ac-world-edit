using ACWorldEdit.Foundation;
using Xunit;
using static ACWorldEdit.Foundation.InteractionEdits;

/// <summary>
/// Row-edit constructors, default rows, hex/decimal parsing and the interaction summary text.
/// </summary>
public class InteractionEditsTests
{
    static Row Template(uint npcflag = 0) => Row.Of(("entry", 42u), ("npcflag", npcflag), ("gossip_menu_id", 0u), ("subname", ""));

    [Fact]
    public void UpdateInsertDeleteBuildKeyedEdits()
    {
        var table = AcTables.CreatureTemplate;
        var update = Update(table, Template(), Template(1));
        Assert.Equal(("creature_template", "42"), (update.Table, update.Key));
        Assert.Equal(Template(), update.Before);
        Assert.Equal(Template(1), update.After);
        Assert.Null(Insert(table, Template()).Before);
        Assert.Null(Delete(table, Template()).After);
        Assert.Equal("42", Insert(table, Template()).Key);
    }

    [Fact]
    public void NewTextAndNewOptionAreCompleteRowsMatchingTheirSchemas()
    {
        var text = NewText(61, "Hello");
        text.Validate(AcTables.NpcText);
        Assert.Equal("61", AcTables.NpcText.KeyOf(text));
        Assert.Equal("Hello", text.T("text0_0"));
        Assert.Equal(1f, text.F("Probability0"));

        var option = NewOption(50, 3, "Browse", 3, AcNpcFlags.Vendor, 1, 51);
        option.Validate(AcTables.GossipOption);
        Assert.Equal("50|3", AcTables.GossipOption.KeyOf(option));
        Assert.Equal(AcNpcFlags.Vendor, option.U("OptionNpcFlag"));
        Assert.Equal(51u, option.U("ActionMenuID"));
        Assert.Equal(0, option.I("OptionBroadcastTextID"));
    }

    [Theory]
    [InlineData("12", 12u)]
    [InlineData("  12  ", 12u)]
    [InlineData("0x10", 16u)]
    [InlineData("0X1f", 31u)]
    [InlineData(" 0xFFFFFFFF ", uint.MaxValue)]
    [InlineData("0", 0u)]
    public void ParseUIntAcceptsDecimalAndHex(string text, uint expected)
    {
        Assert.Equal(expected, ParseUInt(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("0xZZ")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("4294967296")]
    [InlineData("0x100000000")]
    public void ParseUIntRejectsEverythingElse(string text)
    {
        Assert.ThrowsAny<Exception>(() => ParseUInt(text));
    }

    [Fact]
    public void OptionTypeNamesFallBackForUnknownTypes()
    {
        Assert.Equal("Vendor", OptionTypeName(3));
        Assert.Equal($"Unknown {AcNpcFlags.OptionTypes.Length}", OptionTypeName((uint)AcNpcFlags.OptionTypes.Length));
    }

    sealed class NoLookup : IInteractionLookup
    {
        public ItemInfo? Item(uint id) => id == 2589 ? new(id, "Linen Cloth", 1, 10, 0) : null;
        public QuestInfo? Quest(uint id) => null;
        public string? SpellName(uint id) => null;
        public bool ExtendedCostExists(uint id) => false;
        public (string Male, string Female)? BroadcastText(uint id) => null;
        public bool PoiExists(uint id) => false;
        public bool MenuExists(uint menuId) => false;
        public bool TextExists(uint textId) => false;
        public bool TrainerExists(uint id) => false;
        public IReadOnlyList<Row> ReferenceVendor(uint entry) => [];
        public int MenuUsers(uint menuId) => 1;
        public int TextUsers(uint textId) => 1;
        public int TrainerUsers(uint trainerId) => 1;
    }

    static AcCreatureView Build(RowSet rows, RowSet? snapshot = null, string script = "", ScriptHook[]? hooks = null) =>
        AcCreatureView.Build(42, 1, "Test", script, "", 1, snapshot ?? rows.Clone(), rows, new NoLookup(), hooks ?? [], new Dictionary<int, int>());

    [Fact]
    public void SummaryOfAPlainNpc()
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureTemplate, Template());
        Assert.Equal("No NPC flags\nGossip options 0 · Quests 0/0 · Vendor 0 · Trainer 0", Summary(Build(rows)));
    }

    [Fact]
    public void SummaryListsFlagsScriptsDraftEditsAndErrors()
    {
        var snapshot = new RowSet();
        snapshot.Set(AcTables.CreatureTemplate, Template(AcNpcFlags.Vendor | AcNpcFlags.Gossip));
        var rows = snapshot.Clone();
        var item = Row.Of(("entry", 42u), ("item", 2589), ("ExtendedCost", 0u), ("slot", 0), ("maxcount", 0u), ("incrtime", 0u));
        rows.Set(AcTables.Vendor, item);
        rows.Set(AcTables.QuestStarter, Row.Of(("id", 42u), ("quest", 999u)));
        var hooks = new[] { new ScriptHook(64, 1, 0, 0, "hello") };
        string summary = Summary(Build(rows, snapshot, "npc_script", hooks));
        Assert.StartsWith("Gossip · Vendor\n", summary);
        Assert.Contains("Quests 1/0 · Vendor 1", summary);
        Assert.Contains("\nC++ script: npc_script", summary);
        Assert.Contains("\nSmartAI interaction events: 1", summary);
        Assert.Contains("\nLocal draft changes present.", summary);
        Assert.EndsWith("\n1 validation error(s).", summary);
    }
}
