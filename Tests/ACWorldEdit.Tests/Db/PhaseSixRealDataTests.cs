using System.Globalization;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using MySqlConnector;
using Xunit;

namespace ACWorldEdit.Tests.Db;

/// <summary>
/// Phase 6 refinements against the live AzerothCore and TrinityCore databases (and, where noted, the retail
/// client): real rows are found by query at run time, read through the editor's own sources and checked
/// against what the database holds. Nothing is written; the exported guards are run as read-only SELECTs.
/// </summary>
[Trait("Category", "Db")]
public class PhaseSixRealDataTests
{
    static async Task<List<object?[]>> Rows(DatabaseSettings settings, string sql)
    {
        Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
        await using var connection = new MySqlConnection(settings.BuildConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, true);
        await using var command = new MySqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            rows.Add(values.Select(v => v is DBNull ? null : v).ToArray());
        }

        return rows;
    }

    static uint U(object? value) => Convert.ToUInt32(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs every SELECT of an export script's preflight against the live database, in a read-only transaction,
    /// and returns the state each one reports ("ok" or "STALE").
    /// </summary>
    static async Task<List<(string Change, string State)>> Preflight(DatabaseSettings settings, string script)
    {
        var lines = script.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int start = lines.IndexOf("-- Preflight (read-only)") + 1;
        int end = lines.IndexOf("START TRANSACTION;");
        Assert.True(start > 0 && end > start, "the script has a preflight");
        var states = new List<(string, string)>();
        foreach (string select in lines.Skip(start).Take(end - start).Where(l => l.StartsWith("SELECT", StringComparison.Ordinal)))
        {
            var row = Assert.Single(await Rows(settings, select));
            states.Add((Convert.ToString(row[0], CultureInfo.InvariantCulture)!, Convert.ToString(row[1], CultureInfo.InvariantCulture)!));
        }

        Assert.NotEmpty(states);
        return states;
    }

    // ---- AC gaps: BoxBroadcastTextID and ReqAbility1-3 ------------------------------------------------------

    [DbFact]
    public async Task AzerothCoreReadsARealOptionsConfirmationBroadcastAndNamesItsText()
    {
        var found = Assert.Single(await Rows(Test.Config, "SELECT t.entry, o.MenuID, o.OptionID, o.BoxBroadcastTextID FROM creature_template t JOIN gossip_menu_option o ON o.MenuID=t.gossip_menu_id WHERE o.BoxBroadcastTextID<>0 AND EXISTS(SELECT 1 FROM broadcast_text b WHERE b.ID=o.BoxBroadcastTextID) ORDER BY t.entry LIMIT 1"));
        uint entry = U(found[0]), menu = U(found[1]), option = U(found[2]), box = U(found[3]);
        var snapshot = await Test.AcSource(Test.Config).ReadInteractionAsync(entry, 0, [], rows => rows, null, CancellationToken.None);
        var row = snapshot.Rows.All(AcTables.GossipOption).Single(r => r.U("MenuID") == menu && r.U("OptionID") == option);
        Assert.Equal(box, row.U("BoxBroadcastTextID"));
        // The confirmation text is the broadcast text's, as the server sends it, not the option's own BoxText.
        var said = snapshot.Lookup.BroadcastText(box);
        Assert.NotNull(said);
        Assert.NotEmpty(said.Value.Male);
        var view = AcCreatureView.Build(entry, 0, snapshot.Name, snapshot.ScriptName, snapshot.AIName, snapshot.SpawnCount, snapshot.Rows, snapshot.Rows, snapshot.Lookup, snapshot.Scripts, snapshot.Conditions);
        Assert.Equal(said.Value.Male, view.Menus[menu].Options.Single(o => o.OptionId == option).BoxText);
        Assert.Contains(AcSchema.Instance.GossipPage.Options.Fields, f => f.Column == "BoxBroadcastTextID");
    }

    [DbFact]
    public async Task AzerothCoreReadsARealTrainerSpellsRequiredAbilities()
    {
        var found = Assert.Single(await Rows(Test.Config, "SELECT d.CreatureId, s.TrainerId, s.SpellId, s.ReqAbility1, s.ReqAbility2, s.ReqAbility3 FROM creature_default_trainer d JOIN trainer_spell s ON s.TrainerId=d.TrainerId WHERE s.ReqAbility1<>0 ORDER BY d.CreatureId, s.SpellId LIMIT 1"));
        uint entry = U(found[0]), trainer = U(found[1]), spell = U(found[2]);
        var snapshot = await Test.AcSource(Test.Config).ReadInteractionAsync(entry, 0, [], rows => rows, null, CancellationToken.None);
        var row = snapshot.Rows.All(AcTables.TrainerSpell).Single(r => r.U("TrainerId") == trainer && r.U("SpellId") == spell);
        Assert.Equal([U(found[3]), U(found[4]), U(found[5])], new[] { row.U("ReqAbility1"), row.U("ReqAbility2"), row.U("ReqAbility3") });
        Assert.NotEqual(0u, row.U("ReqAbility1"));
        var fields = AcSchema.Instance.TrainerPage.Spells.Fields;
        Assert.All(new[] { "ReqAbility1", "ReqAbility2", "ReqAbility3" }, column => Assert.Contains(fields, f => f.Column == column));
    }

    // ---- Schema-driven pages: the AzerothCore stats rules hold for the real creatures ------------------------

    [DbFact]
    public async Task EveryFieldOfBothStatsPagesIsARealColumnOfTheLiveDatabase()
    {
        foreach (var (settings, schema, database) in new[]
        {
            (Test.Config, (IWorldSchema)AcSchema.Instance, "creature_template"),
            (Environments.TrinityConnection()?.World, TcSchema.Instance, "creature_template_difficulty")
        })
        {
            if (settings == null)
            {
                continue;
            }

            foreach (var field in schema.StatsPage.Fields)
            {
                // creature_stats is a column group of creature_template in AzerothCore.
                string table = field.Table.SqlName == "creature_stats" ? "creature_template" : field.Table.SqlName;
                var count = Assert.Single(await Rows(settings, $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='{table}' AND column_name='{field.Column}'"));
                Assert.Equal(1L, Convert.ToInt64(count[0], CultureInfo.InvariantCulture));
            }

            Assert.NotEmpty(database);
        }
    }

    [DbFact]
    public async Task TheAzerothCoreStatsRulesAcceptSampledRealCreaturesIncludingTheExtremes()
    {
        string[] picks =
        [
            "SELECT entry FROM creature_template ORDER BY maxlevel DESC, entry LIMIT 3",
            "SELECT entry FROM creature_template ORDER BY HealthModifier DESC, entry LIMIT 3",
            "SELECT entry FROM creature_template WHERE minlevel<maxlevel ORDER BY entry LIMIT 3",
            "SELECT entry FROM creature_template WHERE `exp`=2 ORDER BY entry LIMIT 3",
            "SELECT entry FROM creature_template WHERE `exp`=0 ORDER BY entry LIMIT 3",
            "SELECT entry FROM creature_template WHERE unit_class IN (2,4,8) ORDER BY entry LIMIT 6"
        ];
        var entries = new SortedSet<uint>();
        foreach (string pick in picks)
        {
            foreach (var row in await Rows(Test.Config, pick))
            {
                entries.Add(U(row[0]));
            }
        }

        Assert.True(entries.Count >= 10);
        var source = Test.AcSource(Test.Config);
        foreach (uint entry in entries)
        {
            var snapshot = await source.ReadInteractionAsync(entry, 0, [], rows => rows, null, CancellationToken.None);
            Assert.NotNull(snapshot.Rows.Get(AcTables.CreatureStats, entry.ToString(CultureInfo.InvariantCulture)));
            Assert.Empty(AcStatsPage.Check(snapshot.Rows, entry));
        }

        // The whole table, in one query: nothing the rules would refuse.
        var bad = Assert.Single(await Rows(Test.Config, "SELECT COUNT(*) FROM creature_template WHERE minlevel<1 OR minlevel>255 OR maxlevel<1 OR maxlevel>255 OR minlevel>maxlevel OR `exp`>2 OR HealthModifier<0 OR ManaModifier<0 OR ArmorModifier<0 OR DamageModifier<0"));
        Assert.Equal(0L, Convert.ToInt64(bad[0], CultureInfo.InvariantCulture));
    }

    // ---- Gossip: removing a TrinityCore option removes its trainer binding ------------------------------------

    [TcDbFact]
    public async Task RemovingARealTrainerOptionDeletesItsBindingAndTheExportGuardsMatchTheDatabase()
    {
        var connection = Environments.TrinityConnection()!;
        var found = Assert.Single(await Rows(connection.World, "SELECT ct.CreatureID, ct.MenuID, ct.OptionID, ct.TrainerID FROM creature_trainer ct WHERE ct.MenuID<>0 AND EXISTS(SELECT 1 FROM creature_template_gossip g WHERE g.CreatureID=ct.CreatureID AND g.MenuID=ct.MenuID) AND EXISTS(SELECT 1 FROM gossip_menu_option o WHERE o.MenuID=ct.MenuID AND o.OptionID=ct.OptionID) ORDER BY ct.CreatureID LIMIT 1"));
        uint entry = U(found[0]), menu = U(found[1]), option = U(found[2]), trainer = U(found[3]);
        var source = DBSource.Open(connection);
        var snapshot = await source.ReadInteractionAsync(entry, 0, [], rows => rows, null, CancellationToken.None);
        var page = source.Schema.GossipPage;
        var optionRow = snapshot.Rows.All(page.Options.Table).Single(r => r.U("MenuID") == menu && r.U("OptionID") == option);
        var edits = page.Options.Remove(optionRow, snapshot.Rows);
        Assert.Equal(2, edits.Length);
        var binding = Assert.Single(edits, e => e.Table == TcTables.CreatureTrainer.Name);
        Assert.Equal(trainer, binding.Before!.U("TrainerID"));
        var sql = SqlExport.Build("tc", [], edits, DateTime.UnixEpoch, schema: TcSchema.Instance);
        Assert.Contains("DELETE FROM `creature_trainer`", sql.Apply);
        // Every guard of the delete matches exactly one real row: the draft was built from the live rows.
        Assert.All(await Preflight(connection.World, sql.Apply), s => Assert.Equal("ok", s.State));
    }

    // ---- Gossip: a new broadcast text in the hotfixes database ------------------------------------------------

    [TcDbFact]
    public async Task ANewBroadcastTextIsNumberedAboveTheLiveRowsAndItsPreflightPassesWhileACollisionIsStale()
    {
        var connection = Environments.TrinityConnection()!;
        var hotfixes = new TcHotfixSource(connection.HotfixesSettings);
        var (maxBroadcast, maxHotfix) = await hotfixes.ReadMaxIdsAsync(CancellationToken.None);
        Assert.True(maxBroadcast > 100_000 && maxHotfix > 100_000);
        SqlExport.HotfixesDatabase = connection.Hotfixes!;
        var fresh = TcBroadcastText.Create(maxBroadcast + 1, "A greeting written in the editor.", maxHotfix + 1);
        var sql = SqlExport.Build("tc", [], fresh, DateTime.UnixEpoch, schema: TcSchema.Instance);
        Assert.Contains($"INSERT INTO `{connection.Hotfixes}`.`broadcast_text`", sql.Apply);
        Assert.All(await Preflight(connection.World, sql.Apply), s => Assert.Equal("ok", s.State));

        // The table hash in hotfix_data is the one the live server's own BroadcastText rows use.
        var hash = Assert.Single(await Rows(connection.HotfixesSettings!, $"SELECT COUNT(*) FROM hotfix_data WHERE TableHash={TcBroadcastText.TableHash} AND Status IN (1,3)"));
        Assert.True(Convert.ToInt64(hash[0], CultureInfo.InvariantCulture) > 100);

        // Negative control: a hotfix_data row that already exists makes the preflight report STALE.
        var taken = Assert.Single(await Rows(connection.HotfixesSettings!, $"SELECT Id, RecordId FROM hotfix_data WHERE TableHash={TcBroadcastText.TableHash} ORDER BY Id LIMIT 1"));
        var clash = TcBroadcastText.Create(U(taken[1]), "Collides with a real hotfix.", Convert.ToInt32(taken[0], CultureInfo.InvariantCulture));
        var states = await Preflight(connection.World, SqlExport.Build("tc", [], clash, DateTime.UnixEpoch, schema: TcSchema.Instance).Apply);
        Assert.Contains(states, s => s.State == "STALE" && s.Change.StartsWith("hotfix_data", StringComparison.Ordinal));
    }

    [TcDbFact]
    public async Task TheSpawnRowOfARealCreatureLoadsWithItsCachedArea()
    {
        var connection = Environments.TrinityConnection()!;
        var found = Assert.Single(await Rows(connection.World, "SELECT c.guid, c.id, c.map, c.zoneId, c.areaId FROM creature c WHERE c.zoneId<>0 AND c.areaId<>0 AND c.map=0 ORDER BY c.guid LIMIT 1"));
        ulong guid = Convert.ToUInt64(found[0], CultureInfo.InvariantCulture);
        var snapshot = await DBSource.Open(connection).ReadInteractionAsync(U(found[1]), guid, [], rows => rows, null, CancellationToken.None);
        var row = snapshot.Rows.Get(TcTables.SpawnCreature, guid.ToString(CultureInfo.InvariantCulture));
        Assert.NotNull(row);
        Assert.Equal((U(found[2]), U(found[3]), U(found[4])), (row.U("map"), row.U("zoneId"), row.U("areaId")));
    }

    // ---- Stats calculator on real database rows and the retail client -----------------------------------------

    [RetailClientFact]
    public async Task ARealCreatureWithoutATuningTakesItsAreasAndTheRealSeasonRulesMatchAnIndependentCalculation()
    {
        var connection = Assert.IsType<WorldConnection>(Environments.TrinityConnection());
        var lookup = new RetailClientLookup(RetailData.Tables);
        var found = Assert.Single(await Rows(connection.World, "SELECT d.Entry, c.guid, c.map, c.zoneId, c.areaId FROM creature_template_difficulty d JOIN creature c ON c.id=d.Entry JOIN creature_template t ON t.entry=d.Entry WHERE d.DifficultyID=0 AND d.ContentTuningID=0 AND t.type<>8 AND c.map=0 AND c.areaId<>0 ORDER BY d.Entry LIMIT 1"));
        uint entry = U(found[0]);
        ulong guid = Convert.ToUInt64(found[1], CultureInfo.InvariantCulture);
        var source = DBSource.Open(connection, lookup);
        var snapshot = await source.ReadInteractionAsync(entry, guid, [], rows => rows, null, CancellationToken.None);
        var template = snapshot.Rows.Get(TcTables.CreatureTemplate, entry.ToString(CultureInfo.InvariantCulture))!;
        var difficulty = snapshot.Rows.All(TcTables.CreatureDifficulty).Single(r => r.U("Entry") == entry && r.I("DifficultyID") == 0);
        var spawn = snapshot.Rows.Get(TcTables.SpawnCreature, guid.ToString(CultureInfo.InvariantCulture))!;
        var classStats = snapshot.Lookup.TcClassStats;
        var stats = TcStatCalculator.Compute(difficulty, TcStatTemplate.From(template), lookup, (level, unitClass) => classStats.GetValueOrDefault((level, unitClass)),
            new TcStatSite(spawn.U("map"), 0, spawn.U("areaId") != 0 ? spawn.U("areaId") : spawn.U("zoneId")));
        uint expected = lookup.MapDifficultyTuning(spawn.U("map"), 0) is not 0 and var map ? map : lookup.AreaTuning(spawn.U("areaId"));
        Assert.NotEqual(0u, expected);
        Assert.Equal(expected, stats.ContentTuningId);
        Assert.NotNull(stats.High);
        Assert.InRange(stats.High!.Level, 1, TcStatCalculator.StrongMaxLevel);
        Assert.True(stats.High.Health > 0 && stats.High.Armor > 0);
        Assert.True(stats.High.Melee.Max >= stats.High.Melee.Min);

        // Season rules: for every real tuning with a season-limited modifier, the health the calculator gives equals
        // expected stat x (the modifiers the server's reducer keeps) x class modifier x HealthModifier, computed here
        // straight from the client tables with milestone season 0.
        var tuningsWithSeasons = RetailData.Tables.Table("ContentTuningXExpected").Values
            .Where(r => r.FieldAs<int>("MinMythicPlusSeasonID") != 0 || r.FieldAs<int>("MaxMythicPlusSeasonID") != 0)
            .Select(r => r.FieldAs<uint>("ContentTuningID")).Distinct().Where(t => lookup.ContentTuning(t) != null).Take(5).ToList();
        Assert.NotEmpty(tuningsWithSeasons);
        foreach (uint tuning in tuningsWithSeasons)
        {
            var row = difficulty.With("ContentTuningID", (int)tuning).With("DeltaLevelMin", 0).With("HealthModifier", 1f);
            var computed = TcStatCalculator.Compute(row, new TcStatTemplate(1), lookup, (_, _) => null);
            if (computed.High == null)
            {
                continue;
            }

            var stat = lookup.ExpectedStat((uint)computed.MaxLevel, TcStatCalculator.ScalingExpansion(row.I("HealthScalingExpansion"))) ?? lookup.ExpectedStat((uint)computed.MaxLevel, -2);
            double health = stat?.CreatureHealth ?? 1;
            if (stat != null)
            {
                foreach (var mod in RetailData.Tables.Table("ContentTuningXExpected").Values.Where(r => r.FieldAs<uint>("ContentTuningID") == tuning))
                {
                    int min = mod.FieldAs<int>("MinMythicPlusSeasonID"), max = mod.FieldAs<int>("MaxMythicPlusSeasonID");
                    if (lookup.ExpectedStatMod(mod.FieldAs<uint>("ExpectedStatModID")) is not { } statMod
                        || (min != 0 && lookup.MilestoneSeason(min) is { } start && 0 < start)
                        || (max != 0 && lookup.MilestoneSeason(max) is { } end && 0 >= end))
                    {
                        continue;
                    }

                    health *= statMod.CreatureHealthMod;
                }

                health *= lookup.ExpectedStatMod(TcStatCalculator.ClassModRow(1)!.Value)!.CreatureHealthMod;
            }

            Assert.Equal((long)Math.Ceiling(health), computed.High.Health);
        }
    }

    // ---- Gossip: copying a menu carries its conditions and re-points the creature's SmartAI ---------------------

    static async Task CopyingARealMenuCarriesItsConditionsAndSmartRules(DatabaseSettings settings, IDBSource source, TableSchema conditions, TableSchema smart, string schemaName)
    {
        // Anachronos (15192) opens menu 6539, which has many conditions, and listens to it with SmartAI.
        const uint entry = 15192, menu = 6539, copy = 9_900_001;
        var snapshot = await source.ReadInteractionAsync(entry, 0, [], rows => rows, null, CancellationToken.None);
        long conditionsInDb = Convert.ToInt64(Assert.Single(await Rows(settings, $"SELECT COUNT(*) FROM conditions WHERE SourceTypeOrReferenceId IN (14,15) AND SourceGroup={menu}"))[0], CultureInfo.InvariantCulture);
        long smartInDb = Convert.ToInt64(Assert.Single(await Rows(settings, $"SELECT COUNT(*) FROM smart_scripts WHERE entryorguid={entry} AND source_type=0 AND event_type=62 AND event_param1={menu}"))[0], CultureInfo.InvariantCulture);
        Assert.True(conditionsInDb > 20 && smartInDb > 5, "the sample creature still has conditions and gossip-select rules");

        var copiedConditions = GossipMenuCopy.Conditions(conditions, snapshot.Rows, menu, copy);
        var repointed = GossipMenuCopy.RepointSelectEvents(smart, snapshot.Rows, entry, menu, copy);
        Assert.Equal(conditionsInDb, copiedConditions.Length);
        Assert.Equal(smartInDb, repointed.Length);
        Assert.All(copiedConditions, e => Assert.Equal(copy, e.After!.U("SourceGroup")));
        Assert.All(repointed, e => Assert.Equal((menu, copy), (e.Before!.U("event_param1"), e.After!.U("event_param1"))));

        // Run the exported guards against the live rows they were read from: nothing exists at the copy's IDs, and
        // every re-pointed rule still matches exactly one row (real rows have NULLs the loader reads as 0).
        var script = SqlExport.Build("db", [], [.. copiedConditions, .. repointed], DateTime.UnixEpoch, schema: source.Schema);
        Assert.Contains("INSERT INTO `conditions`", script.Apply);
        Assert.Contains("UPDATE `smart_scripts` SET `event_param1` = " + copy, script.Apply);
        var states = await Preflight(settings, script.Apply);
        Assert.Equal(copiedConditions.Length + repointed.Length, states.Count);
        Assert.All(states, s => Assert.True(s.State == "ok", $"{schemaName}: {s.Change} reads {s.State}"));
    }

    [DbFact]
    public async Task AzerothCoreCopyingARealMenuCarriesItsConditionsAndSmartRules() =>
        await CopyingARealMenuCarriesItsConditionsAndSmartRules(Test.Config, Test.AcSource(Test.Config), AcTables.Conditions, AcTables.SmartScripts, "AzerothCore");

    [TcDbFact]
    public async Task TrinityCoreCopyingARealMenuCarriesItsConditionsAndSmartRules()
    {
        var connection = Environments.TrinityConnection()!;
        await CopyingARealMenuCarriesItsConditionsAndSmartRules(connection.World, DBSource.Open(connection), TcTables.Conditions, TcTables.SmartScripts, "TrinityCore");
    }

    // ---- Gossip: OverrideIconID is checked against the retail client's files -----------------------------------

    [RetailClientFact]
    public void AnOptionIconFileIsCheckedAgainstTheRealClientStorage()
    {
        // 134400 is interface/icons/inv_misc_questionmark.blp in the community listfile; no TDB option sets OverrideIconID yet.
        var lookup = new RetailClientLookup(RetailData.Tables, id => RetailData.Files.Exists("fdid:" + id.ToString(CultureInfo.InvariantCulture)));
        Assert.True(lookup.Exists(ClientRecord.FileData, 134400));
        Assert.False(lookup.Exists(ClientRecord.FileData, 2_000_000_000));

        // Taken from a real option of the live database, with only the icon changed.
        var real = TcSchema.Instance.GossipPage.Options.Table;
        var row = Row.Of(real.Columns.Select(c => (c.Name, (object)(c.Kind == ColumnKind.Text ? "" : 0))).ToArray()).With("MenuID", 1u).With("OptionNpc", 1u).With("OptionText", "Buy");
        var rows = new RowSet();
        rows.Set(real, row.With("OverrideIconID", 134400));
        Assert.DoesNotContain(TcGossipRules.Check(rows, lookup.Exists, _ => true), i => i.Message.Contains("icon file"));
        rows.Set(real, row.With("OverrideIconID", 2_000_000_000));
        Assert.Contains(TcGossipRules.Check(rows, lookup.Exists, _ => true), i => i.Error && i.Message.Contains("icon file 2000000000"));
    }
}
