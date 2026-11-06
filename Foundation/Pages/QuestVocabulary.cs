namespace ACWorldEdit.Foundation;

/// <summary>Quest vocabulary the AzerothCore and TrinityCore quest pages show the same way.</summary>
public static class QuestVocabulary
{
    /// <summary>QuestInfoID (QuestInfo.dbc).</summary>
    public static readonly (uint Value, string Name)[] Types = [(0, "Normal"), (1, "Group (elite)"), (21, "Life"), (41, "PvP"), (62, "Raid"), (81, "Dungeon"), (82, "World event"), (83, "Legendary"), (84, "Escort"), (85, "Heroic"), (88, "Raid (10)"), (89, "Raid (25)")];

    /// <summary>
    /// Playable races (ChrRaces IDs) for the 64-bit quest race mask. Names are the fallback; the
    /// retail client's ChrRaces table supplies the current names when it is open.
    /// </summary>
    public static readonly (uint Id, string Name)[] PlayableRaces =
    [
        (1, "Human"), (2, "Orc"), (3, "Dwarf"), (4, "Night Elf"), (5, "Undead"), (6, "Tauren"), (7, "Gnome"), (8, "Troll"),
        (9, "Goblin"), (10, "Blood Elf"), (11, "Draenei"), (22, "Worgen"), (24, "Pandaren"), (25, "Pandaren (Alliance)"),
        (26, "Pandaren (Horde)"), (27, "Nightborne"), (28, "Highmountain Tauren"), (29, "Void Elf"), (30, "Lightforged Draenei"),
        (31, "Zandalari Troll"), (32, "Kul Tiran"), (34, "Dark Iron Dwarf"), (35, "Vulpera"), (36, "Mag'har Orc"),
        (37, "Mechagnome"), (52, "Dracthyr"), (70, "Earthen")
    ];

    /// <summary>The race mask bit for a ChrRaces ID (one bit per race, starting at 1 for Human).</summary>
    public static ulong RaceBit(uint race) => race is > 0 and <= 64 ? 1UL << (int)(race - 1) : 0;

    /// <summary>Text placeholders the client substitutes.</summary>
    public static readonly (string Token, string Meaning)[] Placeholders = [("$N", "player name"), ("$C", "class"), ("$R", "race"), ("$B", "new line"), ("$G man:woman;", "gendered word")];
}