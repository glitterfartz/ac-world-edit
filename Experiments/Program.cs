using ACWorldEdit.Foundation;
using System.Globalization;
using System.Text.Json;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action action, string message)
{
    try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; } catch (ArgumentException) { return; }
    throw new Exception(message);
}
// Synthetic fixtures verify algebra and seam conventions, not real client landmark alignment.
int coordinateSamples = 0;
foreach (double x in new[] { -17000.0, -0.01, 0, 17000.0 })
foreach (double y in new[] { -1000.0, 0, 1000.0 })
{
    var server = new Point3(x, y, 123.456); var view = WorldCoordinates.ToView(server);
    Check(WorldCoordinates.ToServer(view) == server, "Coordinate inverse mismatch"); coordinateSamples++;
}
Check(WorldCoordinates.ToView(new(1, 0, 0)) == new Point3(0, 0, -1), "North basis");
Check(WorldCoordinates.ToView(new(0, 1, 0)) == new Point3(-1, 0, 0), "West basis");
Check(WorldCoordinates.Tile(new(0, 0, 0)) == (32, 32), "Tile origin convention");
Check(WorldCoordinates.Tile(new(0.01, 0.01, 0)) == (31, 31), "Tile seam positive side");
Check(WorldCoordinates.Tile(new(-0.01, -0.01, 0)) == (32, 32), "Tile seam negative side");
Point3[] cardinal = [new(0, 0, -1), new(-1, 0, 0), new(0, 0, 1), new(1, 0, 0)];
for (int i = 0; i < 4; i++)
{
    var error = WorldCoordinates.Heading(i * Math.PI / 2) - cardinal[i];
    Check(error.Dot(error) < 1e-20, "Cardinal heading mismatch");
}
Check(WorldCoordinates.Hit(new(0.25, 10, 0.25), new(0, -1, 0), new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)) == 10, "Terrain ray hit");
Check(WorldCoordinates.Hit(new(1, 10, 1), new(0, -1, 0), new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)) == null, "Outside triangle hit");
Console.WriteLine($"PASS coordinate algebra: {coordinateSamples} synthetic positions, seam sides, four headings, ray hit/miss. Real landmark/GO gates pending.");

string root = Path.GetFullPath(Path.Combine("artifacts", "foundation", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root); string journal = Path.Combine(root, "journal.jsonl");
Guid id, duplicate;
string expectedStateHash;
static string StateHash(DraftWorkspace workspace) => ContentHash.Of(JsonSerializer.Serialize(workspace.Placements.Values.OrderBy(p => p.StableId).ToArray()));
WorldPose pose = new(0, -100, 200, 30, 0);
using (var workspace = new DraftWorkspace(root))
{
    Reject(() => { using var second = new DraftWorkspace(root); }, "Concurrent writer accepted");
    id = workspace.Place(2843, pose); workspace.Move(id, pose with { X = -101 });
    duplicate = workspace.Duplicate(id); workspace.Delete(id); workspace.Undo();
    Check(workspace.Placements.Count == 2, "Undo failed"); workspace.Checkpoint();
    expectedStateHash = StateHash(workspace);
}
using (var workspace = new DraftWorkspace(root))
{
    Check(workspace.Placements[id].Pose.X == -101 && workspace.Placements.ContainsKey(duplicate), "Restart lost stable ID/state");
    Check(StateHash(workspace) == expectedStateHash, "Restart changed domain state hash");
    workspace.Redo(); Check(!workspace.Placements.ContainsKey(id), "Redo after restart failed");
    workspace.Undo(); workspace.Move(id, pose with { Map = 1 }); workspace.Redo();
    Check(workspace.Placements[id].Pose.Map == 1, "Branch failed to discard redo");
    Reject(() => workspace.Move(id, pose with { X = double.NaN }), "Nonfinite command accepted");
    workspace.Checkpoint();
    expectedStateHash = StateHash(workspace);
}
File.WriteAllText(Path.Combine(root, "checkpoint.tmp"), "interrupted checkpoint");
File.AppendAllText(journal, "{\"Payload\":\"torn");
using (var workspace = new DraftWorkspace(root))
{
    Check(workspace.RecoveryDiagnostic != null && workspace.Placements[id].Pose.Map == 1, "Torn tail recovery failed");
    Check(StateHash(workspace) == expectedStateHash, "Interrupted save changed domain state hash");
    workspace.Delete(duplicate); workspace.Checkpoint();
}
using (var workspace = new DraftWorkspace(root)) Check(workspace.Placements.Count == 1, "Phantom recovery command");
string corrupt = root + "-corrupt"; Directory.CreateDirectory(corrupt);
File.WriteAllText(Path.Combine(corrupt, "journal.jsonl"), File.ReadAllText(journal).Replace("2843", "2844"));
Reject(() => { using var workspace = new DraftWorkspace(corrupt); }, "Checksum corruption accepted");
Console.WriteLine("PASS commands/recovery: place, move, duplicate, delete, undo/redo, restart, map switch, branching, torn tail, checkpoint interruption, corruption rejection, writer lock.");
Console.WriteLine("Workspace fixture artifacts: " + root);

var schema = JsonSerializer.Deserialize<SchemaColumn[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "spawn-schema.json")))!;
var identity = new SchemaIdentity("5ddc706b7fdc", "ACDB 335.17-dev", "9.7.2",
    "IGNORE_SPACE,ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION");
var cells = new Dictionary<string, RawCell>
{
    ["guid"] = new("number", "42"), ["id"] = new("number", "2843"), ["map"] = new("number", "0"),
    ["position_x"] = new("number", "-14467.8"), ["position_y"] = new("number", "468.374"),
    ["position_z"] = new("number", "15.1064"), ["orientation"] = new("number", "0.139626"),
    ["future_column"] = new("text", "quotes '\\ unicode Ω retained"), ["VerifiedBuild"] = new("null", null),
    ["equipment_id"] = new("number", "-1")
};
var before = new SpawnRow(cells); cells["guid"] = new("number", "666");
Check(before.Guid == 42, "Row snapshot mutated through input");
var moved = before.WithPose(before.Pose with { X = -14466.8, Heading = Math.PI / 2 });
Check(Math.Abs(moved.Pose.X - (-14466.8)) <= 0.01 && Math.Abs(moved.Pose.Heading - Math.PI / 2) <= 0.01, "Persisted float quantization exceeds proposed tolerances");
Check(moved.Cells["future_column"] == before.Cells["future_column"] && moved.Cells["VerifiedBuild"] == before.Cells["VerifiedBuild"] &&
    moved.Cells["equipment_id"] == before.Cells["equipment_id"], "Unknown/signed/null fields lost");
var bundle = SpawnMoveExport.Compile(before, moved.Pose, schema, identity, "stock-12340-enUS-provisional");
var originalCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    Check(SpawnMoveExport.Compile(before, moved.Pose, schema.Reverse().ToArray(), identity, "stock-12340-enUS-provisional") == bundle,
        "Bundle ordering/culture nondeterministic");
}
finally { CultureInfo.CurrentCulture = originalCulture; }
Check(bundle.ForwardSql.Contains("WHERE `id` = 2843 AND `guid` = 42") && !bundle.ForwardSql.Contains("future_column"), "Unbounded or lossy SQL");
Reject(() => SpawnMoveExport.CheckBaseline(before, moved), "Stale row accepted");
Reject(() => SpawnMoveExport.CheckBaseline(before, null), "Missing row accepted");
SpawnMoveExport.CheckBaseline(before, before);
Reject(() => SpawnMoveExport.CheckAbsent(42, new uint[] { 42 }), "GUID collision accepted");
SpawnMoveExport.CheckAbsent(43, new uint[] { 42 });
Reject(() => SpawnMoveExport.Compile(before, before.Pose with { Map = 1 }, schema, identity, "test"), "Cross-map export accepted");
Reject(() => SpawnMoveExport.Compile(before, before.Pose, schema.Where(c => c.COLUMN_NAME != "orientation").ToArray(), identity, "test"), "Missing required schema accepted");
Reject(() => SpawnMoveExport.Compile(before, before.Pose, schema.Select(c => c.COLUMN_NAME == "position_x" ? c with { COLUMN_TYPE = "double" } : c).ToArray(), identity, "test"), "Schema type drift accepted");
File.WriteAllText(Path.Combine(root, "forward.sql"), bundle.ForwardSql);
File.WriteAllText(Path.Combine(root, "reverse.sql"), bundle.ReverseSql);
File.WriteAllText(Path.Combine(root, "manifest.json"), bundle.ManifestJson);
Console.WriteLine("PASS schema/export: fingerprint=" + SpawnSchema.Fingerprint(schema, identity));
Console.WriteLine("PASS unknown/signed/null retention, immutable rows, deterministic SQL/manifest, bounded predicates, stale/missing row and ID rejection, schema drift rejection. Real DB DML/reverse/transaction gates pending.");
