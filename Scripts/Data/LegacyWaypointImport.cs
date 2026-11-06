using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

// One entry of the retired waypoint draft file (waypoints.json): a point moved from Before to
// After, with the properties of that edit when it has them. Property names match the old file.
public sealed record LegacyWaypointEdit(string Source, uint Path, uint Point, LegacyPos Before, LegacyPos After, LegacyProps? BeforeProps, LegacyProps? AfterProps);
public sealed record LegacyPos(double X, double Y, double Z);
public sealed record LegacyProps(double? Orientation, double Velocity, uint Delay, bool Smooth, int MoveType, int Action, int ActionChance);

/// <summary>One net legacy move: the first baseline to the last state per (source, path, point).</summary>
public sealed record LegacyWaypointMove(string Source, uint Path, uint Point, LegacyPos Before, LegacyPos After, LegacyProps? BeforeProps, LegacyProps? AfterProps);

/// <summary>
/// Reads the retired waypoint draft for a one-time import into the shared content draft. Entries
/// collapse the way the old draft did (first baseline to last state, omitting edits that return to
/// the baseline); an edit whose baseline no longer matches the database is reported as a conflict
/// instead of being rebased.
/// </summary>
public static class LegacyWaypointImport
{
    /// <summary>Every net move in the legacy file, or empty when there is no file.</summary>
    public static IReadOnlyList<LegacyWaypointMove> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var envelope = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid waypoint draft envelope.");
        if (envelope.Length != 2 || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope[0]))) != envelope[1])
        {
            throw new InvalidDataException("Waypoint draft checksum mismatch.");
        }

        var snapshot = JsonSerializer.Deserialize<DraftSnapshot<LegacyWaypointEdit>>(envelope[0])
            ?? throw new InvalidDataException("Invalid waypoint draft.");
        if (snapshot.Version != 1 || snapshot.Cursor < 0 || snapshot.Cursor > snapshot.History.Length)
        {
            throw new InvalidDataException("Unsupported waypoint draft.");
        }

        return Net(snapshot.AppliedEntries());
    }

    static IReadOnlyList<LegacyWaypointMove> Net(IEnumerable<LegacyWaypointEdit> applied) => applied
        .GroupBy(e => (e.Source, e.Path, e.Point))
        .Select(g =>
        {
            var before = g.Select(e => e.BeforeProps).FirstOrDefault(p => p != null);
            var after = g.Last().AfterProps;
            return new LegacyWaypointMove(g.Key.Source, g.Key.Path, g.Key.Point, g.First().Before, g.Last().After,
                after == null ? null : before, after);
        })
        .Where(m => m.Before != m.After || (m.BeforeProps is { } b && m.AfterProps is { } a && b != a))
        .OrderBy(m => m.Path).ThenBy(m => m.Point)
        .ToList();

    /// <summary>
    /// The row update for one legacy move against its current record row: null when the move is
    /// already applied, conflict when the baseline moved under it. Positions compare at float
    /// precision, the way the old draft stored them.
    /// </summary>
    public static (RowEdit? Edit, bool Conflict) ToUpdate(IWorldSchema schema, Row recordRow, LegacyWaypointMove move)
    {
        var table = MovementEdits.NodeTable(schema);
        var after = WithState(recordRow, schema.Dialect, move.After, move.AfterProps);
        if (after == recordRow)
        {
            return (null, false);
        }

        var before = WithState(recordRow, schema.Dialect, move.Before, move.BeforeProps);
        if (before != recordRow)
        {
            return (null, true);
        }

        return (InteractionEdits.Update(table, recordRow, after), false);
    }

    static Row WithState(Row row, WorldDialect dialect, LegacyPos position, LegacyProps? props)
    {
        bool tc = dialect == WorldDialect.TrinityCore;
        var after = row
            .With(tc ? "PositionX" : "position_x", (float)position.X)
            .With(tc ? "PositionY" : "position_y", (float)position.Y)
            .With(tc ? "PositionZ" : "position_z", (float)position.Z);
        if (props == null)
        {
            return after;
        }

        after = after.With(tc ? "Orientation" : "orientation", props.Orientation is double o ? (float)o : null);
        if (tc)
        {
            return after.With("Delay", props.Delay);
        }

        return after.With("velocity", (float)props.Velocity)
            .With("delay", props.Delay)
            .With("smoothTransition", props.Smooth ? 1 : 0)
            .With("move_type", props.MoveType)
            .With("action", props.Action)
            .With("action_chance", props.ActionChance);
    }

    /// <summary>True when another process holds the legacy draft file open.</summary>
    public static bool LockHeld(string path)
    {
        try
        {
            using var _ = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Empty the legacy file after a full import (the file itself is kept).</summary>
    public static void Clear(string path)
    {
        string payload = JsonSerializer.Serialize(new DraftSnapshot<LegacyWaypointEdit>(1, [], 0));
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { payload, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) }), new UTF8Encoding(false));
    }
}
