namespace ACWorldEdit.Foundation;

/// <summary>Quest vocabulary the AzerothCore and TrinityCore quest pages show the same way.</summary>
public static class QuestVocabulary
{
    /// <summary>QuestInfoID (QuestInfo.dbc).</summary>
    public static readonly (uint Value, string Name)[] Types = [(0, "Normal"), (1, "Group (elite)"), (21, "Life"), (41, "PvP"), (62, "Raid"), (81, "Dungeon"), (82, "World event"), (83, "Legendary"), (84, "Escort"), (85, "Heroic"), (88, "Raid (10)"), (89, "Raid (25)")];

    /// <summary>Text placeholders the client substitutes.</summary>
    public static readonly (string Token, string Meaning)[] Placeholders = [("$N", "player name"), ("$C", "class"), ("$R", "race"), ("$B", "new line"), ("$G man:woman;", "gendered word")];
}