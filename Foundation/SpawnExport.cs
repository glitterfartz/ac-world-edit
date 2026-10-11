using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACWorldEdit.Foundation;

public sealed record SchemaColumn(string TABLE_NAME, string COLUMN_NAME, int ORDINAL_POSITION,
    string COLUMN_TYPE, string IS_NULLABLE, string? COLUMN_DEFAULT, string COLUMN_KEY, string EXTRA,
    string? CHARACTER_SET_NAME, string? COLLATION_NAME);
public sealed record SchemaIdentity(string CoreRevision, string DatabaseVersion, string ServerVersion, string SqlMode);
public static class SpawnSchema
{
    public static string Fingerprint(IEnumerable<SchemaColumn> columns, SchemaIdentity identity) => ContentHash.Of(
        JsonSerializer.Serialize(new { identity, columns = columns.OrderBy(c => c.TABLE_NAME, StringComparer.Ordinal).ThenBy(c => c.ORDINAL_POSITION).ToArray() }));
    public static void RequireCreatureMove(IEnumerable<SchemaColumn> columns)
    {
        var actual = columns.Where(c => c.TABLE_NAME == "creature").ToDictionary(c => c.COLUMN_NAME, StringComparer.Ordinal);
        foreach (var pair in new Dictionary<string, string> { ["guid"] = "int unsigned", ["id"] = "int unsigned", ["map"] = "smallint unsigned",
            ["position_x"] = "float", ["position_y"] = "float", ["position_z"] = "float", ["orientation"] = "float" })
            if (!actual.TryGetValue(pair.Key, out var column) || column.COLUMN_TYPE != pair.Value || column.IS_NULLABLE != "NO")
                throw new InvalidDataException("Unsupported creature column: " + pair.Key);
        if (actual["guid"].COLUMN_KEY != "PRI") throw new InvalidDataException("Expected creature guid primary key.");
    }
}
public static class ContentHash
{
    public static string Of(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
public sealed record RawCell(string Kind, string? Value);
public sealed class SpawnRow
{
    public IReadOnlyDictionary<string, RawCell> Cells { get; }
    public SpawnRow(IEnumerable<KeyValuePair<string, RawCell>> cells) => Cells = new ReadOnlyDictionary<string, RawCell>(
        new SortedDictionary<string, RawCell>(cells.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal));
    public string Hash => ContentHash.Of(JsonSerializer.Serialize(Cells));
    public uint Guid => Unsigned("guid");
    public uint Template => Unsigned("id");
    uint Unsigned(string name)
    {
        var cell = Cells[name];
        if (cell.Kind != "number" || !uint.TryParse(cell.Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint value))
            throw new InvalidDataException("Invalid unsigned spawn field " + name);
        return value;
    }
    double Number(string name)
    {
        var cell = Cells[name];
        if (cell.Kind != "number" || !double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new InvalidDataException("Invalid numeric spawn field " + name);
        return value;
    }
    public WorldPose Pose => new(checked((ushort)Unsigned("map")), Number("position_x"), Number("position_y"), Number("position_z"), Number("orientation"));
    public SpawnRow WithPose(WorldPose pose)
    {
        pose.Validate();
        if (pose.Map != Pose.Map) throw new InvalidDataException("Phase 0 export only supports same-map moves.");
        var cells = Cells.ToDictionary(p => p.Key, p => p.Value);
        foreach (var pair in new Dictionary<string, double> { ["position_x"] = pose.X, ["position_y"] = pose.Y, ["position_z"] = pose.Z, ["orientation"] = pose.Heading })
        {
            float persisted = (float)pair.Value;
            if (!float.IsFinite(persisted)) throw new InvalidDataException("Position exceeds SQL float range.");
            cells[pair.Key] = new("number", persisted.ToString("R", CultureInfo.InvariantCulture));
        }
        return new(cells);
    }
}
public sealed record MoveManifest(int FormatVersion, string Mode, string SchemaFingerprint, string ClientProfile,
    uint Guid, uint TemplateId, string BeforeHash, string AfterHash, IReadOnlyDictionary<string, RawCell> Before,
    IReadOnlyDictionary<string, RawCell> After, string ForwardHash, string ReverseHash, string[] Requirements);
public sealed record MoveBundle(string ForwardSql, string ReverseSql, string ManifestJson);

/// <summary>Review-only same-map creature pose experiment; no execution API or automatic safe-apply claim.</summary>
public static class SpawnMoveExport
{
    static readonly string[] poseColumns = ["position_x", "position_y", "position_z", "orientation"];
    public static MoveBundle Compile(SpawnRow baseline, WorldPose pose, SchemaColumn[] schema, SchemaIdentity identity, string clientProfile)
    {
        SpawnSchema.RequireCreatureMove(schema); _ = baseline.Pose;
        if (baseline.Guid == 0 || baseline.Template == 0) throw new InvalidDataException("Spawn identities must be positive.");
        var after = baseline.WithPose(pose); string forward = Sql(after), reverse = Sql(baseline);
        var manifest = new MoveManifest(1, "custom-review-only", SpawnSchema.Fingerprint(schema, identity), clientProfile,
            baseline.Guid, baseline.Template, baseline.Hash, after.Hash, baseline.Cells, after.Cells,
            ContentHash.Of(forward), ContentHash.Of(reverse),
            ["Manual review; SQL files do not enforce manifest preconditions", "Recheck schema and exact baseline before apply",
             "Recheck exact post-state before compensation", "Dependency/ownership checks and isolated clone proof pending", "No updater history mutations"]);
        return new(forward, reverse, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
    static string Sql(SpawnRow row)
    {
        // Only numeric pose columns are emitted. Unknown fields and strings never become executable SQL.
        _ = row.Pose;
        var assignments = poseColumns.Select(name => $"`{name}` = {NumericLiteral(row.Cells[name])}");
        return "-- REVIEW ONLY: verify manifest preconditions before executing.\nUPDATE `creature`\nSET " + string.Join(", ", assignments) +
            $"\nWHERE `id` = {row.Template.ToString(CultureInfo.InvariantCulture)} AND `guid` = {row.Guid.ToString(CultureInfo.InvariantCulture)};\n";
    }
    static string NumericLiteral(RawCell cell)
    {
        if (cell.Kind != "number" || !float.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new InvalidDataException("SQL float literal invalid.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
    public static void CheckBaseline(SpawnRow expected, SpawnRow? current)
    {
        if (current == null || expected.Hash != current.Hash) throw new InvalidDataException("Stale or missing source row.");
    }
    public static void CheckAbsent(uint proposedGuid, IEnumerable<uint> currentGuids)
    {
        if (proposedGuid == 0 || currentGuids.Contains(proposedGuid)) throw new InvalidDataException("Spawn GUID collision.");
    }
}
