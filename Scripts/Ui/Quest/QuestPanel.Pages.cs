using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Quest editor pages shared by both databases (story, chain, givers and the field helpers). Each control reads the effective rows and commits one labelled command; the
/// page re-renders from the draft afterwards, so what is shown is always what is drafted. Column
/// names stay out of the UI: captions describe what the player experiences.
/// </summary>
public abstract partial class QuestPanel
{
    protected void Heading(string text, string help = "")
    {
        page.AddChild(ClientStyle.Text(text, 16, ClientStyle.Header));
        if (help.Length > 0)
        {
            var label = ClientStyle.Text(help, 11, ClientStyle.Faded);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            page.AddChild(label);
        }
    }

    protected void Add(string caption, params Control[] controls) => page.AddChild(EditorFields.Row(caption, controls));

    /// <summary>A labelled row whose caption and controls explain the field on hover.</summary>
    protected void Add(string caption, string tip, params Control[] controls) => page.AddChild(EditorFields.Row(caption, tip, controls));

    protected LineEdit UNumber(TableSchema table, string column, string label, int width = 80, string tooltip = "") =>
        EditorFields.Number(RowOf(table).L(column), value => Set(table, label, (column, value)), width, tooltip);

    protected string Named(SmartParamKind kind, uint id, string empty = "None")
    {
        if (id == 0)
        {
            return empty;
        }

        return snapshot!.Names.TryName(kind, id, out string name) ? $"{name}  ·  {id}" : $"Missing #{id}";
    }

    protected bool Missing(SmartParamKind kind, uint id) => id != 0 && !snapshot!.Names.TryName(kind, id, out _);

    /// <summary>
    /// A reference column edited through a picker, with a clear button.
    /// </summary>
    protected HBoxContainer Ref(TableSchema table, string column, SmartParamKind kind, string what, string label, bool signed = false, string tooltip = "")
    {
        var row = RowOf(table);
        uint id = signed ? (uint)Math.Abs(row.I(column)) : row.U(column);
        Action choose = kind == SmartParamKind.Spell
            ? () => PickSpell($"Choose {what}", picked => Set(table, label, (column, picked)))
            : () => Pick(kind, $"Choose {what}", picked => Set(table, label, (column, picked)));
        return EditorFields.Reference(Named(kind, id), choose, id == 0 ? null : () => Set(table, label, (column, 0)), Missing(kind, id), tooltip);
    }

    /// <summary>The quest log zone: an area name when the client's AreaTable is loaded, else the raw ID.</summary>
    protected void ZoneRow(Row q)
    {
        var areas = Areas();
        if (areas == null)
        {
            Add("Quest log zone", "The area the quest appears under in the quest log. A negative value is a quest-log category.",
                EditorFields.Number(q.I("QuestSortID"), value => Set(Template, "Change quest zone", ("QuestSortID", (int)value)), 80, "AreaTable ID; negative is a quest-log category"));
            return;
        }

        uint zone = q.I("QuestSortID") > 0 ? (uint)q.I("QuestSortID") : 0;
        bool zoneMissing = zone != 0 && !areas.Contains(zone);
        string zoneText = q.I("QuestSortID") switch
        {
            0 => "No zone",
            < 0 => $"Quest log category {-q.I("QuestSortID")}",
            _ => zoneMissing ? $"Missing zone #{zone}" : areas!.Describe(zone).Zone
        };
        Add("Quest log zone", "The area the quest appears under in the quest log, and the map a breadcrumb points at.",
            EditorFields.Reference(zoneText, PickZone, q.I("QuestSortID") == 0 ? null : () => Set(Template, "Clear quest zone", ("QuestSortID", 0)), zoneMissing, "The quest-log zone (AreaTable ID), or a negative quest-log category."));
    }

    protected void PickZone()
    {
        if (Areas() is not { } areas)
        {
            status = "Zone names need the client AreaTable; enter the zone ID in the search box instead.";
            UpdateStatus();
            return;
        }

        PickerPopup.Show(this, "Choose quest log zone", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(areas.Search(term, null, 60)
            .Select(a => (a.Id, a.Parent == null ? a.Name : $"{a.Name} ({a.Parent})")).ToList()),
            (id, _) => Set(Template, "Change quest zone", ("QuestSortID", (int)id)));
    }

    protected void RenderStory(Row q)
    {
        var request = RowOf(Request);
        var offer = RowOf(Offer);
        page.AddChild(EditorFields.Placeholders(QuestVocabulary.Placeholders));
        Heading("1 · When offered", "What the quest giver says before the player accepts.");
        page.AddChild(EditorFields.Area(q.T("QuestDescription"), text => Set(Template, "Edit quest text", ("QuestDescription", text)), 120, "The story text shown in the quest log and when the quest is offered. Use the placeholders below for the player's name, class and race."));
        Heading("Objective summary", "One-line goal shown under \"Quest Objectives\" in the log.");
        page.AddChild(EditorFields.Area(q.T("LogDescription"), text => Set(Template, "Edit objective summary", ("LogDescription", text)), 50, "The one-line objective summary shown under \"Quest Objectives\" in the quest log."));
        Heading("2 · When the player returns early", "Shown when the player talks to the ender before finishing.");
        page.AddChild(EditorFields.Area(request.T("CompletionText"), text => Set(Request, "Edit progress text", ("CompletionText", text)), 70, "What the quest ender says when the player talks to it before finishing the objectives."));
        Add("Giver gesture", "Emote the giver plays when the player is not ready to turn in.", EmoteChoice(Request, "EmoteOnIncomplete", "Change progress gesture", "Emote the giver plays when the player is not ready to turn in."),
            ClientStyle.Tag("when ready"), EmoteChoice(Request, "EmoteOnComplete", "Change ready gesture", "Emote the giver plays when the player has finished the quest."));
        Heading("3 · When turned in", "The reward screen text.");
        page.AddChild(EditorFields.Area(offer.T("RewardText"), text => Set(Offer, "Edit turn-in text", ("RewardText", text)), 100, "The text shown on the reward screen when the quest is turned in."));
        for (int i = 1; i <= 4; i++)
        {
            int slot = i;
            Add(i == 1 ? "Gestures" : "", "Emotes played on the reward screen after the quest is turned in.", EmoteChoice(Offer, $"Emote{slot}", "Change turn-in gesture", "Emote played on the reward screen. 0 means none."),
                ClientStyle.Tag("after"), UNumber(Offer, $"EmoteDelay{slot}", "Change gesture delay", 60, "Delay in milliseconds before this turn-in emote plays."), ClientStyle.Tag("ms"));
        }

        Heading("Completion log line", "Quest log text once the objectives are done (optional).");
        page.AddChild(EditorFields.Area(q.T("QuestCompletionLog"), text => Set(Template, "Edit completion log text", ("QuestCompletionLog", text)), 40, "Extra quest-log text shown after the objectives are complete. Leave blank for none."));
    }

    protected OptionButton EmoteChoice(TableSchema table, string column, string label, string tooltip = "")
    {
        var emotes = (catalog?.All("Emote") ?? []).Where(e => e.Name.Length > 0).Prepend((0u, "none")).DistinctBy(e => e.Item1).ToList();
        return EditorFields.Choice(emotes, RowOf(table).U(column), value => Set(table, label, (column, value)), tooltip);
    }

    protected void ItemSlot(Row q, string caption, string itemColumn, string countColumn, string what, string label, string tooltip = "")
    {
        uint item = q.U(itemColumn);
        if (item == 0)
        {
            Add(caption, tooltip, ButtonOf($"Add {what}…", () => Pick(SmartParamKind.Item, $"Choose {what}", id => Set(Template, label, (itemColumn, id), (countColumn, Math.Max(1u, Quest!.U(countColumn)))))));
            return;
        }

        Add(caption, tooltip, EditorFields.Number(q.U(countColumn), count => Set(Template, label, (countColumn, count)), 50, "How many are required."), ClientStyle.Tag("×"),
            EditorFields.Reference(Named(SmartParamKind.Item, item), () => Pick(SmartParamKind.Item, $"Choose {what}", id => Set(Template, label, (itemColumn, id))), () => Set(Template, label, (itemColumn, 0), (countColumn, 0)), Missing(SmartParamKind.Item, item), tooltip));
    }

    protected HBoxContainer FactionRef(TableSchema table, string column, string label, string tooltip = "")
    {
        uint faction = RowOf(table).U(column);
        string text = faction == 0 ? "None" : catalog?.FactionName(faction) is { } name ? name : $"Faction {faction}";
        return EditorFields.Reference(text, () => PickFromList("Choose faction", catalog?.All("Faction") ?? [], id => Set(table, label, (column, id))), faction == 0 ? null : () => Set(table, label, (column, 0)), false, tooltip);
    }

    protected void RenderChain(Row q)
    {
        var addon = RowOf(Addon);
        int prev = addon.I("PrevQuestID");
        uint next = addon.U("NextQuestID");
        string Arrow(uint id) => id == 0 ? "—" : Named(SmartParamKind.Quest, id);
        var chain = ClientStyle.Text($"{Arrow((uint)Math.Abs(prev))}   →   {q.T("LogTitle")}   →   {Arrow(next != 0 ? next : q.U("RewardNextQuest"))}", 15, ClientStyle.Body);
        chain.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(chain);
        Heading("Before this quest");
        Add("Previous quest", "The quest that must be completed (or in progress) before this one becomes available.",
            Ref(Addon, "PrevQuestID", SmartParamKind.Quest, "previous quest", "Change previous quest", true, "The quest that must come before this one."),
            EditorFields.Choice([(0, "must be completed"), (1, "must be in progress")], prev < 0 ? 1u : 0u, value => Set(Addon, "Change previous-quest rule", ("PrevQuestID", value == 1 ? -Math.Abs(prev) : Math.Abs(prev))), "Whether the previous quest must be completed or only in progress when this one is offered."));
        Add("Breadcrumb to", "The quest this breadcrumb leads to; completing this quest starts that chain.",
            Ref(Addon, "BreadcrumbForQuestId", SmartParamKind.Quest, "quest this leads to", "Change breadcrumb target", false, "The quest this breadcrumb leads to."));
        Heading("After this quest");
        Add("Unlocks", "The quest that becomes available after this one is completed.",
            Ref(Addon, "NextQuestID", SmartParamKind.Quest, "next quest", "Change next quest", false, "The quest this one unlocks."));
        Add("Offered right away", "A follow-up quest offered the moment this one is turned in.",
            Ref(Template, "RewardNextQuest", SmartParamKind.Quest, "quest offered on turn-in", "Change follow-up quest", false, "A follow-up quest offered immediately when this one is turned in."));
        Heading("Exclusive group", "Quests sharing a positive group: completing one hides the rest. Negative group: all must be done to unlock the next quest. 0 = none.");
        Add("Group", "Quests sharing a positive group hide each other; a negative group means all of them must be done. 0 means no group.",
            UNumber(Addon, "ExclusiveGroup", "Change exclusive group", 80, "Positive: completing one hides the rest. Negative: all must be done. 0 means no group."));

        Heading("Requirements");
        Add("Skill", "A profession or skill the player must have before this quest is offered.",
            UNumber(Addon, "RequiredSkillID", "Change required skill", 60, "SkillLine ID, e.g. 197 Tailoring. 0 means no skill."), ClientStyle.Tag("at least"), UNumber(Addon, "RequiredSkillPoints", "Change required skill", 60, "Minimum rank in the required skill."));
        Add("Reputation at least", "A faction the player must be at least friendly with before this quest is offered.",
            FactionRef(Addon, "RequiredMinRepFaction", "Change reputation requirement", "The faction the player must be at least friendly with."), UNumber(Addon, "RequiredMinRepValue", "Change reputation requirement", 70, "Reputation value required: 0 neutral, 3000 friendly, 9000 honored, 21000 revered, 42000 exalted."));
        Add("Reputation below", "A faction the player must be below a certain standing with before this quest is offered.",
            FactionRef(Addon, "RequiredMaxRepFaction", "Change reputation limit", "The faction the player must be below a certain standing with."), UNumber(Addon, "RequiredMaxRepValue", "Change reputation limit", 70, "Maximum reputation value allowed with that faction."));
        ChainExtras();
        page.AddChild(ClientStyle.Text("Conditions (table conditions) and scripted availability are not edited here.", 11, ClientStyle.Faded));
    }

    /// <summary>Settings a dialect adds to the chain page (TrinityCore's quest script).</summary>
    protected virtual void ChainExtras()
    {
    }

    protected void RenderGivers()
    {
        GiverList(Starter, "Who offers it", "quest giver", "Add quest giver");
        GiverList(Ender, "Who completes it", "quest ender", "Add quest ender");
        var starters = effective.All(Starter).Where(r => r.U("quest") == QuestId).Select(r => r.U("id")).ToList();
        var enders = effective.All(Ender).Where(r => r.U("quest") == QuestId).Select(r => r.U("id")).ToHashSet();
        var missing = starters.Where(s => !enders.Contains(s)).ToList();
        if (missing.Count > 0)
        {
            var also = ButtonOf("Givers also complete it", () => Commit("Givers also complete the quest", missing.Select(id =>
                new RowEdit(Ender.Name, $"{id}|{QuestId}", null, Row.Of(("id", id), ("quest", QuestId!.Value)))).ToArray()));
            also.TooltipText = "Make every giver that does not already accept this quest also accept it.";
            page.AddChild(also);
        }

        page.AddChild(ClientStyle.Text("Creatures also need the Quest Giver flag (Interactions window). Object and item starters are not edited here.", 11, ClientStyle.Faded));
    }

    protected void GiverList(TableSchema table, string heading, string what, string label)
    {
        Heading(heading);
        foreach (var link in effective.All(table).Where(r => r.U("quest") == QuestId).OrderBy(r => r.U("id")))
        {
            uint entry = link.U("id");
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Text(Named(SmartParamKind.Creature, entry), 14, Missing(SmartParamKind.Creature, entry) ? ClientStyle.Error : null));
            var fly = ButtonOf("Fly to", async () =>
            {
                status = $"Locating {snapshot!.Names.Creature(entry) ?? $"creature {entry}"}…";
                UpdateStatus();
                status = await FlyToEntry(entry, snapshot.Names.Creature(entry) ?? $"Creature {entry}");
                UpdateStatus();
            });
            fly.TooltipText = "Select this creature in the world.";
            row.AddChild(fly);
            var behavior = ButtonOf("Behavior…", () => BehaviorRequested(entry));
            behavior.TooltipText = "Open this creature's SmartAI rules.";
            row.AddChild(behavior);
            var linkRow = link;
            var remove = ButtonOf("Remove", () => Commit($"Remove {what}", new RowEdit(table.Name, table.KeyOf(linkRow), linkRow, null)));
            remove.TooltipText = $"Stop this creature being a {what} for the quest.";
            row.AddChild(remove);
            page.AddChild(row);
        }

        var add = ButtonOf($"+ Add {what}…", () => Pick(SmartParamKind.Creature, $"Choose {what}", id =>
        {
            var row = Row.Of(("id", id), ("quest", QuestId!.Value));
            if (effective.Get(table, table.KeyOf(row)) == null)
            {
                Commit(label, new RowEdit(table.Name, table.KeyOf(row), null, row));
            }
        }));
        add.TooltipText = $"Choose a creature to add as a {what}.";
        page.AddChild(add);
    }
}
