using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --content-smoke (graphical) on an isolated content draft. Behavior editor: select
/// Innkeeper Farley in Goldshire with a short right-click, open Behavior from the toolbar path, add
/// a rule (enabling SmartAI when the creature uses the default AI), give it a new speech line, add a
/// linked "Then" step, reorder rules, create and open an action list, undo/redo. Quest editor: create
/// a quest, pick an objective through the real search picker, edit story/rewards (an invalid reward
/// must be rejected), add givers, view every page, undo, then reopen the draft file. Saves
/// artifacts/content-*.png.
/// </summary>
public partial class TerrainViewer
{
    bool contentSmoke;
    int contentStep, contentWait;
    double contentTime, contentStepTime;
    List<(string Name, Func<bool> Step)>? contentSteps;
    uint smokeTalkRule, smokeGroup, smokeNewQuest;
    int smokeRuleCount;
    string smokeAiBefore = "", smokeLocateStatus = "";
    const uint SmokeThrall = 10176;

    void ContentSmoke(double delta)
    {
        shotPrefix = "content";
        contentTime += delta;
        contentStepTime += delta;
        contentSteps ??= ContentSteps();
        TakeScreenshot();
        if (shot != null)
        {
            return;
        }

        string Where() => $"behavior='{behaviorEditor.SmokeStatus}' quest='{questEditor.SmokeStatus}' questId={questEditor.QuestId}";
        if (contentStepTime > 60 || contentTime > 360)
        {
            SmokeFail($"CONTENT_SMOKE_TIMEOUT step={contentSteps[Math.Min(contentStep, contentSteps.Count - 1)].Name} {Where()}");
            return;
        }

        if (contentWait > 0)
        {
            contentWait--;
            return;
        }

        if (busy || world == null)
        {
            return;
        }

        if (contentStep >= contentSteps.Count)
        {
            GD.Print("CONTENT_SMOKE_PASSED");
            GetTree().Quit();
            return;
        }

        var (name, step) = contentSteps[contentStep];
        if ((int)(contentStepTime / 5) != (int)((contentStepTime - delta) / 5))
        {
            GD.Print($"CONTENT_SMOKE_WAITING {name} {contentStepTime:F0}s {Where()}");
        }

        try
        {
            if (step())
            {
                GD.Print("CONTENT_SMOKE_STEP " + name);
                contentStep++;
                contentStepTime = 0;
                contentWait = 3;
            }
        }
        catch (Exception error)
        {
            SmokeFail($"CONTENT_SMOKE_FAILED {name}: {error.Message} {Where()}");
        }
    }

    SmartScope FarleyScope => new((int)SmokeInnkeeper, SmartScope.Creature);

    SmartRule RuleById(uint id) => behaviorEditor.SmokeRules(FarleyScope).First(r => r.Id == id);

    List<(string, Func<bool>)> ContentSteps() =>
    [
        ("goldshire", () =>
        {
            if (currentMapId != 0)
            {
                return false;
            }

            tileX.Value = 31;
            tileY.Value = 49;
            Teleport(new TileKey(31, 49));
            FocusPose(new WorldPose(0, -9460, 30, 58, 0));
            return true;
        }),
        ("farley-loaded", () =>
        {
            if (loaded.Count != wanted.Count || creatures.IsLoading || Farley is not { } spawn)
            {
                return false;
            }

            FocusPose(spawn.Pose);
            contentWait = 30;
            return true;
        }),
        ("select-farley", () =>
        {
            RightClick(camera.UnprojectPosition(creatures.PositionOf(Farley!.Guid) + Vector3.Up), Vector2.Zero);
            return true;
        }),
        ("open-behavior", () =>
        {
            Require(creatures.SelectedGuid == Farley!.Guid, "short right-click did not select Farley");
            interaction.Visible = false;
            OpenBehaviorEditor();
            return true;
        }),
        ("behavior-loaded", () =>
        {
            if (behaviorEditor.Entry != SmokeInnkeeper || behaviorEditor.IsLoading || behaviorEditor.Scope == null)
            {
                return false;
            }

            smokeAiBefore = behaviorEditor.SmokeAiName;
            smokeRuleCount = behaviorEditor.SmokeRules(FarleyScope).Count;
            GD.Print($"CONTENT_SMOKE_BEHAVIOR ai='{smokeAiBefore}' script='{behaviorEditor.SmokeScriptName}' rules={smokeRuleCount} lines={behaviorEditor.SmokeLineCount} issues={behaviorEditor.Issues.Count}");
            Screenshot("behavior-start");
            return true;
        }),
        ("add-rule", () =>
        {
            behaviorEditor.SmokeAddRule(0);
            var rules = behaviorEditor.SmokeRules(FarleyScope);
            Require(rules.Count == smokeRuleCount + 1, "rule not added: " + behaviorEditor.SmokeStatus);
            smokeTalkRule = rules.Max(r => r.Id);
            Require(RuleById(smokeTalkRule).Event == 4 && RuleById(smokeTalkRule).Action == SmartCatalog.Talk, "template not applied");
            if (smokeAiBefore.Length == 0 && behaviorEditor.SmokeScriptName.Length == 0)
            {
                Require(behaviorEditor.SmokeAiName == "SmartAI", "adding the first rule did not enable SmartAI");
            }

            return true;
        }),
        ("speech", () =>
        {
            behaviorEditor.ShowScope(null);
            int before = behaviorEditor.SmokeLineCount;
            smokeGroup = behaviorEditor.SmokeEffective.All(InteractionTables.CreatureText).Where(t => t.U("CreatureID") == SmokeInnkeeper).Select(t => t.U("GroupID") + 1).DefaultIfEmpty(0u).Max();
            behaviorEditor.SmokeAddLine(smokeGroup, "Welcome to the Lion's Pride, $n!");
            Require(behaviorEditor.SmokeLineCount == before + 1, "speech line not added: " + behaviorEditor.SmokeStatus);
            Require(behaviorEditor.SmokeUpdate(RuleById(smokeTalkRule), "Change text group", ("action_param1", smokeGroup)), "talk group not set: " + behaviorEditor.SmokeStatus);
            Screenshot("behavior-speech");
            return true;
        }),
        ("talk-sentence", () =>
        {
            string sentence = behaviorEditor.SmokeDescribe(RuleById(smokeTalkRule));
            Require(sentence.StartsWith("When it enters combat", StringComparison.Ordinal) && sentence.Contains("Welcome to the Lion", StringComparison.Ordinal), "talk sentence: " + sentence);
            behaviorEditor.ShowScope(FarleyScope);
            behaviorEditor.SmokeExpand(smokeTalkRule);
            Screenshot("behavior-rule-edit");
            return true;
        }),
        ("then-step", () =>
        {
            behaviorEditor.SmokeAddThen(RuleById(smokeTalkRule));
            uint then = RuleById(smokeTalkRule).Link;
            Require(then != 0 && RuleById(then).Event == SmartCatalog.LinkEvent, "Then step not linked: " + behaviorEditor.SmokeStatus);
            behaviorEditor.SmokeChangeType(RuleById(then), "action", 11);
            Require(behaviorEditor.SmokeUpdate(RuleById(then), "Change spell", ("action_param1", 133u)), "spell not set: " + behaviorEditor.SmokeStatus);
            string sentence = behaviorEditor.SmokeDescribe(RuleById(then));
            Require(sentence.StartsWith("Then:", StringComparison.Ordinal) && sentence.Contains("Fireball", StringComparison.Ordinal), "then sentence: " + sentence);
            return true;
        }),
        ("reorder", () =>
        {
            behaviorEditor.SmokeAddRule(1);
            var roots = SmartCatalog.Chains(behaviorEditor.SmokeRules(FarleyScope)).Select(c => c[0]).ToList();
            var talk = roots.First(r => r.Id == smokeTalkRule);
            var timer = roots[^1];
            Require(timer.Event == 0 && timer.Id > talk.Id, "timer rule not added last");
            behaviorEditor.SmokeSwap(talk, timer);
            var chains = SmartCatalog.Chains(behaviorEditor.SmokeRules(FarleyScope));
            var moved = chains.First(c => c[0].Event == 4 && c[0].Action == SmartCatalog.Talk && c[0].ActionParam(1) == smokeGroup);
            Require(moved[0].Id == timer.Id && moved.Count == 2 && moved[1].Action == 11, "swap lost the Then chain");
            smokeTalkRule = moved[0].Id;
            behaviorEditor.SmokeExpand(null);
            Screenshot("behavior-rules");
            return true;
        }),
        ("composer-open", () =>
        {
            Require(behaviorEditor.SmokePress("AddRule"), "no + Add rule button");
            return true;
        }),
        ("composer-choose", () =>
        {
            int events = SmartCatalog.Events.Count(t => !SmartCatalog.IsMarker(t) && t.Id != SmartCatalog.LinkEvent);
            int actions = SmartCatalog.Actions.Count(t => !SmartCatalog.IsMarker(t));
            int targets = SmartCatalog.Targets.Count(t => !SmartCatalog.IsMarker(t));
            Require(behaviorEditor.SmokeComposerOptions("event") == events && behaviorEditor.SmokeComposerOptions("action") == actions && behaviorEditor.SmokeComposerOptions("target") == targets,
                $"composer lists {behaviorEditor.SmokeComposerOptions("event")}/{behaviorEditor.SmokeComposerOptions("action")}/{behaviorEditor.SmokeComposerOptions("target")}, expected {events}/{actions}/{targets}");
            smokeRuleCount = behaviorEditor.SmokeRules(FarleyScope).Count;
            Require(behaviorEditor.SmokeComposerChoose("event", 8) && behaviorEditor.SmokeComposerChoose("action", 12), "composer selection failed");
            return true;
        }),
        ("composer-preview", () =>
        {
            Require(behaviorEditor.SmokeComposerChoose("target", 1), "composer target selection failed");
            string preview = behaviorEditor.SmokeComposerPreview;
            Require(preview.Contains("When hit by", StringComparison.Ordinal) && preview.Contains("summon", StringComparison.Ordinal), "composer preview: " + preview);
            Require(behaviorEditor.SmokeRules(FarleyScope).Count == smokeRuleCount, "composer drafted a rule before Add");
            GD.Print($"CONTENT_SMOKE_COMPOSER {preview}");
            Screenshot("behavior-composer");
            return true;
        }),
        ("composer-add", () =>
        {
            Require(behaviorEditor.SmokePress("ComposerAdd"), "no composer Add button");
            var added = behaviorEditor.SmokeRules(FarleyScope).MaxBy(r => r.Id)!;
            Require(behaviorEditor.SmokeRules(FarleyScope).Count == smokeRuleCount + 1 && added.Event == 8 && added.Action == 12 && added.Target == 1, "composed rule not added: " + behaviorEditor.SmokeStatus);
            Screenshot("behavior-composed");
            return true;
        }),
        ("action-list", () =>
        {
            behaviorEditor.SmokeAddRule(5);
            var rule = behaviorEditor.SmokeRules(FarleyScope).MaxBy(r => r.Id)!;
            Require(rule.Action == SmartCatalog.RunActionList, "quest scene template");
            _ = behaviorEditor.SmokeCreateActionList(rule);
            return true;
        }),
        ("action-list-open", () =>
        {
            if (behaviorEditor.Scope is not { IsActionList: true } list)
            {
                return false;
            }

            Require(behaviorEditor.SmokeRules(list).Count == 1, "new action list has no first step");
            Require(behaviorEditor.SmokeRules(FarleyScope).Any(r => r.Action == SmartCatalog.RunActionList && r.ActionParam(1) == (uint)list.EntryOrGuid), "rule not pointed at the new list");
            Screenshot("behavior-action-list");
            return true;
        }),
        ("undo", () =>
        {
            behaviorEditor.SmokeHistory(false);
            return true;
        }),
        ("undo-checked", () =>
        {
            if (behaviorEditor.IsLoading)
            {
                return false;
            }

            var list = behaviorEditor.Scope!.Value;
            Require(behaviorEditor.SmokeRules(list).Count == 0, "undo kept the action list step");
            behaviorEditor.SmokeHistory(true);
            return true;
        }),
        ("redo-checked", () =>
        {
            if (behaviorEditor.IsLoading)
            {
                return false;
            }

            Require(behaviorEditor.SmokeRules(behaviorEditor.Scope!.Value).Count == 1, "redo lost the action list step");
            behaviorEditor.Visible = false;
            questEditor.Open(0);
            _ = questEditor.NewQuest(null);
            return true;
        }),
        ("quest-created", () =>
        {
            if (questEditor.IsLoading || questEditor.SmokeQuest is not { } quest)
            {
                return false;
            }

            Require(quest.T("LogTitle") == "New quest", "new quest title");
            smokeNewQuest = questEditor.QuestId!.Value;
            Require(questEditor.SmokeSet(InteractionTables.QuestTemplate, "Rename quest", ("LogTitle", "A Warm Welcome")), "rename: " + questEditor.SmokeStatus);
            Screenshot("quest-overview");
            return true;
        }),
        ("quest-objective-picker", () =>
        {
            questEditor.ShowTab(QuestEditorWindow.Tab.Objectives);
            Require(questEditor.SmokePress("Kill a creature…"), "no Kill a creature button");
            return true;
        }),
        ("quest-picker-search", () =>
        {
            if (questEditor.SmokePicker is not { } picker)
            {
                return false;
            }

            picker.SmokeSearch("Kobold Vermin");
            return true;
        }),
        ("quest-picker-choose", () =>
        {
            if (questEditor.SmokePicker is not { ResultCount: > 0, ShownTerm: "Kobold Vermin" } picker)
            {
                return false;
            }

            picker.Choose(0);
            return true;
        }),
        ("quest-objective-set", () =>
        {
            var quest = questEditor.SmokeQuest!;
            Require(quest.I("RequiredNpcOrGo1") > 0 && quest.U("RequiredNpcOrGoCount1") == 1, "picker did not set the objective: " + questEditor.SmokeStatus);
            Require(questEditor.SmokeObjectiveName(1) == "Kobold Vermin", "picker chose the wrong creature: " + questEditor.SmokeObjectiveName(1));
            Require(questEditor.SmokeSet(InteractionTables.QuestTemplate, "Change objective count", ("RequiredNpcOrGoCount1", 8u)), "count: " + questEditor.SmokeStatus);
            Screenshot("quest-objectives");
            return true;
        }),
        ("quest-story", () =>
        {
            questEditor.ShowTab(QuestEditorWindow.Tab.Story);
            Require(questEditor.SmokeSet(InteractionTables.QuestTemplate, "Edit quest text", ("QuestDescription", "Those kobolds keep scaring my guests, $n.$B$BThin them out, would you?"), ("LogDescription", "Kill 8 Kobold Vermin.")), "story: " + questEditor.SmokeStatus);
            Require(questEditor.SmokeSet(InteractionTables.QuestOfferReward, "Edit turn-in text", ("RewardText", "My guests can finally sleep. Thank you, $n.")), "turn-in: " + questEditor.SmokeStatus);
            Screenshot("quest-story");
            return true;
        }),
        ("quest-rewards", () =>
        {
            questEditor.ShowTab(QuestEditorWindow.Tab.Rewards);
            Require(questEditor.SmokeSet(InteractionTables.QuestTemplate, "Change money reward", ("RewardMoney", 1500)), "money: " + questEditor.SmokeStatus);
            Require(!questEditor.SmokeSet(InteractionTables.QuestTemplate, "Change reward item", ("RewardItem1", 99999999u), ("RewardAmount1", 1u)) && questEditor.SmokeStatus.StartsWith("Not saved", StringComparison.Ordinal), "missing reward item was accepted");
            Screenshot("quest-rewards");
            return true;
        }),
        ("quest-givers", () =>
        {
            questEditor.ShowTab(QuestEditorWindow.Tab.Givers);
            var link = Row.Of(("id", SmokeInnkeeper), ("quest", smokeNewQuest));
            var table = InteractionTables.QuestStarter;
            Require(questEditor.SmokeCommit("Add quest giver", new RowEdit(table.Name, table.KeyOf(link), null, link)), "giver: " + questEditor.SmokeStatus);
            Require(questEditor.SmokePress("Givers also complete it"), "no 'Givers also complete it' shortcut");
            Require(questEditor.SmokeEffective.All(InteractionTables.QuestEnder).Any(r => r.U("quest") == smokeNewQuest && r.U("id") == SmokeInnkeeper), "ender not added");
            Require(!questEditor.SmokeIssues.Any(i => i.Error), "new quest has errors: " + string.Join("; ", questEditor.SmokeIssues.Select(i => i.Message)));
            Screenshot("quest-givers");
            return true;
        }),
        ("quest-chain", () =>
        {
            questEditor.ShowTab(QuestEditorWindow.Tab.Chain);
            Screenshot("quest-chain");
            return true;
        }),
        ("quest-preview", () =>
        {
            questEditor.ShowTab(QuestEditorWindow.Tab.Preview);
            Screenshot("quest-preview");
            return true;
        }),
        ("quest-undo", () =>
        {
            questEditor.SmokeHistory(false);
            return true;
        }),
        ("quest-undo-checked", () =>
        {
            if (questEditor.IsLoading)
            {
                return false;
            }

            Require(!questEditor.SmokeEffective.All(InteractionTables.QuestEnder).Any(r => r.U("quest") == smokeNewQuest), "undo kept the ender");
            drafts.Reopen();
            _ = questEditor.Load(smokeNewQuest);
            return true;
        }),
        ("quest-reopened", () =>
        {
            if (questEditor.IsLoading || questEditor.SmokeQuest is not { } quest)
            {
                return false;
            }

            Require(quest.T("LogTitle") == "A Warm Welcome" && quest.U("RequiredNpcOrGoCount1") == 8 && quest.I("RewardMoney") == 1500, "reopened draft lost quest edits");
            Require(drafts.Draft?.CanRedo == true, "reopened draft lost redo history");
            GD.Print($"CONTENT_SMOKE_QUEST {smokeNewQuest} '{quest.T("LogTitle")}' objective={quest.I("RequiredNpcOrGo1")}x{quest.U("RequiredNpcOrGoCount1")} issues={string.Join("; ", questEditor.SmokeIssues.Select(i => i.Message))}");
            // Locate a creature that is not loaded and lives on another map (Kaltunk, outdoors in the Valley of Trials).
            _ = LocateCreature(SmokeThrall, "Kaltunk").ContinueWith(t => smokeLocateStatus = t.Result, TaskScheduler.FromCurrentSynchronizationContext());
            return true;
        }),
        ("locate-other-map", () =>
        {
            if (busy || currentMapId != 1 || LocatePending || creatures.SelectedSpawn?.TemplateId != SmokeThrall)
            {
                return false;
            }

            return true;
        }),
        ("locate-stays-focused", () =>
        {
            // The camera's tile finishing must not lift it away from the located creature.
            if (busy || !TerrainSettled)
            {
                return false;
            }

            float distance = camera.Position.DistanceTo(creatures.PositionOf(creatures.SelectedGuid!.Value));
            GD.Print($"CONTENT_SMOKE_LOCATE '{smokeLocateStatus}' map={currentMapId} guid={creatures.SelectedGuid} distance={distance:F1}");
            Require(distance < 40, $"camera drifted {distance:F0} yd from the located creature after terrain loaded");
            // Reproduce the order seen in normal use: the creature is focused first and the
            // camera's tile finishes afterwards. Unload that tile so it streams back in now.
            var tile = CameraTile();
            if (loaded.Remove(tile, out var node))
            {
                renderer.Release(node);
                chunkAreas.Remove(tile);
                terrainHeights.Remove(tile);
            }

            return true;
        }),
        ("locate-tile-reload-keeps-focus", () =>
        {
            if (busy || !TerrainSettled)
            {
                return false;
            }

            float distance = camera.Position.DistanceTo(creatures.PositionOf(creatures.SelectedGuid!.Value));
            GD.Print($"CONTENT_SMOKE_LOCATE_RELOAD distance={distance:F1}");
            Require(distance < 40, $"a tile finishing under the focused camera lifted it {distance:F0} yd away");
            Screenshot("locate-giver");
            return true;
        })
    ];
}
