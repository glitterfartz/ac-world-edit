using System.Numerics;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only AreaTrigger checks: table links against the server loaders, the bounded regional
/// source used by the viewer, the overlay's view transform against the server membership test, and
/// the behavior snapshot for Utgarde Pinnacle 5140 (Svala's intro). SELECTs in read-only
/// transactions only.
/// </summary>
[Trait("Category", "Db")]
public class AreaTriggerDbTests
{
    [DbFact]
    public async Task Passes() => await Core();

    static async Task Core()
    {
        var config = Config;
        var database = new WorldDatabase(config);
        long total, smart;
        await using (var session = await database.OpenAsync(CancellationToken.None))
        {
            async Task<long> Count(string sql)
            {
                await using var command = session.ReadOnly(sql);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }

            total = await Count("SELECT COUNT(*) FROM areatrigger");
            smart = await Count("SELECT COUNT(*) FROM areatrigger_scripts WHERE ScriptName='SmartTrigger'");
            long orphanScripts = await Count("SELECT COUNT(*) FROM areatrigger_scripts s LEFT JOIN areatrigger a ON a.entry=s.entry WHERE a.entry IS NULL");
            long orphanTeleports = await Count("SELECT COUNT(*) FROM areatrigger_teleport t LEFT JOIN areatrigger a ON a.entry=t.ID WHERE a.entry IS NULL");
            long orphanSmart = await Count("SELECT COUNT(DISTINCT ss.entryorguid) FROM smart_scripts ss LEFT JOIN areatrigger a ON a.entry=ss.entryorguid WHERE ss.source_type=2 AND a.entry IS NULL");
            long silent = await Count("SELECT COUNT(DISTINCT ss.entryorguid) FROM smart_scripts ss LEFT JOIN areatrigger_scripts s ON s.entry=ss.entryorguid WHERE ss.source_type=2 AND (s.ScriptName IS NULL OR s.ScriptName<>'SmartTrigger')");
            long spheres = await Count("SELECT COUNT(*) FROM areatrigger WHERE radius>0");
            Console.WriteLine($"AREATRIGGER_TABLES triggers={total} spheres={spheres} boxes={total - spheres} smartTrigger={smart} orphanScripts={orphanScripts} orphanTeleports={orphanTeleports} orphanSmartRules={orphanSmart} smartRulesWithoutSmartTrigger={silent}");
            Check(total > 0, "areatrigger is empty");
        }

        // Utgarde Pinnacle (WMO-only map 575) around trigger 5140, the region shape the viewer uses.
        var center = new Point3(312.646, -291.173, 104.702);
        var (tileX, tileY) = WorldCoordinates.Tile(center);
        var region = new CreatureRegion(575, tileX - 2, tileX + 2, tileY - 2, tileY + 2);
        var point = new SpawnQueryPoint(center.X, center.Y, center.Z);
        var source = AcSource(config);
        var result = await source.ReadAreaTriggersAsync(region, point, "", CancellationToken.None);
        Check(result.Triggers.Length > 0 && result.Triggers.Length <= AcAreaTriggerSource.Limit, "No bounded Utgarde Pinnacle triggers");
        double previous = 0;
        foreach (var t in result.Triggers)
        {
            Check(t.Map == 575 && t.X > region.MinServerX && t.X <= region.MaxServerX && t.Y > region.MinServerY && t.Y <= region.MaxServerY, "Trigger outside region");
            double distance = Math.Pow(t.X - point.X, 2) + Math.Pow(t.Y - point.Y, 2) + Math.Pow(t.Z - point.Z, 2);
            Check(distance >= previous, "Triggers not nearest-first");
            previous = distance;
        }

        var svala = result.Triggers.Single(t => t.Entry == 5140);
        Check(svala.Kind == AreaTriggerKind.SmartAI && svala.SmartRows == 1 && !svala.IsSphere && Math.Abs(svala.Length - 37.29) < 0.01 && Math.Abs(svala.Width - 13.79) < 0.01, "5140 shape or link");
        Console.WriteLine($"AREATRIGGER_SOURCE map=575 triggers={result.Triggers.Length} truncated={result.Truncated} kinds={string.Join(",", result.Triggers.GroupBy(t => t.Kind).Select(g => $"{g.Key}:{g.Count()}"))} nearest={result.Triggers[0].Entry} ({result.Triggers[0].Shape})");
        Check((await source.ReadAreaTriggersAsync(region, point, "5140", CancellationToken.None)).Triggers.Single().Entry == 5140, "ID search failed");
        Check((await source.ReadAreaTriggersAsync(region, point, "SmartTrigger", CancellationToken.None)).Triggers.All(t => t.ScriptName == AreaTriggerRecord.SmartTrigger), "Script search failed");
        Check((await source.ReadAreaTriggersAsync(region, point, "%", CancellationToken.None)).Triggers.Length == 0, "Search wildcard not escaped");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try
            {
                await source.ReadAreaTriggersAsync(region, point, "", canceled.Token);
                throw new Exception("Canceled AreaTrigger query completed");
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Rebuild each volume exactly as AreaTriggerOverlay places it (view center, yaw =
        // orientation about +Y, local scale (width, height, length)), then convert back to server
        // space: points just inside each face must pass the server test and just outside must fail.
        int sampled = 0;
        foreach (var t in result.Triggers.Concat([svala with { Orientation = 1.1 }, svala with { Radius = 0, Length = 20, Width = 4, Height = 6, Orientation = 4.0 }]))
        {
            if (t.Length < 0.5 && !t.IsSphere)
            {
                continue;
            }

            var viewCenter = WorldCoordinates.ToView(new(t.X, t.Y, t.Z));
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)t.Orientation);
            var scale = t.IsSphere ? new Vector3((float)t.Radius * 2) : new Vector3((float)t.Width, (float)t.Height, (float)t.Length);
            Point3 ToServer(Vector3 local)
            {
                var v = Vector3.Transform(local * scale, yaw);
                return WorldCoordinates.ToServer(new(viewCenter.X + v.X, viewCenter.Y + v.Y, viewCenter.Z + v.Z));
            }

            Vector3[] directions = t.IsSphere
                ? [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Normalize(new(1, 1, 1)), -Vector3.UnitX]
                : [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitZ, new(1, 1, 1), new(-1, 1, -1)];
            foreach (var direction in directions)
            {
                // Spheres: along a unit direction to the surface (radius 0.5); boxes: to a face or corner.
                var surface = direction * 0.5f;
                Check(t.Contains(ToServer(surface * 0.98f)), $"Trigger {t.Entry}: inside point {direction} fails the server test");
                Check(!t.Contains(ToServer(surface * 1.03f)), $"Trigger {t.Entry}: outside point {direction} passes the server test");
            }

            sampled++;
        }

        Console.WriteLine($"AREATRIGGER_VIEW_TRANSFORM volumes={sampled} faces/corners inside and outside agree with Position::IsWithinBox / sphere distance");

        // Behavior snapshot: the rule that starts Svala's intro and the script name that runs it.
        var snapshot = await source.ReadAreaTriggerBehaviorAsync(new(5140), [], rows => rows, null, CancellationToken.None);
        var scope = new SmartScope(5140, SmartScope.AreaTrigger);
        var rules = snapshot.Rows.All(InteractionTables.SmartScripts).Select(r => new SmartRule(r)).Where(r => r.Scope == scope).ToList();
        Check(snapshot.ScriptName == AreaTriggerRecord.SmartTrigger && rules.Count == 1, "5140 behavior snapshot");
        var rule = rules[0];
        Check(rule.Event == SmartCatalog.AreaTriggerEvent && rule.Action == 45 && rule.ActionParam(1) == 1 && rule.ActionParam(2) == 1 && rule.Target == 10 && rule.TargetParam(1) == 126115, "5140 rule is not set data 1 1 on spawn 126115");
        var issues = SmartCatalog.Ac.Check(scope, rules, snapshot.Names, null, 5140).Concat(SmartCatalog.Ac.CheckAreaTriggerScript(new(5140), snapshot.Rows.Get(InteractionTables.AreaTriggerScripts, "5140"), "", true)).ToList();
        Check(issues.All(i => !i.Error), "5140 rules report errors: " + string.Join("; ", issues.Select(i => i.Message)));
        Console.WriteLine($"AREATRIGGER_BEHAVIOR 5140 script={snapshot.ScriptName} rule=\"{SmartCatalog.Ac.Describe(rule, snapshot.Names, 5140)}\" warnings={issues.Count}");
        try
        {
            await source.ReadAreaTriggerBehaviorAsync(new(999999), [], rows => rows, null, CancellationToken.None);
            throw new Exception("Unknown AreaTrigger loaded");
        }
        catch (InvalidDataException)
        {
        }

        Console.WriteLine("AREATRIGGER_CHECKS_PASSED tables/region/order/search/escape/cancel/view-transform/behavior-5140/unknown-trigger");
    }
}
