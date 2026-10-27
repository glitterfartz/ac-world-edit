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
    protected HBoxContainer Ref(TableSchema table, string column, SmartParamKind kind, string what, string label, bool signed = false)
    {
        var row = RowOf(table);
        uint id = signed ? (uint)Math.Abs(row.I(column)) : row.U(column);
        Action choose = kind == SmartParamKind.Spell
            ? () => PickSpell($"Choose {what}", picked => Set(table, label, (column, picked)))
            : () => Pick(kind, $"Choose {what}", picked => Set(table, label, (column, picked)));
        return EditorFields.Reference(Named(kind, id), choose, id == 0 ? null : () => Set(table, label, (column, 0)), Missing(kind, id));
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
        page.AddChild(EditorFields.Placeholders(QuestModel.Placeholders));
        Heading("1 · When offered", "What the quest giver says before the player accepts.");
        page.AddChild(EditorFields.Area(q.T("QuestDescription"), text => Set(Template, "Edit quest text", ("QuestDescription", text)), 120));
        Heading("Objective summary", "One-line goal shown under \"Quest Objectives\" in the log.");
        page.AddChild(EditorFields.Area(q.T("LogDescription"), text => Set(Template, "Edit objective summary", ("LogDescription", text)), 50));
        Heading("2 · When the player returns early", "Shown when the player talks to the ender before finishing.");
        page.AddChild(EditorFields.Area(request.T("CompletionText"), text => Set(Request, "Edit progress text", ("CompletionText", text)), 70));
        Add("Giver gesture", EmoteChoice(Request, "EmoteOnIncomplete", "Change progress gesture"),
            ClientStyle.Tag("when ready"), EmoteChoice(Request, "EmoteOnComplete", "Change ready gesture"));
        Heading("3 · When turned in", "The reward screen text.");
        page.AddChild(EditorFields.Area(offer.T("RewardText"), text => Set(Offer, "Edit turn-in text", ("RewardText", text)), 100));
        for (int i = 1; i <= 4; i++)
        {
            int slot = i;
            Add(i == 1 ? "Gestures" : "", EmoteChoice(Offer, $"Emote{slot}", "Change turn-in gesture"),
                ClientStyle.Tag("after"), UNumber(Offer, $"EmoteDelay{slot}", "Change gesture delay", 60), ClientStyle.Tag("ms"));
        }

        Heading("Completion log line", "Quest log text once the objectives are done (optional).");
        page.AddChild(EditorFields.Area(q.T("QuestCompletionLog"), text => Set(Template, "Edit completion log text", ("QuestCompletionLog", text)), 40));
    }

    protected OptionButton EmoteChoice(TableSchema table, string column, string label)
    {
        var emotes = (catalog?.All("Emote") ?? []).Where(e => e.Name.Length > 0).Prepend((0u, "none")).DistinctBy(e => e.Item1).ToList();
        return EditorFields.Choice(emotes, RowOf(table).U(column), value => Set(table, label, (column, value)));
    }

    protected void ItemSlot(Row q, string caption, string itemColumn, string countColumn, string what, string label)
    {
        uint item = q.U(itemColumn);
        if (item == 0)
        {
            Add(caption, ButtonOf($"Add {what}…", () => Pick(SmartParamKind.Item, $"Choose {what}", id => Set(Template, label, (itemColumn, id), (countColumn, Math.Max(1u, Quest!.U(countColumn)))))));
            return;
        }

        Add(caption, EditorFields.Number(q.U(countColumn), count => Set(Template, label, (countColumn, count)), 50), ClientStyle.Tag("×"),
            EditorFields.Reference(Named(SmartParamKind.Item, item), () => Pick(SmartParamKind.Item, $"Choose {what}", id => Set(Template, label, (itemColumn, id))), () => Set(Template, label, (itemColumn, 0), (countColumn, 0)), Missing(SmartParamKind.Item, item)));
    }

    protected HBoxContainer FactionRef(TableSchema table, string column, string label)
    {
        uint faction = RowOf(table).U(column);
        string text = faction == 0 ? "None" : catalog?.FactionName(faction) is { } name ? name : $"Faction {faction}";
        return EditorFields.Reference(text, () => PickFromList("Choose faction", catalog?.All("Faction") ?? [], id => Set(table, label, (column, id))), faction == 0 ? null : () => Set(table, label, (column, 0)));
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
        Add("Previous quest", Ref(Addon, "PrevQuestID", SmartParamKind.Quest, "previous quest", "Change previous quest", true),
            EditorFields.Choice([(0, "must be completed"), (1, "must be in progress")], prev < 0 ? 1u : 0u, value => Set(Addon, "Change previous-quest rule", ("PrevQuestID", value == 1 ? -Math.Abs(prev) : Math.Abs(prev)))));
        Add("Breadcrumb to", Ref(Addon, "BreadcrumbForQuestId", SmartParamKind.Quest, "quest this leads to", "Change breadcrumb target"));
        Heading("After this quest");
        Add("Unlocks", Ref(Addon, "NextQuestID", SmartParamKind.Quest, "next quest", "Change next quest"));
        Add("Offered right away", Ref(Template, "RewardNextQuest", SmartParamKind.Quest, "quest offered on turn-in", "Change follow-up quest"));
        Heading("Exclusive group", "Quests sharing a positive group: completing one hides the rest. Negative group: all must be done to unlock the next quest. 0 = none.");
        Add("Group", UNumber(Addon, "ExclusiveGroup", "Change exclusive group", 80));

        Heading("Requirements");
        Add("Skill", UNumber(Addon, "RequiredSkillID", "Change required skill", 60, "SkillLine ID, e.g. 197 Tailoring"), ClientStyle.Tag("at least"), UNumber(Addon, "RequiredSkillPoints", "Change required skill", 60));
        Add("Reputation at least", FactionRef(Addon, "RequiredMinRepFaction", "Change reputation requirement"), UNumber(Addon, "RequiredMinRepValue", "Change reputation requirement", 70));
        Add("Reputation below", FactionRef(Addon, "RequiredMaxRepFaction", "Change reputation limit"), UNumber(Addon, "RequiredMaxRepValue", "Change reputation limit", 70));
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
            page.AddChild(ButtonOf("Givers also complete it", () => Commit("Givers also complete the quest", missing.Select(id =>
                new RowEdit(Ender.Name, $"{id}|{QuestId}", null, Row.Of(("id", id), ("quest", QuestId!.Value)))).ToArray())));
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
            row.AddChild(ButtonOf("Fly to", async () =>
            {
                status = $"Locating {snapshot!.Names.Creature(entry) ?? $"creature {entry}"}…";
                UpdateStatus();
                status = await FlyToEntry(entry, snapshot.Names.Creature(entry) ?? $"Creature {entry}");
                UpdateStatus();
            }));
            row.AddChild(ButtonOf("Behavior…", () => BehaviorRequested(entry)));
            var linkRow = link;
            row.AddChild(ButtonOf("Remove", () => Commit($"Remove {what}", new RowEdit(table.Name, table.KeyOf(linkRow), linkRow, null))));
            page.AddChild(row);
        }

        page.AddChild(ButtonOf($"+ Add {what}…", () => Pick(SmartParamKind.Creature, $"Choose {what}", id =>
        {
            var row = Row.Of(("id", id), ("quest", QuestId!.Value));
            if (effective.Get(table, table.KeyOf(row)) == null)
            {
                Commit(label, new RowEdit(table.Name, table.KeyOf(row), null, row));
            }
        })));
    }
}
