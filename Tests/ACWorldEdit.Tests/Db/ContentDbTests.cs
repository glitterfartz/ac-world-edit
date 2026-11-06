using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only quest and SmartAI snapshot checks against real rows: a known Elwynn quest, a
/// Goldshire SmartAI creature and a creature whose script runs timed action lists.
/// </summary>
[Trait("Category", "Db")]
public class ContentDbTests
{
    [DbFact(NeedsClientData = true)]
    public async Task Passes() => await Core(ClientData.Store);

    [DbFact]
    public async Task ItemSearchReturnsTemplatesForTheQuestPicker()
    {
        var items = await AcSource(Config).SearchItemDetailsAsync("Linen Cloth", CancellationToken.None);
        Check(items.Any(item => item.Info.Id == 2589 && item.Info.Name == "Linen Cloth"),
            "Item search did not return Linen Cloth: " + string.Join(", ", items.Take(5).Select(i => $"{i.Info.Id} {i.Info.Name}")));
    }

    static async Task Core(AssetStore store)
    {
        var catalog = InteractionCatalog.Load(store);
        Check(catalog.SpellIconPath(133) is { Length: > 0 }, "Spell icon");
        Check(catalog.SearchSkills("", 20).Count > 0, "Skill names");
        Check(catalog.SearchMails("", 20).Count > 0, "Mail subjects");
        Check(catalog.SearchTitles("", 20).Count > 0, "Title names");
        Check(catalog.QuestInfos().Count > 0 && catalog.QuestSorts().Count > 0, "Quest info/sort names");
        Check(catalog.SearchFactionTemplates("Stormwind", 60).Count > 0, "FactionTemplate names");
        Check(catalog.Classes().Count > 0, "Class names");
        Check(catalog.CreatureTypes().Count > 0, "Creature type names");
        Check(catalog.CreatureFamilies().Count > 0, "Creature family names");
        var config = Config;
        var source = AcSource(config);
        var quests = await source.SearchAsync(SmartParamKind.Quest, "Kobold Camp Cleanup", CancellationToken.None);
        Check(quests.Count > 0, "Quest search found nothing");
        uint questId = quests[0].Id;
        var quest = await source.ReadQuestAsync(questId, [], rows => rows, catalog, CancellationToken.None);
        var q = quest.Rows.Get(AcTables.QuestTemplate, questId.ToString())!;
        Check(q.T("LogTitle") == "Kobold Camp Cleanup", "Quest title");
        Check(q.I("RequiredNpcOrGo1") > 0 && quest.Names.Creature((uint)q.I("RequiredNpcOrGo1")) is { Length: > 0 }, "Objective creature name");
        Check(quest.Rows.All(AcTables.QuestStarter).Any() && quest.Names.Creature(quest.Rows.All(AcTables.QuestStarter).First().U("id")) != null, "Quest giver name");
        var questIssues = AcQuestModel.Check(questId, quest.Rows, quest.Names, null).ToArray();
        Console.WriteLine($"QUEST_SNAPSHOT {questId} '{q.T("LogTitle")}' objective={quest.Names.Creature((uint)q.I("RequiredNpcOrGo1"))}×{q.U("RequiredNpcOrGoCount1")} giver={quest.Names.Creature(quest.Rows.All(AcTables.QuestStarter).First().U("id"))} errors={questIssues.Count(i => i.Error)} warnings={string.Join("; ", questIssues.Where(i => !i.Error).Select(i => i.Message))}");
        Check(questIssues.All(i => !i.Error), "Real quest reported errors: " + string.Join("; ", questIssues.Where(i => i.Error).Select(i => i.Message)));
        var fresh = AcQuestModel.NewQuest(quest.MaxQuestId + 1, "Smoke quest");
        foreach (var row in fresh)
        {
            row.Validate(AcTables.Tables.First(t => t.Columns.Length == row.Values.Count && t.Columns.All(c => row.Values.ContainsKey(c.Name))));
        }

        var database = new WorldDatabase(config);
        uint goldshire = 0, goldshireGuid = 0, withList = 0;
        await using (var session = await database.OpenAsync(CancellationToken.None))
        {
            string id = session.Has("creature.id1") ? "c.id1" : "c.id";
            await using (var command = session.ReadOnly($"""
                SELECT {id},c.guid FROM creature c JOIN creature_template t ON t.entry={id}
                WHERE t.AIName='SmartAI' AND c.map=0 AND c.position_x BETWEEN -9700 AND -9200 AND c.position_y BETWEEN -300 AND 300
                  AND EXISTS(SELECT 1 FROM smart_scripts s WHERE s.entryorguid={id} AND s.source_type=0)
                ORDER BY POW(c.position_x+9460,2)+POW(c.position_y-30,2) LIMIT 1
                """))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Check(await reader.ReadAsync(), "No Goldshire SmartAI creature");
                goldshire = reader.GetUInt32(0);
                goldshireGuid = reader.GetUInt32(1);
            }

            await using (var command = session.ReadOnly("SELECT s.entryorguid FROM smart_scripts s JOIN creature_template t ON t.entry=s.entryorguid WHERE s.source_type=0 AND s.action_type=80 AND t.AIName='SmartAI' ORDER BY s.entryorguid LIMIT 1"))
            {
                withList = Convert.ToUInt32(await command.ExecuteScalarAsync());
            }
        }

        foreach (uint entry in new[] { goldshire, withList })
        {
            var snapshot = await source.ReadBehaviorAsync(entry, entry == goldshire ? goldshireGuid : 0, [], rows => rows, catalog, CancellationToken.None);
            var rules = snapshot.Rows.All(AcTables.SmartScripts).Select(r => new SmartRule(r)).ToList();
            var scopes = rules.GroupBy(r => r.Scope).ToList();
            Console.WriteLine($"BEHAVIOR_SNAPSHOT entry={entry} '{snapshot.CreatureName}' ai={snapshot.Rows.All(AcTables.CreatureAI).First().T("AIName")} rules={rules.Count} scopes={string.Join(",", scopes.Select(s => s.Key.Describe(snapshot.CreatureName)))}");
            foreach (var scope in scopes)
            {
                var issues = SmartCatalog.Ac.Check(scope.Key, scope.ToList(), snapshot.Names, snapshot.Rows.All(AcTables.CreatureAI).First(), entry).ToArray();
                foreach (var chain in SmartCatalog.Chains(scope).Take(6))
                {
                    Console.WriteLine("  " + string.Join("\n    ", chain.Select(r => SmartCatalog.Ac.Describe(r, snapshot.Names, entry))));
                }

                Console.WriteLine($"  {scope.Key.Describe(snapshot.CreatureName)}: errors={issues.Count(i => i.Error)} {string.Join("; ", issues.Take(3).Select(i => i.Message))}");
            }

            Check(rules.Count > 0, "No rules loaded");
            var lines = snapshot.Rows.All(AcTables.CreatureText).ToList();
            foreach (var line in lines)
            {
                line.Validate(AcTables.CreatureText);
            }

            var used = await source.UsedActionListsAsync((int)entry * 100, (int)entry * 100 + 99, CancellationToken.None);
            Console.WriteLine($"  speech lines={lines.Count} groups={lines.Select(l => l.U("GroupID")).Distinct().Count()} script='{snapshot.ScriptName}' action lists in {entry * 100}..{entry * 100 + 99}: {string.Join(",", used.Order())}");
            if (entry == withList)
            {
                Check(rules.Where(r => r.Scope.IsActionList).Select(r => r.EntryOrGuid).Distinct().All(id => id < entry * 100 || id > entry * 100 + 99 || used.Contains(id)), "Used action list lookup missed a loaded list");
            }
            if (entry == withList)
            {
                Check(scopes.Any(s => s.Key.IsActionList), "Action list not followed");
            }
        }

        Console.WriteLine("CONTENT_CHECKS_PASSED quest snapshot/names/rules, behavior snapshot/action lists/sentences/speech/list IDs");
    }

    [DbFact]
    public async Task MenuSearchFindsMenusByIdAndByGreetingText()
    {
        var source = AcSource(Config);
        var byText = await source.SearchMenusAsync("Greetings", CancellationToken.None);
        Assert.NotEmpty(byText);
        Assert.True(byText.Count <= DbLimits.MaxSearch);
        var byId = await source.SearchMenusAsync(byText[0].Id.ToString(), CancellationToken.None);
        Assert.Equal(byText[0].Id, byId[0].Id);
        Assert.NotEmpty(byId[0].Name);
    }

    [DbFact]
    public async Task BroadcastSearchFindsByIdAndText()
    {
        var source = AcSource(Config);
        var any = await source.SearchBroadcastAsync("the", CancellationToken.None);
        Assert.NotEmpty(any);
        var byId = await source.SearchBroadcastAsync(any[0].Id.ToString(), CancellationToken.None);
        Assert.Equal(any[0].Id, byId[0].Id);
        Assert.True(any.Count <= DbLimits.MaxSearch);
    }
}
