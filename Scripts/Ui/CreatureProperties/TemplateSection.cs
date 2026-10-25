using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// The creature template: who the creature is, how it fights and moves, what it drops, plus the
/// NPC flags and spell clicks that decide how players interact with it. Every editable template
/// column lives here or on the Class / Level / Stats page, under plain-language labels.
/// </summary>
public sealed partial class TemplateSection(CreatureContext ctx) : InteractionSection(ctx)
{
    static readonly (int Value, string Name)[] Ranks = [(0, "Normal"), (1, "Elite"), (2, "Rare Elite"), (3, "Boss"), (4, "Rare")];
    static readonly (int Value, string Name)[] DamageSchools = [(0, "Physical"), (1, "Holy"), (2, "Fire"), (3, "Nature"), (4, "Frost"), (5, "Shadow"), (6, "Arcane")];
    static readonly (int Value, string Name)[] MovementTypes = [(0, "Stands still"), (1, "Wanders randomly"), (2, "Follows waypoints")];
    static readonly (int Value, string Name)[] Types =
    [
        (0, "None"), (1, "Beast"), (2, "Dragonkin"), (3, "Demon"), (4, "Elemental"), (5, "Giant"), (6, "Undead"), (7, "Humanoid"),
        (8, "Critter"), (9, "Mechanical"), (10, "Not specified"), (11, "Totem"), (12, "Non-combat pet"), (13, "Gas cloud")
    ];
    static readonly (int Value, string Name)[] Families =
    [
        (0, "None"), (1, "Wolf"), (2, "Cat"), (3, "Spider"), (4, "Bear"), (5, "Boar"), (6, "Crocolisk"), (7, "Carrion bird"), (8, "Crab"),
        (9, "Gorilla"), (11, "Raptor"), (12, "Tallstrider"), (15, "Felhunter"), (16, "Voidwalker"), (17, "Succubus"), (19, "Doomguard"),
        (20, "Scorpid"), (21, "Turtle"), (23, "Imp"), (24, "Bat"), (25, "Hyena"), (26, "Bird of prey"), (27, "Wind serpent"),
        (28, "Remote control"), (29, "Felguard"), (30, "Dragonhawk"), (31, "Ravager"), (32, "Warp stalker"), (33, "Sporebat"),
        (34, "Nether ray"), (35, "Serpent"), (37, "Moth"), (38, "Chimaera"), (39, "Devilsaur"), (40, "Ghoul"), (41, "Silithid"),
        (42, "Worm"), (43, "Rhino"), (44, "Wasp"), (45, "Core hound"), (46, "Spirit beast")
    ];

    static readonly (uint Bit, string Name, string Tip)[] UnitFlags =
    [
        (0x2, "Cannot be attacked", "Players cannot target it for attack at all."),
        (0x4, "Cannot move", "The creature is rooted in place."),
        (0x100, "Ignores players", "Neither fights nor helps players. Used for scripted or neutral creatures."),
        (0x200, "Ignores creatures", "Neither fights nor helps other non-player creatures."),
        (0x1000, "PvP flagged", "Treated as flagged for player-versus-player combat."),
        (0x2000, "Silenced", "Cannot cast spells."),
        (0x4000, "Cannot swim", "Will not enter water."),
        (0x20000, "Pacified", "Will not attack, but can still cast spells."),
        (0x40000, "Stunned", "Permanently stunned."),
        (0x200000, "Disarmed", "Cannot use melee attacks."),
        (0x2000000, "Cannot be selected", "Players cannot click or target it. Typical for invisible helper creatures."),
        (0x4000000, "Skinnable", "Shows the skinning sparkle when dead."),
        (0x8000000, "Mounted", "Displayed as riding a mount."),
        (0x40000000, "Weapons sheathed", "Shows with weapons put away."),
        (0x80000000, "Immune to all damage", "Takes no damage from anything.")
    ];

    static readonly (uint Bit, string Name, string Tip)[] UnitFlags2 =
    [
        (0x1, "Feigns death", "Plays the feign-death pose while alive."),
        (0x2, "Hides model", "Hides the creature's model."),
        (0x4, "Ignores reputation", "Reputation does not change how it treats players."),
        (0x8, "Understands all languages", "Can talk to both factions."),
        (0x10, "Mirror image", "Copies the appearance of its summoner."),
        (0x20, "Appears instantly", "Does not fade in when spawned."),
        (0x80, "Off-hand disarmed", "Cannot use its off-hand weapon."),
        (0x400, "Ranged disarmed", "Cannot use its ranged weapon."),
        (0x800, "Regenerates power", "Mana and other power slowly regenerate."),
        (0x1000, "Party-only interaction", "Only the player's party or raid may interact with it."),
        (0x2000, "Blocks spell click", "Players cannot use its spell-click action."),
        (0x4000, "Enemies can interact", "Hostile players can still use it."),
        (0x8000, "Cannot turn", "Keeps its facing fixed."),
        (0x20000, "Special death animation", "Plays its special death animation.")
    ];

    static readonly (uint Bit, string Name, string Tip)[] TypeFlags =
    [
        (0x1, "Tameable", "Hunters can tame it. It must also be a beast with a family."),
        (0x2, "Visible to ghosts", "Dead players can see and interact with it."),
        (0x4, "Boss", "Shows level ?? in its portrait and is immune to knockback."),
        (0x8, "No wound animation", "Does not flinch when it parries."),
        (0x10, "Hide faction tooltip", "Does not show its faction in the tooltip."),
        (0x40, "Spell attackable", "Can be targeted by spells that normally need an attackable unit."),
        (0x80, "Interactable while dead", "Players can interact with the corpse."),
        (0x100, "Herbalism loot", "Corpse can be gathered with Herbalism."),
        (0x200, "Mining loot", "Corpse can be gathered with Mining."),
        (0x400, "No death message", "Its death is not written to the combat log."),
        (0x800, "Mounted combat", "Stays mounted when it enters combat."),
        (0x1000, "Can assist players", "May help nearby players who are fighting."),
        (0x8000, "Engineering loot", "Corpse can be gathered with Engineering."),
        (0x10000, "Exotic pet", "Only Beast Mastery hunters can tame it."),
        (0x100000, "Hide nameplate", "Does not show a name above its head."),
        (0x8000000, "Show one gossip option", "Displays a single gossip option even if the flags would not."),
        (0x10000000, "Never sheathe weapons", "Keeps its weapons drawn.")
    ];

    static readonly (uint Bit, string Name, string Tip)[] ExtraFlags =
    [
        (0x1, "Binds instance on kill", "Killing it locks the killer and their group to the instance."),
        (0x2, "Civilian", "Never initiates combat, regardless of faction."),
        (0x4, "Cannot parry", "Attacks against it cannot be parried."),
        (0x10, "Cannot block", "Attacks against it cannot be blocked."),
        (0x20, "No crushing blows", "Its attacks cannot be crushing blows."),
        (0x40, "Gives no experience", "Killing it grants no XP."),
        (0x80, "Trigger", "Invisible helper creature used by scripts."),
        (0x100, "Immune to taunt", "Taunt effects do nothing to it."),
        (0x400, "Ghost visibility", "Only visible to dead players."),
        (0x800, "Uses off-hand attack", "Attacks with a second weapon."),
        (0x1000, "Vendor will not buy", "Players cannot sell items to it."),
        (0x2000, "Cannot enter combat", "Never fights."),
        (0x8000, "Guard", "Behaves as a city or town guard."),
        (0x10000, "Ignores feign death", "Is not fooled by Feign Death."),
        (0x20000, "Cannot critically strike", "Its attacks never crit."),
        (0x40000, "No skill gains", "Fighting it does not train weapon skills."),
        (0x200000, "No damage needed for credit", "Kill credit does not require the player to have damaged it."),
        (0x800000, "Cannot be dodged", "Attacks against it cannot be dodged."),
        (0x2000000, "Does not call for help", "Does not summon nearby allies when it aggroes."),
        (0x4000000, "Ignores calls for help", "Does not join fights started by nearby allies."),
        (0x20000000, "Ignores pathfinding", "Moves in straight lines instead of following the navigation mesh."),
        (0x40000000, "Immune to knockback", "Cannot be knocked back.")
    ];

    GridContainer grid = null!;

    public override string Id => "template";
    public override string Title => "Template";
    public override string IconPath => "Interface/Icons/INV_Misc_Book_09.blp";

    protected override void Populate(NpcInteraction v)
    {
        var details = effective.Get(InteractionTables.CreatureDetails, v.Entry.ToString(CultureInfo.InvariantCulture));
        if (details == null)
        {
            body.AddChild(ClientStyle.Text("This creature has no template row.", 14, ClientStyle.Faded));
            return;
        }

        if (editable)
        {
            body.AddChild(ClientStyle.Text($"The template applies to all {v.SpawnCount} spawn(s) of entry {v.Entry}.", 11, ClientStyle.Warning));
        }

        RenderIdentity(v, details);
        RenderInteraction(v);
        RenderCombat(details);
        RenderMovement(details);
        RenderLoot(details);
        RenderLinks(details);
        RenderFlags(details);
        RenderIssues(v);
    }

    void Group(string title, string? hint = null)
    {
        body.AddChild(ClientStyle.Text(title, 16, ClientStyle.Header));
        if (hint != null)
        {
            body.AddChild(ClientStyle.Text(hint, 12, ClientStyle.Faded));
        }

        NewGrid();
    }

    void NewGrid()
    {
        grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        body.AddChild(grid);
    }

    void Add(string label, string tip, string shown, Func<Control> editor)
    {
        grid.AddChild(Caption(label, tip));
        var control = editable ? editor() : ClientStyle.Text(shown, 14);
        control.TooltipText = tip;
        grid.AddChild(control);
    }

    void RenderIdentity(NpcInteraction v, Row d)
    {
        Group("Identity");
        Add("Name", "The name players see above the creature.", d.T("name"), () => TextBox(d, "name", "Name", 100, "Creature name"));
        Add("Title", "The text in angle brackets under the name, such as <Weapon Merchant>. Leave blank for none.", v.Subname.Length > 0 ? $"<{v.Subname}>" : "None", SubnameBox);
        Add("Creature type", "The creature's kind. Some spells, abilities and quest objectives only work on certain types, such as Banish on demons.", NameOf(Types, (int)d.U("type")), () => Choice(d, "type", "creature type", Types, false));
        Add("Pet family", "Which hunter pet family the creature belongs to. Only matters for tameable beasts.", NameOf(Families, d.I("family")), () => Choice(d, "family", "pet family", Families, true));
        Add("Classification", "Normal, Elite, Rare Elite, Boss or Rare. Decides the dragon frame around its portrait and some combat rules.", NameOf(Ranks, (int)d.U("rank")), () => Choice(d, "rank", "classification", Ranks, false));
        Add("Faction", "Faction template ID (from FactionTemplate.dbc). Decides who the creature is friendly or hostile to; 35 is friendly to everyone, 14 is hostile to everyone.", d.U("faction").ToString(CultureInfo.InvariantCulture), () => Number(d, "faction", "Faction", 65535));
        Add("Racial leader", "Marks the creature as the leader of its race, which affects some player titles and reputation rewards.", Flag(d.U("RacialLeader")), () => Check(d, "RacialLeader", "Racial leader"));
        Add("Mouse cursor icon", "Name of the cursor icon shown when hovering over the creature, such as Speak, Taxi, Buy or Directions. Blank uses the default.", d.T("IconName").Length > 0 ? d.T("IconName") : "Default", () => TextBox(d, "IconName", "Cursor icon", 100, "Default cursor"));
    }

    void RenderInteraction(NpcInteraction v)
    {
        var template = TemplateRow;
        body.AddChild(ClientStyle.Text("Interaction", 16, ClientStyle.Header));
        body.AddChild(Caption("NPC services", "What players can do by right-clicking: talk, buy, train, take quests and so on. Each ticked service must be backed by the matching records on the other pages."));
        if (editable)
        {
            var services = new GridContainer
            {
                Columns = 3
            };
            body.AddChild(services);
            foreach (var (bit, name) in NpcFlags.All)
            {
                var check = new CheckBox
                {
                    Text = name,
                    ButtonPressed = (v.NpcFlag & bit) != 0
                };
                check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {name} flag", [Update(InteractionTables.CreatureTemplate, template, template.With("npcflag", on ? template.U("npcflag") | bit : template.U("npcflag") & ~bit))]);
                services.AddChild(check);
            }
        }
        else
        {
            body.AddChild(ClientStyle.Text(v.NpcFlag == 0 ? "None (right-click does nothing)" : string.Join(", ", NpcFlags.Names(v.NpcFlag)), 14));
        }

        NewGrid();
        Add("Gossip menu", "ID of the gossip menu shown when players talk to it. Zero means no menu. The menu itself is edited on the Gossip page.", v.GossipMenuId.ToString(CultureInfo.InvariantCulture), GossipMenuBox);

        if ((v.NpcFlag & NpcFlags.FlightMaster) != 0 && catalog != null && target != null && catalog.NearestTaxiNode(target.Pose.Map, target.Pose.X, target.Pose.Y, target.Pose.Z) is { } node)
        {
            body.AddChild(ClientStyle.Text($"Flight master: nearest taxi node {node.Name} ({node.Id}), {node.Distance:0} yd.", 14));
        }

        body.AddChild(Caption("Spell click", "Spells that cast when a player clicks the creature, used for rideable vehicles and similar. Each entry names the spell, who casts it and on whom."));
        if (v.SpellClicks.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var click in v.SpellClicks)
        {
            body.AddChild(ClientStyle.Text($"{click.Name}   ·   cast flags {click.CastFlags}   ·   user type {click.UserType}", 14, click.Edited ? ClientStyle.Edited : null));
        }

        if (editable)
        {
            RenderSpellClickEditor(v);
        }
    }

    void RenderCombat(Row d)
    {
        Group("Combat");
        Add("Damage type", "The school of magic the creature's melee attacks deal. Physical damage is reduced by armor, other schools by resistance.", NameOf(DamageSchools, d.I("dmgschool")), () => Choice(d, "dmgschool", "damage type", DamageSchools, true));
        Add("Melee attack interval", "Milliseconds between melee swings. Lower is faster; 2000 is the usual default.", $"{d.U("BaseAttackTime")} ms", () => Number(d, "BaseAttackTime", "Melee attack interval", uint.MaxValue));
        Add("Ranged attack interval", "Milliseconds between ranged attacks.", $"{d.U("RangeAttackTime")} ms", () => Number(d, "RangeAttackTime", "Ranged attack interval", uint.MaxValue));
        Add("Melee damage spread", "How much melee hits vary. 1 uses the normal damage spread; 0 makes every hit the same size.", Shown(d.F("BaseVariance")), () => Decimal(d, "BaseVariance", "Melee damage spread"));
        Add("Ranged damage spread", "How much ranged hits vary, like the melee damage spread.", Shown(d.F("RangeVariance")), () => Decimal(d, "RangeVariance", "Ranged damage spread"));
        Add("Aggro range", "Distance in yards at which the creature notices players and attacks. Higher level differences shrink or grow it.", Shown(d.F("detection_range")) + " yd", () => Decimal(d, "detection_range", "Aggro range"));
        Add("Regenerates health", "Whether it heals itself when out of combat. Turn off for creatures that must stay wounded.", Flag(d.U("RegenHealth")), () => Check(d, "RegenHealth", "Regenerates health"));
        Add("Experience multiplier", "Multiplies the experience a player earns for killing it. 1 is normal, 0 gives none.", Shown(d.F("ExperienceModifier")), () => Decimal(d, "ExperienceModifier", "Experience multiplier"));
        Add("Immunities set", "ID of a set of immunities to spells, mechanics and schools. Zero means none. Negative values are not used.", d.I("CreatureImmunitiesId").ToString(CultureInfo.InvariantCulture), () => Whole(d, "CreatureImmunitiesId", "Immunities set"));
    }

    void RenderMovement(Row d)
    {
        Group("Movement", "Speeds are multiples of the normal rate: 1 is a player's walking pace, 1.14286 is the usual run.");
        Add("Movement behaviour", "What it does when it is not fighting.", NameOf(MovementTypes, (int)d.U("MovementType")), () => Choice(d, "MovementType", "movement behaviour", MovementTypes, false));
        Add("Walk speed", "Speed when walking.", Shown(d.F("speed_walk")), () => Decimal(d, "speed_walk", "Walk speed"));
        Add("Run speed", "Speed when running, such as while chasing a target.", Shown(d.F("speed_run")), () => Decimal(d, "speed_run", "Run speed"));
        Add("Swim speed", "Speed while swimming.", Shown(d.F("speed_swim")), () => Decimal(d, "speed_swim", "Swim speed"));
        Add("Flight speed", "Speed while flying.", Shown(d.F("speed_flight")), () => Decimal(d, "speed_flight", "Flight speed"));
        Add("Hover height", "How far above the ground the creature hovers, in yards. 1 is the normal standing height.", Shown(d.F("HoverHeight")), () => Decimal(d, "HoverHeight", "Hover height"));
        Add("Movement style ID", "Selects a movement style from the server's creature movement table, such as always walking. Zero uses the default.", d.U("movementId").ToString(CultureInfo.InvariantCulture), () => Number(d, "movementId", "Movement style ID", uint.MaxValue));
    }

    void RenderLoot(Row d)
    {
        Group("Loot");
        Add("Loot table", "ID of the loot table rolled when the creature dies. Zero drops nothing. It matches the 'entry' of its loot rows.", d.U("lootid").ToString(CultureInfo.InvariantCulture), () => Number(d, "lootid", "Loot table", uint.MaxValue));
        Add("Pickpocket table", "ID of the loot table a rogue can steal with Pickpocket. Zero means it cannot be pickpocketed.", d.U("pickpocketloot").ToString(CultureInfo.InvariantCulture), () => Number(d, "pickpocketloot", "Pickpocket table", uint.MaxValue));
        Add("Skinning table", "ID of the loot table gathered by skinning, mining or herbalism. Zero means it cannot be skinned.", d.U("skinloot").ToString(CultureInfo.InvariantCulture), () => Number(d, "skinloot", "Skinning table", uint.MaxValue));
        Add("Minimum money", "Lowest amount of money it can drop, in copper (100 copper = 1 silver, 100 silver = 1 gold).", Money(d.U("mingold")), () => Number(d, "mingold", "Minimum money", uint.MaxValue));
        Add("Maximum money", "Highest amount of money it can drop, in copper.", Money(d.U("maxgold")), () => Number(d, "maxgold", "Maximum money", uint.MaxValue));
    }

    void RenderLinks(Row d)
    {
        Group("Links and scripting");
        Add("Kill credit 1", "A second creature entry that also counts as killed when this one dies, for quests that name another creature. Zero for none.", d.U("KillCredit1").ToString(CultureInfo.InvariantCulture), () => Number(d, "KillCredit1", "Kill credit 1", uint.MaxValue));
        Add("Kill credit 2", "A third creature entry that also counts as killed. Zero for none.", d.U("KillCredit2").ToString(CultureInfo.InvariantCulture), () => Number(d, "KillCredit2", "Kill credit 2", uint.MaxValue));
        Add("Heroic version", "Entry used for this creature in heroic 10-player or 25-player normal dungeons and raids. Zero for none.", d.U("difficulty_entry_1").ToString(CultureInfo.InvariantCulture), () => Number(d, "difficulty_entry_1", "Heroic version", uint.MaxValue));
        Add("25-player heroic version", "Entry used in 25-player heroic raids. Zero for none.", d.U("difficulty_entry_2").ToString(CultureInfo.InvariantCulture), () => Number(d, "difficulty_entry_2", "25-player heroic version", uint.MaxValue));
        Add("Third difficulty version", "Entry used for a further difficulty setting. Zero for none.", d.U("difficulty_entry_3").ToString(CultureInfo.InvariantCulture), () => Number(d, "difficulty_entry_3", "Third difficulty version", uint.MaxValue));
        Add("Vehicle ID", "ID of the vehicle seat layout if players can ride it. Zero if it is not a vehicle.", d.U("VehicleId").ToString(CultureInfo.InvariantCulture), () => Number(d, "VehicleId", "Vehicle ID", uint.MaxValue));
        Add("Pet spell set", "ID of the set of spells it offers on a hunter's or warlock's pet bar. Zero for none.", d.U("PetSpellDataId").ToString(CultureInfo.InvariantCulture), () => Number(d, "PetSpellDataId", "Pet spell set", uint.MaxValue));
        Add("C++ script", "Name of a compiled server script that drives the creature. It can replace gossip, quests and SmartAI entirely, so leave blank unless the script exists.", d.T("ScriptName").Length > 0 ? d.T("ScriptName") : "None", () => TextBox(d, "ScriptName", "C++ script", 64, "None"));
    }

    void RenderFlags(Row d)
    {
        body.AddChild(ClientStyle.Text("Flags", 16, ClientStyle.Header));
        body.AddChild(ClientStyle.Text("Tick the behaviours that apply. The raw value keeps any bits the list does not name.", 12, ClientStyle.Faded));
        FlagSet(d, "unit_flags", "State flags", "Basic state and combat switches, such as being unattackable or unselectable.", UnitFlags);
        FlagSet(d, "unit_flags2", "More state flags", "Additional state switches, such as hiding the model or ignoring reputation.", UnitFlags2);
        FlagSet(d, "type_flags", "Creature type flags", "Tags on the kind of creature: tameable, boss portrait, gatherable corpse and so on.", TypeFlags);
        FlagSet(d, "flags_extra", "Server rules", "Special rules the server applies to it, such as no parry, civilian or guard.", ExtraFlags);
        FlagSet(d, "dynamicflags", "Dynamic flags", "Runtime display state such as lootable or tapped. The server sets these while the game runs, so they are normally left at 0.", []);
    }

    void FlagSet(Row d, string column, string title, string tip, (uint Bit, string Name, string Tip)[] known)
    {
        uint value = d.U(column);
        body.AddChild(Caption(title, tip));
        if (!editable)
        {
            var names = known.Where(f => (value & f.Bit) != 0).Select(f => f.Name).ToList();
            uint unnamed = value & ~known.Aggregate(0u, (all, f) => all | f.Bit);
            if (unnamed != 0)
            {
                names.Add($"other bits 0x{unnamed:X}");
            }

            var text = ClientStyle.Text(names.Count == 0 ? "None" : string.Join(", ", names), 14);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            body.AddChild(text);
            return;
        }

        if (known.Length > 0)
        {
            var checks = new GridContainer
            {
                Columns = 3
            };
            body.AddChild(checks);
            foreach (var (bit, name, flagTip) in known)
            {
                var check = new CheckBox
                {
                    Text = name,
                    TooltipText = flagTip,
                    ButtonPressed = (value & bit) != 0
                };
                check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {name.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, on ? value | bit : value & ~bit))]);
                checks.AddChild(check);
            }
        }

        var raw = new HBoxContainer();
        body.AddChild(raw);
        raw.AddChild(ClientStyle.Tag("Raw value"));
        var edit = new LineEdit
        {
            Text = $"0x{value:X}",
            CustomMinimumSize = new(110, 0),
            TooltipText = "The combined number stored in the database. Decimal or 0x hex."
        };
        edit.TextSubmitted += text => Try(() => Commit($"Change {title.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, ParseUInt(text)))]));
        raw.AddChild(edit);
    }

    static string NameOf((int Value, string Name)[] options, int value) => options.FirstOrDefault(o => o.Value == value).Name ?? $"Unknown ({value})";

    static string Flag(uint value) => value != 0 ? "Yes" : "No";

    static string Money(uint copper) => copper == 0 ? "None" : $"{copper / 10000}g {copper / 100 % 100}s {copper % 100}c";

    OptionButton Choice(Row d, string column, string label, (int Value, string Name)[] options, bool signed)
    {
        int current = signed ? d.I(column) : (int)d.U(column);
        var picker = new OptionButton();
        foreach (var (value, name) in options)
        {
            picker.AddItem(name, value);
        }

        int index = picker.GetItemIndex(current);
        if (index < 0)
        {
            picker.AddItem($"Unknown ({current})", current);
            index = picker.ItemCount - 1;
        }

        picker.Selected = index;
        picker.ItemSelected += i =>
        {
            int chosen = picker.GetItemId((int)i);
            object value = signed ? chosen : (uint)chosen;
            Commit($"Change {label}", [Update(InteractionTables.CreatureDetails, d, d.With(column, value))]);
        };
        return picker;
    }

    CheckBox Check(Row d, string column, string label)
    {
        var check = new CheckBox
        {
            ButtonPressed = d.U(column) != 0
        };
        check.Toggled += on => Commit($"{(on ? "Enable" : "Disable")} {label.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, on ? 1u : 0u))]);
        return check;
    }

    LineEdit Number(Row d, string column, string label, uint max)
    {
        var edit = new LineEdit
        {
            Text = d.U(column).ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Try(() =>
        {
            uint value = ParseUInt(text);
            if (value > max)
            {
                Reject($"{label} cannot be more than {max}.");
                return;
            }

            Commit($"Change {label.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, value))]);
        });
        return edit;
    }

    LineEdit Whole(Row d, string column, string label)
    {
        var edit = new LineEdit
        {
            Text = d.I(column).ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Try(() => Commit($"Change {label.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, int.Parse(text.Trim(), CultureInfo.InvariantCulture)))]));
        return edit;
    }

    LineEdit Decimal(Row d, string column, string label)
    {
        var edit = new LineEdit
        {
            Text = Shown(d.F(column)),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Try(() =>
        {
            float value = float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!float.IsFinite(value) || value < 0)
            {
                Reject($"{label} must be zero or more.");
                return;
            }

            Commit($"Change {label.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, value))]);
        });
        return edit;
    }

    LineEdit TextBox(Row d, string column, string label, int maxLength, string placeholder)
    {
        var edit = new LineEdit
        {
            Text = d.T(column),
            MaxLength = maxLength,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(220, 0)
        };
        edit.TextSubmitted += text =>
        {
            if (column == "name" && text.Trim().Length == 0)
            {
                Reject("The name cannot be empty.");
                return;
            }

            Commit($"Change {label.ToLowerInvariant()}", [Update(InteractionTables.CreatureDetails, d, d.With(column, text.Trim()))]);
        };
        return edit;
    }

    LineEdit SubnameBox()
    {
        var template = TemplateRow;
        var edit = new LineEdit
        {
            Text = template.T("subname"),
            MaxLength = 100,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(220, 0),
            PlaceholderText = "None"
        };
        edit.TextSubmitted += text => Commit("Change title", [Update(InteractionTables.CreatureTemplate, template, template.With("subname", text.Trim()))]);
        return edit;
    }

    LineEdit GossipMenuBox()
    {
        var edit = new LineEdit
        {
            Text = TemplateRow.U("gossip_menu_id").ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Try(() => _ = SetRootMenu(ParseUInt(text)));
        return edit;
    }

    void Reject(string message)
    {
        status = $"Not saved: {message}";
        Render();
    }

    void RenderIssues(NpcInteraction v)
    {
        if (v.Issues.Length == 0)
        {
            return;
        }

        body.AddChild(ClientStyle.Text("Validation", 16, ClientStyle.Header));
        foreach (var issue in v.Issues)
        {
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Text((issue.Error ? "Error: " : "Warning: ") + issue.Message, 13, issue.Error ? ClientStyle.Error : ClientStyle.Warning));
            if (issue.Fix != null && draft != null)
            {
                var fix = new Button
                {
                    Text = "Fix"
                };
                var edits = issue.Fix;
                fix.Pressed += () => Commit("Fix npcflag", edits);
                row.AddChild(fix);
            }

            body.AddChild(row);
        }
    }

    async Task SetRootMenu(uint menu)
    {
        if (menu != 0 && !effective.All(InteractionTables.GossipMenu).Any(r => r.U("MenuID") == menu) && !snapshot!.Lookup.MenuExists(menu))
        {
            if (!await DBSource.Open(Connection()).MenuExistsAsync(menu, CancellationToken.None))
            {
                status = $"Not saved: gossip menu {menu} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Menus.Add(menu);
        }

        Commit("Change gossip menu", [Update(InteractionTables.CreatureTemplate, TemplateRow, TemplateRow.With("gossip_menu_id", menu))]);
    }

    void RenderSpellClickEditor(NpcInteraction v)
    {
        foreach (var click in v.SpellClicks)
        {
            var remove = new Button
            {
                Text = $"Remove {click.Name}"
            };
            remove.Pressed += () => Commit($"Remove spell click {click.SpellId}", [Delete(InteractionTables.SpellClick, click.Row)]);
            body.AddChild(remove);
        }

        SearchPicker("click", "Add spell click: spell name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add spell click {s.Id}", [Insert(InteractionTables.SpellClick, Row.Of(("npc_entry", v.Entry), ("spell_id", s.Id), ("cast_flags", 1u), ("user_type", 0u)))])))).ToList()));
    }
}
