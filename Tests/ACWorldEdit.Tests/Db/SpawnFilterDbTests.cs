using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>SQL-only, read-only preview checks against the user's known event-bound mannequin.</summary>
[Trait("Category", "Db")]
public class SpawnFilterDbTests
{
    [DbFact]
    public async Task Passes() => await Core();

    static async Task Core()
    {
        var policy = new SpawnPreview();
        Check(policy.MatchesMasks(3, 1) && !policy.MatchesMasks(2, 1) && !policy.MatchesMasks(1, 2), "Mask overlap semantics");
        Check(!policy.MatchesEvents([31], []) && policy.MatchesEvents([-31], []), "Inactive positive/negative events");
        Check(policy.MatchesEvents([31], [31]) && !policy.MatchesEvents([-31], [31]), "Active event membership");
        Check((policy with { Events = EventPreviewMode.Selected, ActiveEvents = [31] }).MatchesEvents([31], []), "Manual event override");
        Check(SpawnPreview.AllStored.MatchesEvents([31, -31], []) && SpawnPreview.AllStored.MatchesMasks(0, 0), "Broad preview still needs no gameplay masks");
        try
        {
            (policy with
            {
                QueryLimit = 100000
            }

            ).Validate();
            throw new Exception("Unbounded query accepted");
        }
        catch (ArgumentException)
        {
        }

        var config = Config;
        var source = new AcCreatureSource(config);
        var region = new CreatureRegion(0, 31, 33, 48, 50);
        var point = new SpawnQueryPoint(-9492.72, 75.6383, 56.1841);
        var ordinary = await source.ReadAsync(region, policy, point, "96098", default);
        Check(ordinary.Spawns.Count == 0, "Inactive Arena Tournament GUID 96098 leaked into defaults");
        var broad = await source.ReadAsync(region, SpawnPreview.AllStored, point, "96098", default);
        Check(broad.Spawns.Count == 1 && broad.Spawns[0].TemplateId == 26724 && broad.Spawns[0].Visibility!.Events.Contains(31), "Mannequin association missing");
        var selected = await source.ReadAsync(region, policy with { Events = EventPreviewMode.Selected, ActiveEvents = [31] }, point, "96098", default);
        Check(selected.Spawns.Count == 1, "Manual event 31 did not include the mannequin");
        var phase = await source.ReadAsync(region, policy with { PhaseMask = 2, Events = EventPreviewMode.All }, point, "96098", default);
        Check(phase.Spawns.Count == 0, "Phase mismatch was not filtered in SQL");
        var bounded = await source.ReadAsync(region, policy with { QueryLimit = 3 }, point, "", default);
        Check(bounded.Spawns.Count == 3 && bounded.Truncated, "Filtering/nearest LIMIT contract failed");
        var scripted = await source.ReadAsync(region, policy with { IncludeScripted = false }, point, "", default);
        Check(scripted.Spawns.All(s => s.Visibility!.Script.Length == 0), "Script-binding filter failed");
        var objects = await new AcGameObjectSource(config).ReadAsync(region, policy, point, "", default);
        Check(objects.Spawns.Count > 0 && objects.Spawns.Count <= policy.QueryLimit, "No bounded GameObject snapshot");
        Check(objects.Spawns.All(s => s.Visibility.Pool == 0 && !s.Visibility.Conditional && !s.Visibility.Helper && !s.Visibility.InitiallyDespawned && policy.MatchesMasks(s.PhaseMask, s.SpawnMask)), "GameObject defaults leaked restricted spawns");
        var cancel = new CancellationTokenSource();
        Check(objects.Spawns.All(s => Math.Abs(s.Rotation.LengthSquared() - 1) < .0001f), "GameObject rotations are not normalized");
        var objectSource = new AcGameObjectSource(config);
        var objectLimit = await objectSource.ReadAsync(region, SpawnPreview.AllStored with { QueryLimit = 3 }, point, "", default);
        Check(objectLimit.Spawns.Count == 3 && objectLimit.Truncated, "Broad GameObject preview bypassed its SQL cap");
        var objectMask = await objectSource.ReadAsync(region, policy with { PhaseMask = 0 }, point, "", default);
        Check(objectMask.Spawns.Count == 0, "Zero phase mask included GameObjects");
        var injection = await objectSource.ReadAsync(region, policy, point, "' OR 1=1 --", default);
        Check(injection.Spawns.Count == 0, "Search text was interpreted as SQL");
        double Distance(WorldPose pose) => Math.Pow(pose.X - point.X, 2) + Math.Pow(pose.Y - point.Y, 2) + Math.Pow(pose.Z - point.Z, 2);
        var distances = objects.Spawns.Select(s => Distance(s.Pose)).ToArray();
        Check(distances.SequenceEqual(distances.Order()), "GameObject snapshot is not nearest first");
        Check(distances.All(d => d <= Math.Pow(policy.ViewDistance + 64, 2)), "GameObject query leaked beyond its neighborhood");
        Check(scripted.Spawns.All(s => s.Visibility!.Pool == 0 && !s.Visibility.Conditional && !s.Visibility.Helper && policy.MatchesMasks(s.PhaseMask, s.SpawnMask) && policy.MatchesEvents(s.Visibility.Events, scripted.Events!.Where(e => e.Scheduled).Select(e => e.Id))), "Creature defaults leaked restricted spawns");
        cancel.Cancel();
        try
        {
            await source.ReadAsync(region, policy, point, "", cancel.Token);
            throw new Exception("Canceled query accepted");
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine($"SPAWN_FILTER_CHECKS_PASSED guid=96098 default-hidden/all-visible/manual-event-visible phase-filter nearest-limit script-filter gameobjects={objects.Spawns.Count} cancellation read-only");
    }
}
