using ACWorldEdit.Foundation;

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
WorldPose pose = new(0, -100, 200, 30, 0);
using (var workspace = new DraftWorkspace(root))
{
    Reject(() => { using var second = new DraftWorkspace(root); }, "Concurrent writer accepted");
    id = workspace.Place(2843, pose); workspace.Move(id, pose with { X = -101 });
    duplicate = workspace.Duplicate(id); workspace.Delete(id); workspace.Undo();
    Check(workspace.Placements.Count == 2, "Undo failed"); workspace.Checkpoint();
}
using (var workspace = new DraftWorkspace(root))
{
    Check(workspace.Placements[id].Pose.X == -101 && workspace.Placements.ContainsKey(duplicate), "Restart lost stable ID/state");
    workspace.Redo(); Check(!workspace.Placements.ContainsKey(id), "Redo after restart failed");
    workspace.Undo(); workspace.Move(id, pose with { Map = 1 }); workspace.Redo();
    Check(workspace.Placements[id].Pose.Map == 1, "Branch failed to discard redo");
    Reject(() => workspace.Move(id, pose with { X = double.NaN }), "Nonfinite command accepted");
    workspace.Checkpoint();
}
File.WriteAllText(Path.Combine(root, "checkpoint.tmp"), "interrupted checkpoint");
File.AppendAllText(journal, "{\"Payload\":\"torn");
using (var workspace = new DraftWorkspace(root))
{
    Check(workspace.RecoveryDiagnostic != null && workspace.Placements[id].Pose.Map == 1, "Torn tail recovery failed");
    workspace.Delete(duplicate); workspace.Checkpoint();
}
using (var workspace = new DraftWorkspace(root)) Check(workspace.Placements.Count == 1, "Phantom recovery command");
string corrupt = root + "-corrupt"; Directory.CreateDirectory(corrupt);
File.WriteAllText(Path.Combine(corrupt, "journal.jsonl"), File.ReadAllText(journal).Replace("2843", "2844"));
Reject(() => { using var workspace = new DraftWorkspace(corrupt); }, "Checksum corruption accepted");
Console.WriteLine("PASS commands/recovery: place, move, duplicate, delete, undo/redo, restart, map switch, branching, torn tail, checkpoint interruption, corruption rejection, writer lock.");
Console.WriteLine("Workspace fixture artifacts: " + root);
