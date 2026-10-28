using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The quests a creature gives and accepts, for either dialect: one "Linked quests" list with a "!"
/// for each quest the NPC gives and a "?" for each it accepts, a "…" menu to change the roles or unlink,
/// and a quest search whose results carry Giver and Ender checkboxes. A quest opens in the embedded
/// quest editor. The link tables, rules and quest panel come from <see cref="CreatureContext.Schema"/>.
/// </summary>
public sealed partial class QuestsSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override string Id => PickerPages.Quests.Id;
    public override string Title => PickerPages.Quests.Title;
    public override string IconPath => PickerPages.Quests.IconPath;
    public override bool Supports(WorldDialect dialect) => true;
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || v.Starts.Length + v.Ends.Length > 0);
    public override string Badge => Ctx.View is { } v && v.Starts.Length + v.Ends.Length > 0 ? (v.Starts.Length + v.Ends.Length).ToString(CultureInfo.InvariantCulture) : "";
    public override bool ScrollsItself => true;

    // The list scrolls inside the section; the opened quest is the quest editor panel, which scrolls itself.
    readonly ScrollContainer listScroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    readonly VBoxContainer list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly VBoxContainer detail = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    readonly SearchState<(uint Id, string Title, int Level)> search = new();
    readonly Dictionary<uint, (bool Gives, bool Takes)> roles = [];
    uint questDetail, openedQuest;

    /// <summary>The quest opened in the embedded editor, 0 for the list (smoke tests).</summary>
    public uint SmokeQuestDetail => questDetail;

    QuestLinksPageSpec Page => Ctx.Schema.QuestLinksPage;

    public override void _Ready()
    {
        list.AddThemeConstantOverride("separation", 6);
        listScroll.AddChild(list);
        AddChild(listScroll);
        var row = new HBoxContainer();
        row.AddChild(ContentEditorWindow.ButtonOf("◂ Back to quests", () =>
        {
            questDetail = 0;
            Ctx.Redraw();
        }));
        row.AddChild(ContentEditorWindow.ButtonOf("Open in Quest window", () => Ctx.RequestQuestEditor(questDetail)));
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

    public override void OnShown(object? argument)
    {
        questDetail = argument is uint quest ? quest : 0;
        openedQuest = 0;
    }

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        questDetail = 0;
        openedQuest = 0;
        search.Reset();
        roles.Clear();
    }

    public override void Refresh()
    {
        Clear();
        if (Ctx.View is not { } v)
        {
            list.AddChild(Placeholder());
            return;
        }

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
        Host(panel, detail);
        if (openedQuest != questDetail)
        {
            openedQuest = questDetail;
            panel.Open(questDetail);
        }
    }

    int Conditions(uint quest) => Ctx.Snapshot?.Lookup.QuestConditionCounts.GetValueOrDefault(quest) ?? 0;

    void RenderQuests(ICreatureView v)
    {
        list.AddChild(ClientStyle.Text("Linked quests", 16, ClientStyle.Header));
        foreach (var issue in Page.Check(Ctx.Effective))
        {
            list.AddChild(ClientStyle.Text(issue.Message, 11, ClientStyle.Warning));
        }

        var linked = v.Starts.Concat(v.Ends).Select(q => q.Quest).Distinct()
            .Select(id => (Id: id, Start: v.Starts.FirstOrDefault(q => q.Quest == id), End: v.Ends.FirstOrDefault(q => q.Quest == id)))
            .OrderBy(q => (q.Start ?? q.End)!.Info?.Title ?? "", StringComparer.OrdinalIgnoreCase).ToList();
        if (linked.Count == 0)
        {
            list.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var (_, start, end) in linked)
        {
            list.AddChild(QuestRow(v, start ?? end!, start != null, end != null, (start?.Edited ?? false) || (end?.Edited ?? false)));
        }

        int conditions = linked.Sum(q => Conditions(q.Id));
        list.AddChild(ClientStyle.Text($"In game, availability depends on the player's level, race, class and earlier quests{(conditions > 0 ? $" and {conditions} SQL condition(s)" : "")}.", 11, ClientStyle.Faded));
        if (Ctx.Editable)
        {
            RenderLinkEditor(v);
        }
    }

    Control QuestRow(ICreatureView v, QuestLinkView quest, bool gives, bool takes, bool edited)
    {
        int conditions = Conditions(quest.Quest);
        var button = new Button
        {
            // TrinityCore quests have no level column (the level reads 0), so it is shown only when there is one.
            Text = (quest.Info is { } info ? info.Level > 0 ? $"{info.Title} ({info.Level})" : info.Title : $"Quest {quest.Quest} (missing)") + (conditions > 0 ? $" · {conditions} condition(s)" : ""),
            Alignment = HorizontalAlignment.Left
        };
        if (edited)
        {
            button.AddThemeColorOverride("font_color", ClientStyle.Edited);
        }

        button.Pressed += () =>
        {
            questDetail = quest.Quest;
            Ctx.Redraw();
        };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(QuestIcons(gives, takes));
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(button);
        if (!Ctx.Editable)
        {
            return row;
        }

        var menu = new MenuButton
        {
            Text = "…",
            TooltipText = "Change how this NPC is linked to the quest."
        };
        var popup = menu.GetPopup();
        popup.AddCheckItem("Gives this quest", 0);
        popup.SetItemChecked(0, gives);
        popup.AddCheckItem("Accepts it as handed in", 1);
        popup.SetItemChecked(1, takes);
        popup.AddSeparator();
        popup.AddItem("Unlink", 2);
        popup.IdPressed += id =>
        {
            string label = id switch { 0 => gives ? "Stop giving" : "Give", 1 => takes ? "Stop accepting" : "Accept", _ => "Unlink" };
            var (nextGives, nextTakes) = id switch
            {
                0 => (!gives, takes),
                1 => (gives, !takes),
                _ => (false, false)
            };
            Ctx.Commit($"{label} quest {quest.Quest}", Page.SetRoles(Ctx.Effective, v.Entry, quest.Quest, nextGives, nextTakes));
        };
        row.AddChild(menu);
        return row;
    }

    void RenderLinkEditor(ICreatureView v)
    {
        list.AddChild(new HSeparator());
        list.AddChild(ClientStyle.Text("Link a quest", 16, ClientStyle.Header));
        SearchBox(list, search, "Search_quest", "Find quest by title or ID…", "quest", false,
            text => DBSource.Open(Ctx.Connection()).SearchQuestsAsync(text, CancellationToken.None));
        foreach (var quest in search.Results)
        {
            list.AddChild(LinkRow(v, quest));
        }
    }

    Control LinkRow(ICreatureView v, (uint Id, string Title, int Level) quest)
    {
        var (gives, takes) = roles.GetValueOrDefault(quest.Id, (true, false));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label
        {
            Text = quest.Level > 0 ? $"{quest.Title} ({quest.Id}, level {quest.Level})" : $"{quest.Title} ({quest.Id})",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        });
        var giver = new CheckBox
        {
            Text = "Giver",
            ButtonPressed = gives,
            TooltipText = "The NPC offers this quest to players."
        };
        var ender = new CheckBox
        {
            Text = "Ender",
            ButtonPressed = takes,
            TooltipText = "Players hand this quest in to the NPC."
        };
        giver.Toggled += on => roles[quest.Id] = (on, ender.ButtonPressed);
        ender.Toggled += on => roles[quest.Id] = (giver.ButtonPressed, on);
        row.AddChild(giver);
        row.AddChild(ender);
        var link = new Button
        {
            Text = "Link"
        };
        link.Pressed += () =>
        {
            // Linking adds the selected roles without removing any role already assigned.
            bool Has(ItemListSpec spec) => spec.Select(Ctx.Effective, v.Entry).Any(r => r.U("quest") == quest.Id);
            var edits = Page.SetRoles(Ctx.Effective, v.Entry, quest.Id, giver.ButtonPressed || Has(Page.Starts), ender.ButtonPressed || Has(Page.Ends));
            if (edits.Length == 0)
            {
                Ctx.Status = giver.ButtonPressed || ender.ButtonPressed ? $"{quest.Title} is already linked that way." : "Tick Giver, Ender or both.";
                Ctx.Redraw();
                return;
            }

            // Seed the lookup so validation knows the picked quest before the next reload.
            Ctx.Snapshot!.Lookup.Quests.TryAdd(quest.Id, new QuestInfo(quest.Id, quest.Title, quest.Level, 0, "", "", 0, []));
            Ctx.Commit($"Link quest {quest.Id}", edits);
        };
        row.AddChild(link);
        return row;
    }
}
