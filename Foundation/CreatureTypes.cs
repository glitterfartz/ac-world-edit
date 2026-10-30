namespace ACWorldEdit.Foundation;

/// <summary>
/// Names of the `creature_template.type` values (`CREATURE_TYPE_*` in TrinityCore's SharedDefines.h). AzerothCore's
/// 3.3.5 client stops at 13 (gas cloud); 14 and 15 only occur on TrinityCore.
/// </summary>
public static class CreatureTypes
{
    static readonly string[] Names =
    [
        "None", "Beast", "Dragonkin", "Demon", "Elemental", "Giant", "Undead", "Humanoid", "Critter", "Mechanical",
        "Not specified", "Totem", "Non-combat pet", "Gas cloud", "Wild pet", "Aberration"
    ];

    /// <summary>The highest type with a name here.</summary>
    public static int MaxNamed => Names.Length - 1;

    public static string Name(int type) => type >= 0 && type < Names.Length ? Names[type] : $"Type {type}";
}
