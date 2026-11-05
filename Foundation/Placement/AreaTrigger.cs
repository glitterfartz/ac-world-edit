namespace ACWorldEdit.Foundation;

/// <summary>Where an AreaTrigger teleports players (areatrigger_teleport).</summary>
public sealed record AreaTriggerTeleport(string Name, ushort Map, double X, double Y, double Z, double Orientation);

/// <summary>
/// What an AreaTrigger does on the server, in the order MiscHandler.cpp HandleAreaTriggerOpcode
/// consults it: a script (C++ name or the SmartAI script), exploration quest credit, tavern rest,
/// then teleport. Colors and inspector wording key off this.
/// </summary>
public enum AreaTriggerKind
{
    SmartAI,
    Script,
    Teleport,
    Quest,
    Tavern,
    Unlinked
}

/// <summary>
/// The volume the server tests, numbered as AreaTriggerShapeType (TrinityCore DBCEnums.h). AzerothCore
/// rows are spheres or boxes; TrinityCore client triggers add polygons and cylinders, and its
/// server-spawned triggers add disks and bounded planes.
/// </summary>
public enum AreaTriggerShape : byte
{
    Sphere = 0,
    Box = 1,
    Polygon = 3,
    Cylinder = 4,
    Disk = 7,
    BoundedPlane = 8
}

/// <summary>
/// Which SmartAI script an AreaTrigger's rules belong to: a client trigger (source_type 2, keyed by its
/// ID) or a TrinityCore server-spawned trigger (source_type 11, or 12 for custom templates, keyed by the
/// areatrigger_template ID). <see cref="SpawnId"/> names the spawn the editor was opened from, whose
/// ScriptName decides whether the rules run.
/// </summary>
public readonly record struct AreaTriggerOwner(uint Id, uint SourceType = SmartScope.AreaTrigger, ulong SpawnId = 0)
{
    public bool IsEntity => SourceType is SmartScope.AreaTriggerEntity or SmartScope.AreaTriggerEntityCustom;
}

/// <summary>
/// One read-only AreaTrigger with its links. Coordinates are server space. A client trigger is an
/// `areatrigger` row (AzerothCore) or an AreaTrigger.db2 row (TrinityCore); a TrinityCore server spawn
/// (<see cref="SpawnId"/> non-zero) is an `areatrigger` row shaped by its areatrigger_create_properties
/// and owned by the areatrigger_template <see cref="Entry"/>.
/// Shapes: a sphere of Radius; a box centered on (X, Y, Z) whose Length runs along Orientation, Width
/// across it and Height vertically (both halves around Z); a polygon of <see cref="Vertices"/> (offsets
/// from the center, already turned to world axes) between <see cref="Bottom"/> and <see cref="Top"/>
/// (offsets from Z); a cylinder of Radius between Bottom and Top; a disk (a cylinder with
/// <see cref="InnerRadius"/> cut out); a bounded plane, a vertical Width × Height rectangle across the
/// facing that catches players within <see cref="PlaneReach"/>.
/// </summary>
public sealed record AreaTriggerRecord(uint Entry, ushort Map, double X, double Y, double Z, double Radius, double Length, double Width, double Height, double Orientation,
    string ScriptName = "", AreaTriggerTeleport? Teleport = null, uint QuestId = 0, string QuestTitle = "", string Tavern = "", int SmartRows = 0)
{
    /// <summary>The AreaTriggerScript that runs a client trigger's SmartAI rules (both cores).</summary>
    public const string SmartTrigger = "SmartTrigger";

    /// <summary>The AreaTriggerEntityScript that runs a TrinityCore spawned trigger's SmartAI rules.</summary>
    public const string SmartEntity = "SmartAreaTriggerAI";

    /// <summary>A unit's combat reach (DEFAULT_PLAYER_COMBAT_REACH), how close a bounded plane catches it.</summary>
    public const double PlaneReach = 1.5;

    readonly AreaTriggerShape? volume;

    /// <summary>The shape; rows that do not say (AzerothCore) are spheres when Radius &gt; 0, else boxes.</summary>
    public AreaTriggerShape Volume
    {
        get => volume ?? (Radius > 0 ? AreaTriggerShape.Sphere : AreaTriggerShape.Box);
        init => volume = value;
    }

    public IReadOnlyList<(double X, double Y)> Vertices { get; init; } = [];
    public double InnerRadius { get; init; }

    /// <summary>Vertical bounds of polygons, cylinders and disks, relative to Z; infinite when the server has none.</summary>
    public double Bottom { get; init; }
    public double Top { get; init; }

    /// <summary>A client polygon's corner height relative to Z, where its volume is drawn and focused; null when Z serves.</summary>
    public double? Ground { get; init; }

    /// <summary>
    /// Where the volume is: the corners' centroid at <see cref="Ground"/> for a polygon (many client
    /// polygons sit at the map origin with world-space corners), else the center. Region queries,
    /// ordering, labels and fly-to use it.
    /// </summary>
    public Point3 Focus => Volume == AreaTriggerShape.Polygon && Vertices.Count > 0
        ? new(X + Vertices.Average(v => v.X), Y + Vertices.Average(v => v.Y), Z + (Ground ?? 0))
        : new(X, Y, Z);

    /// <summary>The server spawn (TrinityCore `areatrigger.SpawnId`), 0 for a client trigger.</summary>
    public ulong SpawnId { get; init; }

    /// <summary>A spawn's template is a custom one (IsCustom), whose rules are source_type 12.</summary>
    public bool IsCustom { get; init; }
    public uint CreatePropertiesId { get; init; }
    public string Comment { get; init; } = "";

    public bool IsSpawn => SpawnId != 0;
    public bool IsSphere => Volume == AreaTriggerShape.Sphere;

    /// <summary>The overlay's key: client triggers and spawns are numbered separately.</summary>
    public AreaTriggerKey Key => new(IsSpawn, IsSpawn ? SpawnId : Entry);

    public AreaTriggerOwner Owner => IsSpawn
        ? new(Entry, IsCustom ? SmartScope.AreaTriggerEntityCustom : SmartScope.AreaTriggerEntity, SpawnId)
        : new(Entry);

    public AreaTriggerKind Kind => ScriptName == (IsSpawn ? SmartEntity : SmartTrigger) ? AreaTriggerKind.SmartAI : ScriptName.Length > 0 ? AreaTriggerKind.Script
        : Teleport != null ? AreaTriggerKind.Teleport : QuestId != 0 ? AreaTriggerKind.Quest : Tavern.Length > 0 ? AreaTriggerKind.Tavern : AreaTriggerKind.Unlinked;

    /// <summary>
    /// Server membership test. Client triggers follow Player::IsInAreaTrigger (and AzerothCore's
    /// IsInAreaTriggerRadius): sphere distance, Position::IsWithinBox, IsInPolygon2D capped by the
    /// path's volume height, IsWithinVerticalCylinder. Spawns follow AreaTrigger::SearchUnitIn*, whose
    /// shapes turn with the spawn's orientation. <paramref name="delta"/> widens every bound, as the
    /// tavern tolerance (5 yd) does.
    /// </summary>
    public bool Contains(Point3 server, double delta = 0)
    {
        double dx = server.X - X, dy = server.Y - Y, dz = server.Z - Z;
        bool within(double bottom, double top) => dz >= bottom - delta && dz <= top + delta;
        double flat = Math.Sqrt(dx * dx + dy * dy);
        switch (Volume)
        {
            case AreaTriggerShape.Sphere:
                return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= Radius + delta;
            case AreaTriggerShape.Polygon:
                return within(Bottom, Top) && InPolygon(dx, dy);
            case AreaTriggerShape.Cylinder:
                return within(Bottom, Top) && flat <= Radius + delta;
            case AreaTriggerShape.Disk:
                return within(Bottom, Top) && flat <= Radius + delta && flat >= InnerRadius - delta;
        }

        var (along, across) = Local(dx, dy);
        if (Volume == AreaTriggerShape.BoundedPlane)
        {
            double y = Math.Max(Math.Abs(across) - Width / 2, 0), z = Math.Max(Math.Abs(dz) - Height / 2, 0);
            return Math.Sqrt(along * along + y * y + z * z) <= PlaneReach + delta;
        }

        return Math.Abs(along) <= Length / 2 + delta && Math.Abs(across) <= Width / 2 + delta && Math.Abs(dz) <= Height / 2 + delta;
    }

    /// <summary>An offset turned into the facing's frame (Position::IsWithinBox rotates by 2π - orientation).</summary>
    (double Along, double Across) Local(double dx, double dy)
    {
        double rotation = 2 * Math.PI - Orientation;
        return (dx * Math.Cos(rotation) - dy * Math.Sin(rotation), dy * Math.Cos(rotation) + dx * Math.Sin(rotation));
    }

    /// <summary>Position::IsInPolygon2D: a ray cast to +X, counting the edges it crosses.</summary>
    bool InPolygon(double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
        {
            var (a, b) = (Vertices[i], Vertices[j]);
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Horizontal reach from the center, for region bounds and label distance.</summary>
    public double Reach => Volume switch
    {
        AreaTriggerShape.Sphere or AreaTriggerShape.Cylinder or AreaTriggerShape.Disk => Radius,
        AreaTriggerShape.Polygon => Vertices.Select(v => Math.Sqrt(v.X * v.X + v.Y * v.Y)).DefaultIfEmpty(0).Max(),
        AreaTriggerShape.BoundedPlane => Width / 2 + PlaneReach,
        _ => Math.Sqrt(Length * Length + Width * Width) / 2
    };

    public string Shape => Volume switch
    {
        AreaTriggerShape.Sphere => $"Sphere, radius {Radius:0.##} yd",
        AreaTriggerShape.Polygon => $"Polygon of {Vertices.Count} corners, {Span()}",
        AreaTriggerShape.Cylinder => $"Cylinder, radius {Radius:0.##} yd, {Span()}",
        AreaTriggerShape.Disk => $"Disk, radius {InnerRadius:0.##}-{Radius:0.##} yd, {Span()}",
        AreaTriggerShape.BoundedPlane => $"Plane {Width:0.##} × {Height:0.##} yd (width × height), within {PlaneReach:0.#} yd",
        _ => $"Box {Length:0.##} × {Width:0.##} × {Height:0.##} yd (length × width × height)"
    };

    string Span() => (double.IsInfinity(Bottom), double.IsInfinity(Top)) switch
    {
        (true, true) => "any height",
        (true, false) => $"up to {Top:0.##} yd above the center, no floor",
        (false, true) => $"from {Bottom:0.##} yd, no ceiling",
        _ => $"from {Bottom:0.##} to {Top:0.##} yd relative to the center"
    };
}

/// <summary>Identifies an overlay AreaTrigger: a client trigger ID, or a TrinityCore spawn ID.</summary>
public readonly record struct AreaTriggerKey(bool Spawn, ulong Id)
{
    const ulong SpawnBit = 1UL << 63;

    /// <summary>One number for list metadata: spawns set the top bit (SpawnId never reaches it).</summary>
    public ulong ListId => Spawn ? Id | SpawnBit : Id;

    public static AreaTriggerKey FromListId(ulong id) => (id & SpawnBit) != 0 ? new(true, id & ~SpawnBit) : new(false, id);

    public override string ToString() => Spawn ? $"spawn {Id}" : Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
