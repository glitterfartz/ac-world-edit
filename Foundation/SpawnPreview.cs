namespace ACWorldEdit.Foundation;

/// <summary>How database event associations are interpreted; none of these modes reads live core state.</summary>
public enum EventPreviewMode
{
    Scheduled,
    None,
    Selected,
    All
}

/// <summary>
/// Immutable preview policy captured by each query. Broad visibility never removes spatial,
/// result-count or GPU instance budgets. Phase and difficulty masks match when any bit overlaps.
/// </summary>
public sealed record SpawnPreview
{
    public uint PhaseMask { get; init; } = 1;
    public uint SpawnMask { get; init; } = 1;

    /// <summary>
    /// TrinityCore replaced the masks with a difficulty ID list and a phase ID. DifficultyId is the
    /// difficulty to preview (0 is the open world; <see cref="AnyDifficulty"/> matches every row) and
    /// PhaseId the phase (0 shows only spawns outside any phase). Wrath sources ignore both.
    /// </summary>
    public WorldDialect Dialect { get; init; } = WorldDialect.AzerothCore;
    public int DifficultyId { get; init; }
    public uint PhaseId { get; init; }
    public const int AnyDifficulty = -1;
    public bool RespectMasks { get; init; } = true;
    public EventPreviewMode Events { get; init; } = EventPreviewMode.Scheduled;
    public int[] ActiveEvents { get; init; } = [];
    public bool IncludeHelpers { get; init; }
    public bool IncludePools { get; init; }
    public bool IncludeConditional { get; init; }
    public bool IncludeScripted { get; init; } = true;
    public bool IncludeDespawned { get; init; }
    public float ViewDistance { get; init; } = 200;
    public int QueryLimit { get; init; } = 384;
    public int ModelLimit { get; init; } = 128;
    public bool ShowNames { get; init; }

    public static SpawnPreview AllStored => new()
    {
        RespectMasks = false,
        Events = EventPreviewMode.All,
        IncludeHelpers = true,
        IncludePools = true,
        IncludeConditional = true,
        IncludeDespawned = true
    };

    public void Validate()
    {
        if (!float.IsFinite(ViewDistance) || ViewDistance is < 60 or > 600 ||
            QueryLimit is < 1 or > 768 || ModelLimit is < 1 or > 256 ||
            ActiveEvents.Length > 128 || ActiveEvents.Any(e => e is < 1 or > 65535) ||
            !Enum.IsDefined(Events) || DifficultyId is < AnyDifficulty or > 255)
        {
            throw new ArgumentException("Preview settings exceed supported bounds.");
        }
    }

    // MatchesMasks and MatchesEvents are the executable spec of the SQL built in WorldReadSession
    // (Spatial for masks, Restrictions for events). Production filtering happens in SQL; unit tests
    // pin these rules and the Db tests use them as an oracle for the query results.
    public bool MatchesMasks(uint phase, uint spawn) =>
        !RespectMasks || ((phase & PhaseMask) != 0 && (spawn & SpawnMask) != 0);

    /// <summary>
    /// TrinityCore counterpart of <see cref="MatchesMasks"/>. A spawn lists its difficulties as a CSV
    /// ("0" is the open world) and sits in at most one phase and phase group. A phased spawn shows
    /// when its phase is the previewed one; an unphased spawn always shows.
    /// </summary>
    public bool MatchesPhasing(string difficulties, uint phaseId, uint phaseGroup) =>
        !RespectMasks || (MatchesDifficulty(difficulties) && ((phaseId == 0 && phaseGroup == 0) || (phaseId != 0 && phaseId == PhaseId)));

    public bool MatchesDifficulty(string difficulties) =>
        DifficultyId == AnyDifficulty || difficulties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(DifficultyId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public bool MatchesEvents(IEnumerable<int> bindings, IEnumerable<int> scheduled)
    {
        if (Events == EventPreviewMode.All)
        {
            return true;
        }
        var active = (Events == EventPreviewMode.Selected ? ActiveEvents :
            Events == EventPreviewMode.Scheduled ? scheduled : []).ToHashSet();
        var values = bindings.ToArray();
        return (!values.Any(e => e > 0) || values.Any(e => e > 0 && active.Contains(e))) &&
            !values.Any(e => e < 0 && active.Contains(-e));
    }
}

/// <summary>Quantized camera query center avoids querying on every mouse/camera frame.</summary>
public readonly record struct SpawnQueryPoint(double X, double Y, double Z)
{
    public static SpawnQueryPoint FromPose(WorldPose pose) => new(
        Math.Floor(pose.X / 64) * 64 + 32,
        Math.Floor(pose.Y / 64) * 64 + 32,
        Math.Floor(pose.Z / 64) * 64 + 32);
}

/// <summary>SQL-derived restrictions explain a stored spawn without claiming C++ runtime visibility.</summary>
public sealed record SpawnVisibility(int[] Events, uint Pool, bool Conditional, string Script,
    bool Helper, bool InitiallyDespawned = false)
{
    public string Description => string.Join(" · ", new[]
    {
        Events.Length > 0 ? "Events " + string.Join(", ", Events) : "",
        Pool != 0 ? $"Pool {Pool}" : "", Conditional ? "SQL conditions" : "",
        Script.Length > 0 ? "Script-bound (runtime unknown)" : "",
        Helper ? "Hidden helper" : "", InitiallyDespawned ? "Initially despawned" : ""
    }.Where(s => s.Length > 0));
}

public sealed record WorldEventInfo(int Id, string Name, bool Scheduled);

/// <summary>Database GO pose uses a native server-space quaternion, independent of ADT facing rules.</summary>
public sealed record GameObjectSpawn(ulong Guid, uint TemplateId, string Name, WorldPose Pose,
    uint SpawnMask, uint PhaseMask, uint DisplayId, float Scale,
    System.Numerics.Quaternion Rotation, int State, int Type, SpawnVisibility Visibility, SpawnPhasing? Phasing = null,
    System.Numerics.Quaternion? StoredRotation = null);

/// <summary>
/// Where and when a TrinityCore spawn exists: its difficulty CSV, phase, phase group and phase use
/// flags. Null on records read from AzerothCore, which carries masks instead.
/// </summary>
public sealed record SpawnPhasing(string Difficulties, uint PhaseId, uint PhaseGroup, uint PhaseUseFlags)
{
    public string Description => string.Join(" · ", new[]
    {
        Difficulties.Length > 0 ? "Difficulty " + Difficulties : "",
        PhaseId != 0 ? $"Phase {PhaseId}" : "",
        PhaseGroup != 0 ? $"Phase group {PhaseGroup}" : "",
        PhaseUseFlags != 0 ? $"Phase flags 0x{PhaseUseFlags:X}" : ""
    }.Where(s => s.Length > 0));
}

public sealed record GameObjectRegionResult(IReadOnlyList<GameObjectSpawn> Spawns, bool Truncated,
    WorldEventInfo[] Events, string SourceKey);
