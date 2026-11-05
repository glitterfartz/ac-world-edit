using System.Text.RegularExpressions;

namespace ACWorldEdit.Foundation;

public sealed record ItemInfo(uint Id, string Name, int Quality, long BuyPrice, uint DisplayId);
public sealed record QuestInfo(uint Id, string Title, int Level, int MinLevel, string Details, string Objectives, int RewardMoney, string[] RewardItems);
/// <summary>
/// SmartAI row that reacts to interaction (event 64 gossip hello, 62 gossip select, 19 quest
/// accepted, 20 quest rewarded). Shown as a warning; never simulated.
/// </summary>
public sealed record ScriptHook(int EventType, int ActionType, int EventParam1, int EventParam2, string Comment);

/// <summary>
/// Read-only facts the editor needs beyond the editable rows: names, existence checks for server
/// validation, sharing counts and script/condition context. Implemented by the database/client
/// layer; Foundation stays engine- and database-independent.
/// </summary>
public interface IInteractionLookup
{
    ItemInfo? Item(uint id);
    QuestInfo? Quest(uint id);
    string? SpellName(uint id);
    bool ExtendedCostExists(uint id);
    (string Male, string Female)? BroadcastText(uint id);
    bool PoiExists(uint id);
    /// <summary>
    /// Database rows outside this NPC's loaded scope (another NPC's menu, an existing trainer).
    /// </summary>
    bool MenuExists(uint menuId);
    bool TextExists(uint textId);
    bool TrainerExists(uint id);
    IReadOnlyList<Row> ReferenceVendor(uint entry);
    int MenuUsers(uint menuId);
    int TextUsers(uint textId);
    int TrainerUsers(uint trainerId);
}

public sealed record TextView(uint TextId, string Male, string Female, bool FromBroadcast, uint BroadcastId, float Probability, bool Edited);
public sealed record OptionView(Row Row, uint OptionId, uint Icon, string Text, bool FromBroadcast, uint Type, uint NpcFlag, uint ActionMenu, uint Poi, bool BoxCoded, uint BoxMoney, string BoxText, bool HiddenByFlags, bool Edited);
public sealed record MenuView(uint MenuId, TextView[] Texts, OptionView[] Options, int SharedBy, bool Edited);
public sealed record VendorView(Row? Row, uint Item, uint ExtendedCost, int Slot, uint MaxCount, uint IncrTime, ItemInfo? Info, uint FromReference, bool Edited);
public sealed record TrainerSpellView(Row Row, uint SpellId, string Name, uint Cost, uint ReqSkillLine, uint ReqSkillRank, uint ReqLevel, bool Edited);
public sealed record TrainerView(uint Id, uint Type, uint Requirement, string Greeting, TrainerSpellView[] Spells, int SharedBy, bool Exists);
public sealed record QuestLinkView(uint Quest, QuestInfo? Info, bool Edited);
public sealed record SpellClickView(Row Row, uint SpellId, string Name, uint CastFlags, uint UserType, bool Edited);

/// <summary>
/// Problem found by validation. Errors block a command; warnings mirror what the server logs and
/// may carry a one-click fix.
/// </summary>
public sealed record InteractionIssue(bool Error, string Message, RowEdit[]? Fix = null);

/// <summary>Client text placeholders in gossip, quest and trainer text, shown the way the client would.</summary>
public static class InteractionText
{
    /// <summary>
    /// Render client text placeholders for preview: $B newline, $N/$C/$R name/class/race tokens,
    /// $g male:female; picks the male form. Raw text stays in the rows.
    /// </summary>
    public static string Render(string text)
    {
        text = Regex.Replace(text, @"\$[gG]\s*([^:;]*):([^;]*);", m => m.Groups[1].Value);
        return text.Replace("$B", "\n").Replace("$b", "\n").Replace("$N", "<name>").Replace("$n", "<name>").Replace("$C", "<class>").Replace("$c", "<class>").Replace("$R", "<race>").Replace("$r", "<race>");
    }
}
