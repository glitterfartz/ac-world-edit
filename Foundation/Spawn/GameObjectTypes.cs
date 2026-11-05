namespace ACWorldEdit.Foundation;

/// <summary>
/// Names of the `gameobject_template.type` values both servers share (0-35, from `GAMEOBJECT_TYPE_*` in
/// SharedDefines.h). TrinityCore master goes on to 66 for retail-only types; those read as "Type N".
/// Type 29 is AzerothCore's capture point and TrinityCore's control zone.
/// </summary>
public static class GameObjectTypes
{
    static readonly string[] Names =
    [
        "Door", "Button", "Quest giver", "Chest", "Binder", "Generic", "Trap", "Chair", "Spell focus", "Text", "Goober",
        "Transport", "Area damage", "Camera", "Map object", "Map object transport", "Duel arbiter", "Fishing node", "Summoning ritual",
        "Mailbox", "Unused (20)", "Guard post", "Spell caster", "Meeting stone", "Flag stand", "Fishing hole", "Flag drop", "Mini game",
        "Unused (28)", "Capture point", "Aura generator", "Dungeon difficulty", "Barber chair", "Destructible building", "Guild bank", "Trap door"
    ];

    /// <summary>The highest type with a name here.</summary>
    public static int MaxNamed => Names.Length - 1;

    public static string Name(int type) => type >= 0 && type < Names.Length ? Names[type] : $"Type {type}";
}
