using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class QuestsSection(CreatureContext ctx) : InteractionSection(ctx)
{
    public override string Id => "quests";
    public override string Title => "Quests";
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editing || v.Starts.Length + v.Ends.Length > 0);
    public override string Badge => Ctx.View is { } v && v.Starts.Length + v.Ends.Length > 0 ? (v.Starts.Length + v.Ends.Length).ToString() : "";
    public override bool ScrollsItself => true;

    // The list scrolls inside the section; the opened quest is the quest editor panel, which scrolls itself.
    readonly ScrollContainer listScroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    readonly VBoxContainer list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly VBoxContainer detail = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    uint questDetail, openedQuest;

    protected override VBoxContainer body => list;

    /// <summary>The quest opened in the embedded editor, 0 for the list (smoke tests).</summary>
    public uint SmokeQuestDetail => questDetail;

    public override void _Ready()
    {
        list.AddThemeConstantOverride("separation", 6);
        listScroll.AddChild(list);
        AddChild(listScroll);
        var row = new HBoxContainer();
        row.AddChild(ButtonOf("◂ Back to quests", () =>
        {
            questDetail = 0;
            Render();
        }));
        row.AddChild(ButtonOf("Open in Quest window", () => Ctx.RequestQuestEditor(questDetail)));
        detail.AddChild(row);
        AddChild(detail);
    }

    protected override void Clear()
    {
        foreach (var child in list.GetChildren())
        {
            child.QueueFree();
        }
    }

    protected override void Populate(NpcInteraction v)
    {
        bool showDetail = questDetail != 0;
        listScroll.Visible = !showDetail;
        detail.Visible = showDetail;
        if (!showDetail)
        {
            openedQuest = 0;
            RenderQuests(v);
            return;
        }

        var panel = Ctx.Quest;
        if (panel.GetParent() != detail)
        {
            panel.GetParent()?.RemoveChild(panel);
            detail.AddChild(panel);
        }

        if (openedQuest != questDetail)
        {
            openedQuest = questDetail;
            panel.Open(questDetail);
        }
    }

    static Button ButtonOf(string text, Action action) => ContentEditorWindow.ButtonOf(text, action);

    public override void OnShown(object? argument)
    {
        questDetail = argument is uint quest ? quest : 0;
        openedQuest = 0;
    }

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        questDetail = 0;
        openedQuest = 0;
    }

    void RenderQuests(NpcInteraction v)
    {
        body.AddChild(ClientStyle.Text("Current Quests", 16, ClientStyle.Header));
        if (v.Ends.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var quest in v.Ends)
        {
            body.AddChild(QuestButton(quest, ClientStyle.ActiveQuestIcon));
        }

        body.AddChild(ClientStyle.Text("Available Quests", 16, ClientStyle.Header));
        if (v.Starts.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var quest in v.Starts)
        {
            body.AddChild(QuestButton(quest, ClientStyle.AvailableQuestIcon));
        }

        int conditions = v.Conditions.GetValueOrDefault(19);
        body.AddChild(ClientStyle.Text($"In game, availability depends on the player's level, race, class and earlier quests{(conditions > 0 ? $" and {conditions} SQL condition(s)" : "")}.", 11, ClientStyle.Faded));
        if (editing)
        {
            RenderQuestLinkEditor(v);
        }
    }

    Control QuestButton(QuestLinkView quest, string icon)
    {
        var button = new Button
        {
            Text = quest.Info != null ? $"{quest.Info.Title} ({quest.Info.Level})" : $"Quest {quest.Quest} (missing)",
            Alignment = HorizontalAlignment.Left
        };
        if (quest.Edited)
        {
            button.AddThemeColorOverride("font_color", ClientStyle.Edited);
        }

        button.Pressed += () =>
        {
            questDetail = quest.Quest;
            Render();
        };
        return IconRow(icon, button);
    }

    void RenderQuestLinkEditor(NpcInteraction v)
    {
        body.AddChild(new HSeparator());
        body.AddChild(ClientStyle.Text("Edit quest links", 16, ClientStyle.Header));
        foreach (var (table, links, kind) in new[] { (InteractionTables.QuestStarter, v.Starts, "starts"), (InteractionTables.QuestEnder, v.Ends, "ends") })
        {
            foreach (var link in links)
            {
                var row = new HBoxContainer();
                row.AddChild(ClientStyle.Text($"{kind}: {link.Info?.Title ?? "missing"} ({link.Quest})", 13, link.Edited ? ClientStyle.Edited : null));
                var remove = new Button
                {
                    Text = "Remove"
                };
                var existing = effective.Get(table, $"{v.Entry}|{link.Quest}");
                remove.Disabled = existing == null;
                remove.Pressed += () => Commit($"Remove quest {link.Quest} ({kind})", [Delete(table, existing!)]);
                row.AddChild(remove);
                var open = new Button
                {
                    Text = "Quest Editor"
                };
                open.Pressed += () => Ctx.RequestQuestEditor(link.Quest);
                row.AddChild(open);
                body.AddChild(row);
            }
        }

        SearchPicker("quest", "Find quest by title or ID…", async term =>
        {
            var found = await new InteractionSource(Connection()).SearchQuestsAsync(term, CancellationToken.None);
            return found.SelectMany(q => new (string, Action)[]
            {
                ($"Starts: {q.Title} ({q.Id}, level {q.Level})", () => LinkQuest(InteractionTables.QuestStarter, q)),
                ($"Ends: {q.Title} ({q.Id})", () => LinkQuest(InteractionTables.QuestEnder, q))
            }).ToList();
        });
    }

    void LinkQuest(TableSchema table, (uint Id, string Title, int Level) quest)
    {
        // Seed the lookup so validation knows the picked quest before the next reload.
        snapshot!.Lookup.Quests.TryAdd(quest.Id, new QuestInfo(quest.Id, quest.Title, quest.Level, 0, "", "", 0, []));
        Commit($"Link quest {quest.Id}", [Insert(table, Row.Of(("id", snapshot.Entry), ("quest", quest.Id)))]);
    }
}
