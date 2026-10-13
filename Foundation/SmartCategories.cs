namespace ACWorldEdit.Foundation;

/// <summary>
/// Browsing groups for SmartAI types, assigned from their server code names (first matching
/// keyword wins, so order matters). Grouping is presentation only; it never changes validation.
/// Server range markers (TC_END/AC_START/AC_END) and reserved slots are not types.
/// </summary>
public static partial class SmartCatalog
{
    public static bool IsMarker(SmartType type) =>
        type.Code.EndsWith("_TC_END", StringComparison.Ordinal) || type.Code.EndsWith("_AC_START", StringComparison.Ordinal) ||
        type.Code.EndsWith("_AC_END", StringComparison.Ordinal) || type.Code.Contains("RESERVED", StringComparison.Ordinal);

    static readonly (string Category, string[] Keywords)[] EventGroups =
    [
        ("Timers", ["UPDATE", "TIMED_EVENT"]),
        ("Linked", ["LINK"]),
        ("Gossip & clicks", ["GOSSIP", "SPELLCLICK"]),
        ("Players & quests", ["QUEST", "RECEIVE_EMOTE", "LOS", "AREATRIGGER", "INSTANCE_PLAYER", "NEAR_PLAYERS", "TEXT_OVER"]),
        ("Combat", ["AGGRO", "KILL", "DEATH", "EVADE", "SPELLHIT", "DAMAGED", "HEALTH", "MANA", "CASTING", "RANGE", "BEHIND", "MELEE", "HAS_AURA", "BUFFED", "FRIENDLY", "CHARMED", "RECEIVE_HEAL"]),
        ("Movement & escort", ["MOVEMENTINFORM", "REACHED_HOME", "ESCORT", "WAYPOINT", "FOLLOW", "PASSENGER", "TRANSPORT"]),
        ("Spawn & summons", ["RESPAWN", "RESET", "AI_INIT", "JUST_CREATED", "JUST_SUMMONED", "SUMMON", "CORPSE", "DESPAWN"]),
        ("Objects & world", ["GO_", "GAME_EVENT", "DISTANCE", "NEAR_UNIT", "SCENE"]),
        ("Phases & data", ["PHASE", "DATA_SET", "COUNTER", "ACTION_DONE"])
    ];

    static readonly (string Category, string[] Keywords)[] ActionGroups =
    [
        ("Speech & sound", ["TALK", "SOUND", "MUSIC", "EMOTE", "MOVIE", "CINEMATIC", "ANIMKIT", "SPELL_VISUAL", "ANIM"]),
        ("Gossip, players & quests", ["QUEST", "GOSSIP", "ITEM", "TAXI", "CREDIT", "KILLEDMONSTER", "AREAEXPLORED", "GROUPEVENT", "REWARD"]),
        ("Spells & combat", ["CAST", "AURA", "ATTACK", "THREAT", "COMBAT", "EVADE", "FLEE", "CALL_FOR_HELP", "INTERRUPT", "DIE", "KILL_UNIT", "INVINCIBILITY", "IMMUNITY", "REACT", "ENCOUNTER", "ZONE_UNDER_ATTACK"]),
        ("Movement", ["MOVE", "WAYPOINT", "ESCORT", "FOLLOW", "JUMP", "ROOT", "RUN", "FLY", "SWIM", "HOVER", "TELEPORT", "ORIENTATION", "HOME_POS", "FALL", "RISE", "MOTION", "SPEED", "VEHICLE", "DISMOUNT", "COMBAT_DISTANCE", "RANGED_MOVEMENT"]),
        ("Summons & spawns", ["SUMMON", "SPAWN", "DESPAWN", "RESPAWN", "GOBJECT", "GAMEOBJECT", "GO_"]),
        ("Phases, events & scripts", ["PHASE", "ACTIONLIST", "TIMED_EVENT", "DATA", "COUNTER", "GAME_EVENT", "SCRIPT", "AI_TEMPLATE", "DO_ACTION", "STORE_TARGET", "SEND_TARGET", "SET_GUID", "WORLD_SCRIPT", "LOAD_GRID", "EVENT_FLAG", "ACTIVE", "NONE"]),
        ("Appearance & state", ["MORPH", "MOUNT", "FACTION", "FLAG", "VISIBILITY", "SHEATH", "EQUIP", "SCALE", "STAND", "FIELD_BYTES", "UPDATE_TEMPLATE", "HEALTH", "POWER", "CORPSE", "SIGHT", "ENVIRONMENT"])
    ];

    static readonly (string Category, string[] Keywords)[] TargetGroups =
    [
        ("Itself & event", ["NONE", "SELF", "INVOKER", "OWNER", "STORED", "VEHICLE", "SUMMONED", "FORMATION", "SHARED_OWNER", "INSTANCE"]),
        ("Enemies", ["VICTIM", "HOSTILE", "THREAT", "ENEMY", "LOOT", "FARTHEST", "ROLE"]),
        ("Players", ["PLAYER", "PARTY"]),
        ("Creatures", ["CREATURE", "FRIENDLY"]),
        ("Game objects", ["GAMEOBJECT"]),
        ("Positions", ["POSITION", "POINT"])
    ];

    /// <summary>
    /// Category of an event, action or target type for grouped pickers.
    /// </summary>
    public static string Category(SmartType type)
    {
        var groups = type.Code.StartsWith("SMART_EVENT_", StringComparison.Ordinal) ? EventGroups
            : type.Code.StartsWith("SMART_ACTION_", StringComparison.Ordinal) ? ActionGroups : TargetGroups;
        string name = type.Code[(type.Code.IndexOf('_', 6) + 1)..];
        foreach (var (category, keywords) in groups)
        {
            if (keywords.Any(k => name.Contains(k, StringComparison.Ordinal)))
            {
                return category;
            }
        }

        return "Other";
    }

    /// <summary>
    /// Selectable types grouped by category (catalog order of groups, names sorted within).
    /// </summary>
    public static IEnumerable<(string Category, IReadOnlyList<SmartType> Types)> Grouped(IEnumerable<SmartType> types, string kind)
    {
        var groups = kind == "event" ? EventGroups : kind == "action" ? ActionGroups : TargetGroups;
        var order = groups.Select(g => g.Category).Append("Other").ToList();
        return types.Where(t => !IsMarker(t)).GroupBy(Category).OrderBy(g => order.IndexOf(g.Key))
            .Select(g => (g.Key, (IReadOnlyList<SmartType>)g.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()));
    }
}
