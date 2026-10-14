namespace ACWorldEdit.Foundation;

/// <summary>Where an AreaTrigger teleports players (areatrigger_teleport).</summary>
public sealed record AreaTriggerTeleport(string Name, ushort Map, double X, double Y, double Z, double Orientation);

/// <summary>
/// What an AreaTrigger does on the server, in the order MiscHandler.cpp HandleAreaTriggerOpcode
/// consults it: a script (C++ name or "SmartTrigger"), exploration quest credit, tavern rest,
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
/// One read-only `areatrigger` row with its links. Coordinates are server space; the shape is a
/// sphere when Radius &gt; 0, otherwise a box centered on (X, Y, Z) whose Length runs along
/// Orientation, Width across it and Height vertically (both halves around Z).
/// </summary>
public sealed record AreaTriggerRecord(uint Entry, ushort Map, double X, double Y, double Z, double Radius, double Length, double Width, double Height, double Orientation,
    string ScriptName = "", AreaTriggerTeleport? Teleport = null, uint QuestId = 0, string QuestTitle = "", string Tavern = "", int SmartRows = 0)
{
    public const string SmartTrigger = "SmartTrigger";
    public bool IsSphere => Radius > 0;
    public AreaTriggerKind Kind => ScriptName == SmartTrigger ? AreaTriggerKind.SmartAI : ScriptName.Length > 0 ? AreaTriggerKind.Script
        : Teleport != null ? AreaTriggerKind.Teleport : QuestId != 0 ? AreaTriggerKind.Quest : Tavern.Length > 0 ? AreaTriggerKind.Tavern : AreaTriggerKind.Unlinked;

    /// <summary>
    /// Server membership test (Player::IsInAreaTriggerRadius, Position::IsWithinBox): sphere
    /// distance, or the point rotated by -Orientation about the center compared against half
    /// extents. <paramref name="delta"/> is the tavern tolerance (5 yd) the server adds.
    /// </summary>
    public bool Contains(Point3 server, double delta = 0)
    {
        double dx = server.X - X, dy = server.Y - Y, dz = server.Z - Z;
        if (IsSphere)
        {
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= Radius + delta;
        }

        double rotation = 2 * Math.PI - Orientation;
        double localX = dx * Math.Cos(rotation) - dy * Math.Sin(rotation);
        double localY = dy * Math.Cos(rotation) + dx * Math.Sin(rotation);
        return Math.Abs(localX) <= Length / 2 + delta && Math.Abs(localY) <= Width / 2 + delta && Math.Abs(dz) <= Height / 2 + delta;
    }

    /// <summary>Horizontal reach from the center, for region bounds and label distance.</summary>
    public double Reach => IsSphere ? Radius : Math.Sqrt(Length * Length + Width * Width) / 2;

    public string Shape => IsSphere ? $"Sphere, radius {Radius:0.##} yd" : $"Box {Length:0.##} × {Width:0.##} × {Height:0.##} yd (length × width × height)";
}
