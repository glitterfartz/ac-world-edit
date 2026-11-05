namespace ACWorldEdit.Foundation;

/// <summary>What the client does when a gossip option is chosen, as far as the editor can follow it.</summary>
public enum GossipService
{
    /// <summary>Plain chat: closes the window unless the option opens a submenu.</summary>
    Chat,
    Quests,
    Vendor,
    Trainer,
    Taxi,
    /// <summary>Another client window (bank, auction house, binder…).</summary>
    Other
}

/// <summary>Gossip menu links, option fields and greeting storage for one dialect.
/// Greetings retain their specialized editor: AC stores text, TC stores eight broadcast variants.
/// <see cref="IconOf"/> and <see cref="ServiceOf"/> read the dialect's own option columns (AC OptionIcon and
/// OptionType, TC OptionNpc) so the editors can draw and follow options without knowing which they have.</summary>
public sealed record GossipPageSpec(ItemListSpec Menus, ItemListSpec Options, TableSchema TextTable, Func<Row, uint> IconOf, Func<Row, GossipService> ServiceOf)
{
    /// <summary>UNIT_NPC_FLAG_GOSSIP, the same bit in both servers: a creature opens gossip only with it set.</summary>
    public const ulong GossipFlag = 1;

    /// <summary>The creature template with the gossip npcflag bit set.</summary>
    public Row EnableGossip(Row template) => template.With("npcflag", template.UL("npcflag") | GossipFlag);

    /// <summary>Writing literal option text must stop the broadcast override hiding it in game.</summary>
    public RowEdit[] ChangeText(Row row, string text) => ItemListSpec.Change(Options.Table, row,
        row.With("OptionText", text).With("OptionBroadcastTextID", 0));

    /// <summary>Confirmation text also has a broadcast override in TC.</summary>
    public RowEdit[] ChangeConfirmation(Row row, string text) => ItemListSpec.Change(Options.Table, row,
        Options.Table.Columns.Any(c => c.Name == "BoxBroadcastTextID")
            ? row.With("BoxText", text).With("BoxBroadcastTextID", 0)
            : row.With("BoxText", text));

    /// <summary>Delete both original keys before inserting either replacement, so SQL cannot collide.</summary>
    public RowEdit[] SwapOptions(Row first, Row second)
    {
        if (first.U("MenuID") != second.U("MenuID") || first.U("OptionID") == second.U("OptionID"))
        {
            throw new InvalidDataException("Only distinct options of the same menu can be reordered.");
        }

        return
        [
            Options.Remove(first), Options.Remove(second),
            InteractionEdits.Insert(Options.Table, first.With("OptionID", second.U("OptionID"))),
            InteractionEdits.Insert(Options.Table, second.With("OptionID", first.U("OptionID")))
        ];
    }
}

/// <summary>Schema references: the official AC and TC gossip_menu_option wiki pages.</summary>
public static class GossipPage
{
    public static readonly GossipPageSpec AzerothCore = Build(false);
    public static readonly GossipPageSpec TrinityCore = Build(true);

    /// <summary>
    /// GOSSIP_ICON value of a TrinityCore option. GossipOptionNpc 1-9 (vendor, taxi, trainer, spirit healer,
    /// binder, banker, petition, tabard, battlemaster) are the same numbers as the client's gossip icons; the
    /// other services use the chat icon.
    /// </summary>
    static uint TcIcon(Row option) => option.U("OptionNpc") is > 0 and < 10 and var npc ? npc : 0;

    static GossipService TcService(Row option) => option.U("OptionNpc") switch
    {
        1 => GossipService.Vendor,
        2 => GossipService.Taxi,
        3 => GossipService.Trainer,
        0 => GossipService.Chat,
        _ => GossipService.Other
    };

    /// <summary>GOSSIP_OPTION_* of an AzerothCore option: 1 gossip, 2 quest giver, 3 vendor, 4 taxi, 5 trainer, 15 armorer.</summary>
    static GossipService AcService(Row option) => option.U("OptionType") switch
    {
        1 => GossipService.Chat,
        2 => GossipService.Quests,
        3 or 15 => GossipService.Vendor,
        4 => GossipService.Taxi,
        5 => GossipService.Trainer,
        _ => GossipService.Other
    };

    /// <summary>A trainer bound to a removed option (TrinityCore keys <c>creature_trainer</c> by menu and option) would point at nothing.</summary>
    static IEnumerable<RowEdit> TrainerBindings(Row option, RowSet rows) => rows.All(TcTables.CreatureTrainer)
        .Where(b => b.U("MenuID") == option.U("MenuID") && b.U("OptionID") == option.U("OptionID"))
        .Select(b => InteractionEdits.Delete(TcTables.CreatureTrainer, b));

    static GossipPageSpec Build(bool retail)
    {
        var table = retail ? TcTables.GossipOption : AcTables.GossipOption;
        FieldSpec Field(string column, string label, string tip, FieldKind kind = FieldKind.Whole,
            (int Value, string Name)[]? options = null) =>
            new("option", table, column, label, tip, kind, RowBinding.Item, Options: options, AllowEmpty: true);
        var fields = new List<FieldSpec>
        {
            Field("OptionText", "Option text", "Player-selectable text. A valid broadcast text takes precedence.", FieldKind.Text),
            Field("OptionBroadcastTextID", "Broadcast text", "BroadcastText ID overriding the literal option text. 0 uses the literal text."),
            retail
                ? Field("OptionNpc", "Service", "The client service this option opens.", FieldKind.Choice,
                    TcCreatureView.OptionNpcNames.Select((name, id) => (id, name)).ToArray())
                : Field("OptionType", "Service", "What the client does when the option is selected.", FieldKind.Choice,
                    AcNpcFlags.OptionTypes.Select((name, id) => (id, name)).ToArray()),
            Field("ActionMenuID", "Submenu", "Menu opened by this option. 0 means none."),
            Field("ActionPoiID", "Point of interest", "Map marker shown by this option. 0 means none."),
            Field("BoxText", "Confirmation text", "Text of the confirmation dialog.", FieldKind.Text),
            Field("BoxMoney", "Confirmation cost", "Cost in copper shown in the confirmation dialog."),
            Field("BoxBroadcastTextID", "Confirmation broadcast", "BroadcastText overriding the literal confirmation text."),
            Field("BoxCoded", "Ask for code", "Ask the player to enter a code.", FieldKind.Check)
        };
        if (retail)
        {
            fields.AddRange(new[]
            {
                Field("GossipOptionID", "Client option ID", "Client gossip option record."),
                Field("Language", "Language", "Language ID for this option."),
                Field("Flags", "Flags", "Additional option flags."),
                Field("GossipNpcOptionID", "NPC option ID", "Client NPC option record."),
                Field("SpellID", "Spell", "Spell cast by this option."),
                Field("OverrideIconID", "Icon override", "Client icon override.")
            });
        }
        else
        {
            fields.Insert(0, Field("OptionIcon", "Icon", "Icon shown beside this option.", FieldKind.Choice,
                AcNpcFlags.OptionIcons.Select((name, id) => (id, name)).ToArray()));
            fields.Insert(4, Field("OptionNpcFlag", "NPC flag", "Shown only when this and the creature's npcflag overlap."));
        }

        Row NewOption(uint menu, RowSet rows)
        {
            uint id = rows.All(table).Where(r => r.U("MenuID") == menu).Select(r => r.U("OptionID"))
                .Select(id => checked(id + 1)).DefaultIfEmpty(0u).Max();
            if (!retail)
            {
                return InteractionEdits.NewOption(menu, id, "New option", 1, AcNpcFlags.Gossip, 0, 0);
            }

            return Row.Of(table.Columns.Select(c => (c.Name, (object)(c.Kind == ColumnKind.Text ? "" : "0"))).ToArray())
                .With("MenuID", menu).With("OptionID", id).With("OptionText", "New option");
        }

        var menus = retail ? TcTables.GossipMenu : AcTables.GossipMenu;
        return new(new(menus, "MenuID", "TextID", "gossip text link", [],
                (menu, _) => Row.Of(("MenuID", menu), ("TextID", 0u))),
            new(table, "MenuID", "OptionID", "gossip option", fields.ToArray(), NewOption,
                Dependents: retail ? TrainerBindings : null),
            retail ? TcTables.NpcText : AcTables.NpcText,
            retail ? TcIcon : r => r.U("OptionIcon"),
            retail ? TcService : AcService);
    }
}
