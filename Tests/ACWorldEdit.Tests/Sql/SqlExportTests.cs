using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// SQL export: net-change collapsing in the three drafts and the generated forward/reverse scripts
/// (guards, escaping, column groups). The scripts are not executed here; applying them to a
/// disposable database is a separate, explicitly authorized check.
/// </summary>
public class SqlExportTests
{
    [Fact]
    public void Passes() => Core();

    static void Core()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N")));
        var home = new WorldPose(0, -9425.82, -2119.2, 66.5052, 4.17939);
        var near = home with { X = -9420 };
        var far = home with { X = -9400, Heading = 1.5 };

        // Spawn moves are row edits of the `creature` table on the content draft.
        var spawnTable = AcTables.SpawnCreature;
        var spawn = SpawnEdits.NewCreature(AcSchema.Instance, 86251, 1234, home, 0, 0);
        var spawnFive = SpawnEdits.NewCreature(AcSchema.Instance, 5, 1234, home, 0, 0);
        var spawnSeven = SpawnEdits.NewCreature(AcSchema.Instance, 7, 1234, home, 0, 0);
        RowEdit MoveTo(Row from, WorldPose pose) => InteractionEdits.Update(spawnTable, from, SpawnEdits.WithPose(from, pose));

        var graveyard = new GraveyardRecord(4, 0, -10546.9, 1197.24, 31.7263, "Sentinel's Hill", [new(40, 0, "Westfall"), new(12, 469, "")]);
        using var graveyards = new GraveyardDraft(Path.Combine(root, "g.json"));
        var edited = graveyard with { X = -10540, Links = [new(40, 67, "Westfall"), new(44, 0, "Redridge")] };
        graveyards.Edit("db", graveyard, edited);
        var gnet = graveyards.NetEdits();
        Check(gnet.Count == 1 && gnet[0].Before == graveyard.Normalized() && gnet[0].After == edited.Normalized(), "Graveyard net edit");

        var template = AcTables.CreatureTemplate;
        var before = Row.Of(("entry", 100u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", "Old \"sub\""));
        var middle = before.With("gossip_menu_id", 7u);
        var after = before.With("subname", "It's a \\ trick\nline").With("gossip_menu_id", 7u);
        var menu = Row.Of(("MenuID", 9u), ("TextID", 1u));
        var transient = Row.Of(("MenuID", 9u), ("TextID", 2u));
        var ai = Row.Of(("entry", 100u), ("AIName", ""));
        using var content = new InteractionDraft(Path.Combine(root, "i.json"));
        var snapshot = new RowSet();
        snapshot.Set(template, before);
        snapshot.Set(AcTables.CreatureAI, ai);
        foreach (var row in new[] { spawn, spawnFive, spawnSeven })
        {
            snapshot.Set(spawnTable, row);
        }

        var spawnNear = SpawnEdits.WithPose(spawn, near);
        content.Execute("db", snapshot, "move", [MoveTo(spawn, near)]);
        content.Execute("db", snapshot, "move again", [MoveTo(spawnNear, far)]);
        content.Execute("db", snapshot, "move five", [MoveTo(spawnFive, near)]);
        content.Execute("db", snapshot, "move five back", [MoveTo(SpawnEdits.WithPose(spawnFive, near), home)]);
        content.Execute("db", snapshot, "move seven", [MoveTo(spawnSeven, near)]);
        content.Undo();
        content.Execute("db", snapshot, "edit",[new("creature_template", "100", before, middle), new("gossip_menu", "9|1", null, menu)]);
        content.Execute("db", snapshot, "edit again", [new("creature_template", "100", middle, after), new("creature_ai", "100", ai, ai.With("AIName", "SmartAI")), new("gossip_menu", "9|2", null, transient)]);
        content.Execute("db", snapshot, "remove", [new("gossip_menu", "9|2", transient, null)]);
        var rows = content.NetEdits();
        Check(rows.Count == 4 && rows.All(r => r.Source == "db"), "Insert-then-delete must cancel, round-trip and undone spawn moves must drop; got " + rows.Count);
        var spawnEdit = rows.Single(r => r.Edit.Table == "creature").Edit;
        Check(spawnEdit.Before == spawn && spawnEdit.After == SpawnEdits.WithPose(spawn, far), "Spawn net move must keep the first baseline and the last value");
        var templateEdit = rows.First(r => r.Edit.Table == "creature_template").Edit;
        Check(templateEdit.Before == before && templateEdit.After == after, "Row net edit");

        var scripts = SqlExport.Build("necron/acore_world", gnet, rows.Select(r => r.Edit), new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc));
        // (graveyard position/name, link 12 removed, link 40 faction, link 44 added) + 1 creature + 3 rows.
        Check(scripts.Changes == 8 && scripts.Summary.Count == 8, "Change count " + scripts.Changes);
        string apply = scripts.Apply;
        string revert = scripts.Revert;

        // Only the changed columns are set and guarded (the heading changed too, the Y position did not).
        Check(apply.Contains("UPDATE `creature` SET `position_x` = -9400, `orientation` = 1.5 WHERE `guid` = 86251 AND ABS(`position_x` - (-9425.82)) < 0.0001", StringComparison.Ordinal), "Creature update and freshness guard");
        Check(!apply.Contains("`position_y` =", StringComparison.Ordinal), "An unchanged column must stay out of the update");
        Check(revert.Contains("WHERE `guid` = 86251 AND ABS(`position_x` - (-9400)) < 0.0001", StringComparison.Ordinal), "Creature revert guard");
        Check(apply.Contains("UPDATE `creature_template` SET `AIName` = 'SmartAI' WHERE `entry` = 100 AND BINARY COALESCE(`AIName`, '') = BINARY ''", StringComparison.Ordinal), "creature_ai must target creature_template with only its column group");
        Check(apply.Contains("`subname` = 'It\\'s a \\\\ trick\\nline'", StringComparison.Ordinal), "Text escaping");
        Check(apply.Contains("INSERT INTO `gossip_menu` (`MenuID`, `TextID`) VALUES (9, 1);", StringComparison.Ordinal) && revert.Contains("DELETE FROM `gossip_menu` WHERE `MenuID` = 9 AND `TextID` = 1;", StringComparison.Ordinal), "Insert/delete pair");
        Check(apply.Contains("INSERT INTO `graveyard_zone`", StringComparison.Ordinal) && apply.Contains("DELETE FROM `graveyard_zone` WHERE `ID` = 4 AND `GhostZone` = 12", StringComparison.Ordinal) && apply.Contains("UPDATE `graveyard_zone` SET `Faction` = 67", StringComparison.Ordinal), "Graveyard link diff");
        Check(apply.IndexOf("Preflight", StringComparison.Ordinal) < apply.IndexOf("START TRANSACTION", StringComparison.Ordinal) && apply.Contains("Post-check", StringComparison.Ordinal), "Preflight and post-check framing");
        Check(!apply.Contains("86251;", StringComparison.Ordinal) && !apply.Contains("9|1", StringComparison.Ordinal), "Composite keys must not leak into SQL");
        foreach (string script in new[] { apply, revert })
        {
            Check(script.Split('\n').Where(l => !l.StartsWith("--", StringComparison.Ordinal) && l.Trim().Length > 0).All(l => l.EndsWith(';')), "Every statement must be one line ending in a semicolon");
            Check(!script.Contains('\r', StringComparison.Ordinal), "Scripts must use LF");
        }

        // Revert runs in reverse order: the last forward change (a content row) is undone first.
        Check(revert.IndexOf("DELETE FROM `gossip_menu`", StringComparison.Ordinal) < revert.IndexOf("UPDATE `creature` SET", StringComparison.Ordinal), "Revert order");
        var empty = SqlExport.Build("db", [], []);
        Check(empty.Changes == 0 && empty.Apply.Contains("Nothing to export", StringComparison.Ordinal), "Empty export");
        Check(SqlExport.Quote("a'b") == "'a\\'b'" && SqlExport.Quote("x\0y") == "'x\\0y'", "Quote");

        // Pending changes: per-change discard reverts only that change; exporting clears it.
        var store = new PendingChanges(Path.Combine(root, "exported.json"));
        var list = store.Collect(graveyards.NetEdits(), content.NetEdits());
        Check(list.Count == 5, "Pending list is one entry per graveyard and row (a spawn move is a row); got " + list.Count);
        Check(list.Select(c => c.Id).Distinct().Count() == list.Count, "Pending ids must be unique");
        var again = store.Collect(graveyards.NetEdits(), content.NetEdits());
        Check(list.Select(c => c.Id).SequenceEqual(again.Select(c => c.Id)), "Pending ids must be stable between collections");
        content.Revert("db", list.First(c => c.Row?.Table == "creature").Row!);
        Check(graveyards.NetEdits().Count == 1 && content.NetEdits().Count == 3 && content.NetEdits().All(r => r.Edit.Table != "creature"), "Discarding a spawn move leaves graveyards and the other rows alone");
        content.Revert("db", list.First(c => c.Row?.Table == "gossip_menu").Row!);
        var left = content.NetEdits();
        Check(left.Count == 2 && left.All(r => r.Edit.Table != "gossip_menu") && graveyards.NetEdits().Count == 1, "Discarding one row leaves the other rows and graveyards");
        graveyards.Revert(graveyards.NetEdits()[0]);
        var remaining = store.Collect(graveyards.NetEdits(), content.NetEdits());
        Check(remaining.Count == 2 && remaining.All(c => c.Kind == PendingKind.Row), "Only the untouched rows remain pending");
        store.MarkExported(remaining);
        Check(store.Collect(graveyards.NetEdits(), content.NetEdits()).Count == 0, "Exported changes leave the pending list");
        var reopened = new PendingChanges(Path.Combine(root, "exported.json"));
        Check(reopened.Collect(graveyards.NetEdits(), content.NetEdits()).Count == 0, "Exported state persists");
        content.Execute("db", snapshot, "move after export", [MoveTo(spawn, near)]);
        Check(reopened.Collect(graveyards.NetEdits(), content.NetEdits()).Count == 1, "A new change after export is pending again");
        File.WriteAllText(Path.Combine(root, "sample-apply.sql"), apply);
        File.WriteAllText(Path.Combine(root, "sample-revert.sql"), revert);
        Console.WriteLine("SQL_EXPORT_CHECKS_PASSED net changes, guards, escaping, column groups, insert/delete/update pairs; samples in " + root);
    }
}
