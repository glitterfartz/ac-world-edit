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

    static InteractionIssue Error(string message) => new(true, message);
}
