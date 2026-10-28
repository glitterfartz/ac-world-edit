using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Row-edit constructors, default rows and text helpers behind the interaction window, moved out of
/// it with bodies unchanged so they can be tested without Godot.
/// </summary>
public static class InteractionEdits
{
    public static RowEdit Update(TableSchema table, Row before, Row after) => new(table.Name, table.KeyOf(before), before, after);
    public static RowEdit Insert(TableSchema table, Row after) => new(table.Name, table.KeyOf(after), null, after);
    public static RowEdit Delete(TableSchema table, Row before) => new(table.Name, table.KeyOf(before), before, null);

    public static Row NewText(uint id, string text) => Row.Of(("ID", id), ("text0_0", text), ("text0_1", ""), ("BroadcastTextID0", 0), ("Probability0", 1f));

    public static Row NewOption(uint menu, uint id, string text, uint type, uint npcFlag, uint icon, uint action) =>
        Row.Of(("MenuID", menu), ("OptionID", id), ("OptionIcon", icon), ("OptionText", text), ("OptionBroadcastTextID", 0), ("OptionType", type), ("OptionNpcFlag", npcFlag), ("ActionMenuID", action), ("ActionPoiID", 0u), ("BoxCoded", 0u), ("BoxMoney", 0u), ("BoxText", ""));

    /// <summary>
    /// Decimal or 0x-prefixed hex; throws FormatException/OverflowException for anything else.
    /// </summary>
    public static uint ParseUInt(string text) => text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? uint.Parse(text.Trim()[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : uint.Parse(text.Trim(), CultureInfo.InvariantCulture);

    public static string OptionTypeName(uint type) => type < AcNpcFlags.OptionTypes.Length ? AcNpcFlags.OptionTypes[type] : $"Unknown {type}";

    public static string Summary(AcCreatureView v)
    {
        var flags = AcNpcFlags.Names(v.NpcFlag).ToArray();
        string scripts = (v.ScriptName.Length > 0 ? $"\nC++ script: {v.ScriptName}" : "") + (v.Scripts.Length > 0 ? $"\nSmartAI interaction events: {v.Scripts.Length}" : "");
        int edits = v.Menus.Values.Count(m => m.Edited) + v.Vendor.Count(i => i.Edited) + v.Starts.Concat(v.Ends).Count(q => q.Edited);
        return $"{(flags.Length == 0 ? "No NPC flags" : string.Join(" · ", flags))}\n" +
               $"Gossip options {v.Menus.Values.Sum(m => m.Options.Length)} · Quests {v.Starts.Length}/{v.Ends.Length} · Vendor {v.Vendor.Length} · Trainer {v.Trainer?.Spells.Length ?? 0}" +
               scripts + (edits > 0 ? "\nLocal draft changes present." : "") + (v.Issues.Any(i => i.Error) ? $"\n{v.Issues.Count(i => i.Error)} validation error(s)." : "");
    }
}
