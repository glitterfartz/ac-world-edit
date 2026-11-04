namespace ACWorldEdit;

/// <summary>A picker entry of the creature window: the key, caption and client icon both dialects' sections share.</summary>
public sealed record PickerPage(string Id, string Title, string IconPath);

/// <summary>
/// The pages that exist in both dialects. An AzerothCore section and its TrinityCore counterpart read
/// their id, title and icon from the same entry, so the two pickers cannot drift apart.
/// </summary>
public static class PickerPages
{
    public static readonly PickerPage Stats = new("stats", "Class / Level / Stats", "Interface/Icons/INV_Sword_04.blp");
    public static readonly PickerPage Gossip = new("gossip", "Gossip", ClientStyle.GossipIcon(0));
    public static readonly PickerPage Quests = new("quests", "Quests", ClientStyle.AvailableQuestIcon);
    public static readonly PickerPage Template = new("template", "Template", "Interface/Icons/INV_Misc_Book_09.blp");
    public static readonly PickerPage Trainer = new("trainer", "Trainer", ClientStyle.GossipIcon(3));
    public static readonly PickerPage Vendor = new("vendor", "Vendor", ClientStyle.GossipIcon(1));
}
