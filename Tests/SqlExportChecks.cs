using ACWorldEdit.Foundation;
using static Test;

/// <summary>
/// SQL export: net-change collapsing in the three drafts and the generated forward/reverse scripts
/// (guards, escaping, column groups). The scripts are not executed here; applying them to a
/// disposable database is a separate, explicitly authorized check.
/// </summary>
public static class SqlExportChecks
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "draft-checks", Guid.NewGuid().ToString("N")));
        var home = new WorldPose(0, -9425.82, -2119.2, 66.5052, 4.17939);
        var near = home with { X = -9420 };
        var far = home with { X = -9400, Heading = 1.5 };

        using var creatures = new CreatureDraft(Path.Combine(root, "c.json"));
        creatures.Move("db", 86251, home, near);
        creatures.Move("db", 86251, home, far);
        creatures.Move("db", 5, home, near);
        creatures.Move("db", 5, home, home);
        creatures.Move("db", 7, home, near);
        creatures.Undo();
        var net = creatures.NetMoves();
        Check(net.Count == 1 && net[0].Guid == 86251 && net[0].Before == home && net[0].After == far, "Creature net moves must keep first baseline and last value, drop round trips and undone moves");

        var graveyard = new GraveyardRecord(4, 0, -10546.9, 1197.24, 31.7263, "Sentinel's Hill", [new(40, 0, "Westfall"), new(12, 469, "")]);
        using var graveyards = new GraveyardDraft(Path.Combine(root, "g.json"));
        var edited = graveyard with { X = -10540, Links = [new(40, 67, "Westfall"), new(44, 0, "Redridge")] };
        graveyards.Edit("db", graveyard, edited);
        var gnet = graveyards.NetEdits();
        Check(gnet.Count == 1 && gnet[0].Before == graveyard.Normalized() && gnet[0].After == edited.Normalized(), "Graveyard net edit");

        var template = InteractionTables.CreatureTemplate;
        var before = Row.Of(("entry", 100u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", "Old \"sub\""));
        var middle = before.With("gossip_menu_id", 7u);
        var after = before.With("subname", "It's a \\ trick\nline").With("gossip_menu_id", 7u);
        var menu = Row.Of(("MenuID", 9u), ("TextID", 1u));
        var transient = Row.Of(("MenuID", 9u), ("TextID", 2u));
        var ai = Row.Of(("entry", 100u), ("AIName", ""));
        using var content = new InteractionDraft(Path.Combine(root, "i.json"));
        var snapshot = new RowSet();
        snapshot.Set(template, before);
        snapshot.Set(InteractionTables.CreatureAI, ai);
        content.Execute("db", snapshot, "edit", [new("creature_template", "100", before, middle), new("gossip_menu", "9|1", null, menu)]);
        content.Execute("db", snapshot, "edit again", [new("creature_template", "100", middle, after), new("creature_ai", "100", ai, ai.With("AIName", "SmartAI")), new("gossip_menu", "9|2", null, transient)]);
        content.Execute("db", snapshot, "remove", [new("gossip_menu", "9|2", transient, null)]);
        var rows = content.NetEdits();
        Check(rows.Count == 3 && rows.All(r => r.Source == "db"), "Insert-then-delete must cancel; got " + rows.Count);
        var templateEdit = rows.First(r => r.Edit.Table == "creature_template").Edit;
        Check(templateEdit.Before == before && templateEdit.After == after, "Row net edit");

        var scripts = SqlExport.Build("necron/acore_world", net, gnet, rows.Select(r => r.Edit), new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc));
        // 1 creature + (graveyard position/name, link 12 removed, link 40 faction, link 44 added) + 3 rows.
        Check(scripts.Changes == 8 && scripts.Summary.Count == 8, "Change count " + scripts.Changes);
        string apply = scripts.Apply;
        string revert = scripts.Revert;

        Check(apply.Contains("UPDATE `creature` SET `position_x` = -9400, `position_y` = -2119.2", StringComparison.Ordinal), "Creature update");
        Check(apply.Contains("WHERE `guid` = 86251 AND ABS(`position_x` - (-9425.82)) < 0.0001", StringComparison.Ordinal), "Creature freshness guard");
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
        var empty = SqlExport.Build("db", [], [], []);
        Check(empty.Changes == 0 && empty.Apply.Contains("Nothing to export", StringComparison.Ordinal), "Empty export");
        Check(SqlExport.Quote("a'b") == "'a\\'b'" && SqlExport.Quote("x\0y") == "'x\\0y'", "Quote");
        File.WriteAllText(Path.Combine(root, "sample-apply.sql"), apply);
        File.WriteAllText(Path.Combine(root, "sample-revert.sql"), revert);
        Console.WriteLine("SQL_EXPORT_CHECKS_PASSED net changes, guards, escaping, column groups, insert/delete/update pairs; samples in " + root);
    }
}
