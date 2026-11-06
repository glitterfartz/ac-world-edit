using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// What the editor can look up about the world a spawn row refers to. A null lookup skips the rules
/// that need it, so the checks that depend on the row alone still run when a client or database
/// is not available.
/// </summary>
public sealed record SpawnFacts(
    Func<uint, bool>? TemplateExists = null,
    Func<uint, bool>? MapExists = null,
    Func<uint, int, bool>? EquipmentExists = null,
    Func<uint, int, bool>? DifficultyValid = null,
    Func<uint, bool>? PhaseExists = null,
    Func<uint, uint, bool>? SpawnMaskValid = null);

/// <summary>
/// The checks the server's own loader makes on `creature` and `gameobject` rows
/// (<c>ObjectMgr::LoadCreatures</c>, <c>LoadGameObjects</c>). The server logs an error and then skips the
/// spawn or quietly changes a value, so a row that fails here would not behave as the editor shows it.
/// Used as the check of a spawn command: only an error a command introduces refuses the command.
/// </summary>
public static class SpawnRules
{
    /// <summary>Movement types a row may hold: idle, random, waypoint.</summary>
    public const uint MaxDbMotionType = 3;

    /// <summary>TrinityCore refuses a wander distance between 0 and this and resets it to 0.</summary>
    public const double MinWander = 0.1;

    const uint PhaseAlwaysVisible = 1, PhaseInverse = 2, PhaseUseFlagsAll = PhaseAlwaysVisible | PhaseInverse;

    public static IEnumerable<InteractionIssue> Check(WorldDialect dialect, SpawnKind kind, RowSet rows, TableSchema table, SpawnFacts? facts = null)
    {
        foreach (var row in rows.All(table))
        {
            foreach (var issue in CheckRow(dialect, kind, row, facts ?? new()))
            {
                yield return issue;
            }
        }
    }

    public static IEnumerable<InteractionIssue> CheckRow(WorldDialect dialect, SpawnKind kind, Row row, SpawnFacts facts)
    {
        string name = kind == SpawnKind.Creature ? "creature" : "GameObject";
        string who = $"{char.ToUpperInvariant(name[0])}{name[1..]} spawn {row["guid"]}";
        uint entry = row.U("id"), map = row.U("map");
        if (facts.TemplateExists?.Invoke(entry) == false)
        {
            yield return Error($"{who}: {name} template {entry} does not exist.");
        }

        if (facts.MapExists?.Invoke(map) == false)
        {
            yield return Error($"{who}: map {map} does not exist.");
        }

        if (kind == SpawnKind.Creature)
        {
            foreach (var issue in CheckCreature(dialect, row, facts, who, entry))
            {
                yield return issue;
            }
        }

        if (dialect == WorldDialect.TrinityCore)
        {
            foreach (var issue in CheckTrinityVisibility(row, facts, who, map))
            {
                yield return issue;
            }
        }
        else if (facts.SpawnMaskValid?.Invoke(map, row.U("spawnMask")) == false)
        {
            yield return Error($"{who}: spawn mask {row.U("spawnMask")} includes a difficulty map {map} does not support.");
        }
        else if (row.U("phaseMask") == 0)
        {
            yield return Error($"{who}: phase mask 0 means the spawn is visible in no phase.");
        }
    }

    static IEnumerable<InteractionIssue> CheckCreature(WorldDialect dialect, Row row, SpawnFacts facts, string who, uint entry)
    {
        uint movement = row.U("MovementType");
        double wander = row.F("wander_distance");
        if (movement >= MaxDbMotionType)
        {
            yield return Error($"{who}: movement type {movement} cannot be set in the database (0 idle, 1 random, 2 waypoint).");
        }
        else if (wander < 0)
        {
            yield return Error($"{who}: wander distance cannot be negative.");
        }
        else if (dialect == WorldDialect.TrinityCore && wander > 0 && wander < MinWander)
        {
            yield return Error($"{who}: a wander distance below {MinWander} is reset to 0 by the server.");
        }
        else if (movement == 1 && wander == 0)
        {
            yield return Error($"{who}: random movement needs a wander distance above 0, or the server makes the creature stand still.");
        }
        else if (movement == 0 && wander != 0)
        {
            yield return Error($"{who}: a creature that stands still cannot have a wander distance; the server resets it to 0.");
        }

        int equipment = row.I("equipment_id");
        if (equipment != 0 && facts.EquipmentExists?.Invoke(entry, equipment) == false)
        {
            yield return Error($"{who}: equipment set {equipment} does not exist for creature {entry}; the server uses none.");
        }
    }

    static IEnumerable<InteractionIssue> CheckTrinityVisibility(Row row, SpawnFacts facts, string who, uint map)
    {
        var difficulties = row["spawnDifficulties"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (difficulties.Length == 0)
        {
            yield return Error($"{who}: it needs at least one difficulty, or the server skips it.");
        }

        foreach (string text in difficulties)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int difficulty))
            {
                yield return Error($"{who}: difficulty \"{text}\" is not a number.");
            }
            else if (facts.DifficultyValid?.Invoke(map, difficulty) == false)
            {
                yield return Error($"{who}: difficulty {difficulty} is not valid for map {map}.");
            }
        }

        uint phase = row.IsNull("PhaseId") ? 0 : (uint)Math.Max(0, row.I("PhaseId"));
        uint group = row.IsNull("PhaseGroup") ? 0 : (uint)Math.Max(0, row.I("PhaseGroup"));
        uint use = row.U("phaseUseFlags");
        if (phase != 0 && group != 0)
        {
            yield return Error($"{who}: set a phase or a phase group, not both; the server drops the group.");
        }

        if (phase != 0 && facts.PhaseExists?.Invoke(phase) == false)
        {
            yield return Error($"{who}: phase {phase} does not exist.");
        }

        if ((use & ~PhaseUseFlagsAll) != 0)
        {
            yield return Error($"{who}: phase use flags {use} include unknown bits.");
        }
        else if ((use & PhaseAlwaysVisible) != 0 && (use & PhaseInverse) != 0)
        {
            yield return Error($"{who}: a phase cannot be both always visible and inverse.");
        }
    }


    /// <summary>Highest stand state and sheath state the loader accepts (UNIT_STAND_STATE_SUBMERGED, SHEATH_STATE_RANGED).</summary>
    const uint MaxStandState = 9, MaxSheathState = 2;

    /// <summary>The checks of <c>LoadCreatureAddons</c> on every addon row of a spawn kind. A GameObject addon has none.</summary>
    public static IEnumerable<InteractionIssue> CheckAddons(WorldDialect dialect, SpawnKind kind, RowSet rows, TableSchema addonTable)
    {
        if (kind != SpawnKind.Creature)
        {
            yield break;
        }

        foreach (var row in rows.All(addonTable))
        {
            foreach (var issue in CheckAddonRow(dialect, row, $"Creature spawn {row["guid"]}'s appearance"))
            {
                yield return issue;
            }
        }
    }

    /// <summary>The same checks on the rows of `creature_template_addon`, which the server loads with the same rules.</summary>
    public static IEnumerable<InteractionIssue> CheckTemplateAddons(WorldDialect dialect, RowSet rows, TableSchema templateAddonTable)
    {
        foreach (var row in rows.All(templateAddonTable))
        {
            foreach (var issue in CheckAddonRow(dialect, row, $"Creature template {row["entry"]}'s appearance"))
            {
                yield return issue;
            }
        }
    }

    static IEnumerable<InteractionIssue> CheckAddonRow(WorldDialect dialect, Row row, string who)
    {
        bool tc = dialect == WorldDialect.TrinityCore;
        uint stand = tc ? row.U("StandState") : (uint)(ulong.Parse(row["bytes1"], CultureInfo.InvariantCulture) & 0xFF);
        uint sheath = tc ? row.U("SheathState") : (uint)(ulong.Parse(row["bytes2"], CultureInfo.InvariantCulture) & 0xFF);
        if (stand > MaxStandState)
        {
            yield return Error($"{who}: stance {stand} is not one of 0 to {MaxStandState}.");
        }

        if (sheath > MaxSheathState)
        {
            yield return Error($"{who}: weapon state {sheath} is not one of 0 to {MaxSheathState}.");
        }

        string auras = row.IsNull("auras") ? "" : row["auras"];
        var seen = new HashSet<uint>();
        foreach (string token in auras.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out uint spell) || spell == 0)
            {
                yield return Error($"{who}: aura \"{token}\" is not a spell ID.");
            }
            else if (!seen.Add(spell))
            {
                yield return Error($"{who}: aura {spell} is listed twice.");
            }
        }
    }

    static InteractionIssue Error(string message) => new(true, message);

    /// <summary>
    /// The checks of one command that touches waypoint or formation rows: node coordinates, the
    /// TrinityCore path header and formation offsets. Only errors a command introduces refuse it, so
    /// pre-existing database rows never block an unrelated edit.
    /// </summary>
    public static IEnumerable<InteractionIssue> CheckMovement(IWorldSchema schema, RowSet rows)
    {
        foreach (var issue in CheckWaypointNodes(schema.Dialect, rows, MovementEdits.NodeTable(schema)))
        {
            yield return issue;
        }

        if (MovementEdits.HeaderTable(schema) is { } header)
        {
            foreach (var issue in CheckWaypointHeaders(rows, header))
            {
                yield return issue;
            }
        }

        foreach (var issue in CheckFormations(rows, MovementEdits.FormationTable(schema)))
        {
            yield return issue;
        }
    }

    /// <summary>Mirror what the servers load: every node needs finite coordinates and a finite facing.</summary>
    public static IEnumerable<InteractionIssue> CheckWaypointNodes(WorldDialect dialect, RowSet rows, TableSchema nodes)
    {
        string point = dialect == WorldDialect.TrinityCore ? "NodeId" : "point";
        string id = dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        (string X, string Y, string Z, string Facing) = dialect == WorldDialect.TrinityCore
            ? ("PositionX", "PositionY", "PositionZ", "Orientation")
            : ("position_x", "position_y", "position_z", "orientation");
        foreach (var row in rows.All(nodes))
        {
            string who = $"Waypoint path {row[id]} point {row[point]}";
            if (!float.IsFinite(row.F(X)) || !float.IsFinite(row.F(Y)) || !float.IsFinite(row.F(Z)))
            {
                yield return Error($"{who}: coordinates must be finite numbers.");
            }

            if (!row.IsNull(Facing) && !float.IsFinite(row.F(Facing)))
            {
                yield return Error($"{who}: facing must be a number.");
            }
        }
    }

    /// <summary>The TrinityCore path header both servers' move types share (0 walk, 1 run, 2 land, 3 take off).</summary>
    public static IEnumerable<InteractionIssue> CheckWaypointHeaders(RowSet rows, TableSchema header)
    {
        foreach (var row in rows.All(header))
        {
            string who = $"Waypoint path {row["PathId"]}";
            if (row.U("MoveType") > WaypointProps.MaxMoveType)
            {
                yield return Error($"{who}: move type must be 0 (walk), 1 (run), 2 (land) or 3 (take off).");
            }

            if (!row.IsNull("Velocity") && (!float.IsFinite(row.F("Velocity")) || row.F("Velocity") < 0))
            {
                yield return Error($"{who}: velocity must be zero or more.");
            }
        }
    }

    /// <summary>
    /// Mirror the formation loader: guids must name creatures (zero never does) and turn-around
    /// points fit 16 bits the way the loader reads them. A negative distance or an angle outside
    /// 0–360 still loads, so those are warnings, not errors.
    /// </summary>
    public static IEnumerable<InteractionIssue> CheckFormations(RowSet rows, TableSchema formations)
    {
        foreach (var row in rows.All(formations))
        {
            string who = $"Formation member {row["memberGUID"]}";
            if (row["leaderGUID"] == "0" || row["memberGUID"] == "0")
            {
                yield return Error($"{who}: leader and member must be creature spawns.");
            }

            if (!float.IsFinite(row.F("dist")))
            {
                yield return Error($"{who}: distance must be a number.");
            }
            else if (row.F("dist") < 0)
            {
                yield return new(false, $"{who}: a negative distance still loads, but the follower walks through its leader.");
            }

            if (!float.IsFinite(row.F("angle")))
            {
                yield return Error($"{who}: angle must be a number.");
            }
            else if (row.F("angle") < 0 || row.F("angle") > 360)
            {
                yield return new(false, $"{who}: angle is in degrees, 0 to 360; other values still load.");
            }

            if (row.U("point_1") > ushort.MaxValue || row.U("point_2") > ushort.MaxValue)
            {
                yield return Error($"{who}: turn-around points must fit a 16-bit number.");
            }
        }
    }
}
