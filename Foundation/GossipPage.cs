namespace ACWorldEdit.Foundation;

/// <summary>Gossip menu links, option fields and greeting storage for one dialect.
/// Greetings retain their specialized editor: AC stores text, TC stores eight broadcast variants.</summary>
public sealed record GossipPageSpec(ItemListSpec Menus, ItemListSpec Options, TableSchema TextTable)
{
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
                Field("BoxBroadcastTextID", "Confirmation broadcast", "BroadcastText overriding the literal confirmation text."),
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
            new(table, "MenuID", "OptionID", "gossip option", fields.ToArray(), NewOption),
            retail ? TcTables.NpcText : AcTables.NpcText);
    }
}
