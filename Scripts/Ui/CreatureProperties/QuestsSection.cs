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
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || v.Starts.Length + v.Ends.Length > 0);
    public override string Badge => Ctx.View is { } v && v.Starts.Length + v.Ends.Length > 0 ? (v.Starts.Length + v.Ends.Length).ToString() : "";
    public override bool ScrollsItself => true;

    // The list scrolls inside the section; the opened quest is the quest editor panel, which scrolls itself.
    readonly ScrollContainer listScroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    readonly VBoxContainer list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly VBoxContainer detail = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    uint questDetail, openedQuest;
    string questTerm = "";
    List<(uint Id, string Title, int Level)> foundQuests = [];
    readonly Dictionary<uint, (bool Gives, bool Takes)> roles = [];

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
        body.AddChild(ClientStyle.Text("Linked quests", 16, ClientStyle.Header));
        var linked = v.Starts.Concat(v.Ends).Select(q => q.Quest).Distinct()
            .Select(id => (Id: id, Start: v.Starts.FirstOrDefault(q => q.Quest == id), End: v.Ends.FirstOrDefault(q => q.Quest == id)))
            .OrderBy(q => (q.Start ?? q.End)!.Info?.Title ?? "", StringComparer.OrdinalIgnoreCase).ToList();
        if (linked.Count == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var (id, start, end) in linked)
        {
            body.AddChild(QuestRow(v, start ?? end!, start != null, end != null, (start?.Edited ?? false) || (end?.Edited ?? false)));
        }

        int conditions = v.Conditions.GetValueOrDefault(19);
        body.AddChild(ClientStyle.Text($"In game, availability depends on the player's level, race, class and earlier quests{(conditions > 0 ? $" and {conditions} SQL condition(s)" : "")}.", 11, ClientStyle.Faded));
        if (editable)
        {
            RenderQuestLinkEditor(v);
        }
    }

    /// <summary>"!" for a quest the NPC gives, "?" for one it accepts, both for both, in a fixed-width slot so titles line up.</summary>
    Control QuestIcons(bool gives, bool takes)
    {
        var slot = new HBoxContainer
        {
            CustomMinimumSize = new(34, 0)
        };
        slot.AddThemeConstantOverride("separation", 2);
        foreach (var path in new[] { gives ? ClientStyle.AvailableQuestIcon : null, takes ? ClientStyle.ActiveQuestIcon : null }.OfType<string>())
        {
            slot.AddChild(new TextureRect
            {
                Texture = icons.Get(path) ?? icons.Get(ClientStyle.MissingIcon),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new(16, 16)
            });
        }

        return slot;
    }

    Control QuestRow(NpcInteraction v, QuestLinkView quest, bool gives, bool takes, bool edited)
    {
        var button = new Button
        {
            Text = quest.Info != null ? $"{quest.Info.Title} ({quest.Info.Level})" : $"Quest {quest.Quest} (missing)",
            Alignment = HorizontalAlignment.Left
        };
        if (edited)
        {
            button.AddThemeColorOverride("font_color", ClientStyle.Edited);
        }

        button.Pressed += () =>
        {
            questDetail = quest.Quest;
            Render();
        };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(QuestIcons(gives, takes));
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(button);
        if (!editable)
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
            var starter = effective.Get(InteractionTables.QuestStarter, $"{v.Entry}|{quest.Quest}");
            var ender = effective.Get(InteractionTables.QuestEnder, $"{v.Entry}|{quest.Quest}");
            var edits = new List<RowEdit>();
            string label;
            switch (id)
            {
                case 0:
                    label = gives ? "Stop giving" : "Give";
                    edits.Add(gives ? Delete(InteractionTables.QuestStarter, starter!) : Insert(InteractionTables.QuestStarter, Row.Of(("id", v.Entry), ("quest", quest.Quest))));
                    break;
                case 1:
                    label = takes ? "Stop accepting" : "Accept";
                    edits.Add(takes ? Delete(InteractionTables.QuestEnder, ender!) : Insert(InteractionTables.QuestEnder, Row.Of(("id", v.Entry), ("quest", quest.Quest))));
                    break;
                default:
                    label = "Unlink";
                    edits.AddRange(new[] { (InteractionTables.QuestStarter, starter), (InteractionTables.QuestEnder, ender) }.Where(e => e.Item2 != null).Select(e => Delete(e.Item1, e.Item2!)));
                    break;
            }

            Commit($"{label} quest {quest.Quest}", edits);
        };
        row.AddChild(menu);
        return row;
    }

    void RenderQuestLinkEditor(NpcInteraction v)
    {
        body.AddChild(new HSeparator());
        body.AddChild(ClientStyle.Text("Link a quest", 16, ClientStyle.Header));
        var row = new HBoxContainer();
        body.AddChild(row);
        var term = new LineEdit
        {
            Text = questTerm,
            PlaceholderText = "Find quest by title or ID…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Name = "Search_quest"
        };
        var go = new Button
        {
            Text = "Search"
        };
        async void Search()
        {
            questTerm = term.Text;
            status = "Searching…";
            Render();
            try
            {
                foundQuests = (await new InteractionSource(Connection()).SearchQuestsAsync(questTerm, CancellationToken.None)).ToList();
                status = $"{foundQuests.Count} result(s) for \"{questTerm}\".";
            }
            catch (Exception error)
            {
                status = CreatureSource.SafeError(error);
            }

            Render();
        }

        term.TextSubmitted += _ => Search();
        go.Pressed += Search;
        row.AddChild(term);
        row.AddChild(go);
        foreach (var quest in foundQuests)
        {
            body.AddChild(LinkRow(v, quest));
        }
    }

    Control LinkRow(NpcInteraction v, (uint Id, string Title, int Level) quest)
    {
        var (gives, takes) = roles.GetValueOrDefault(quest.Id, (true, false));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label
        {
            Text = $"{quest.Title} ({quest.Id}, level {quest.Level})",
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
            var edits = new List<RowEdit>();
            foreach (var (table, wanted) in new[] { (InteractionTables.QuestStarter, giver.ButtonPressed), (InteractionTables.QuestEnder, ender.ButtonPressed) })
            {
                if (wanted && effective.Get(table, $"{v.Entry}|{quest.Id}") == null)
                {
                    edits.Add(Insert(table, Row.Of(("id", v.Entry), ("quest", quest.Id))));
                }
            }

            if (edits.Count == 0)
            {
                status = giver.ButtonPressed || ender.ButtonPressed ? $"{quest.Title} is already linked that way." : "Tick Giver, Ender or both.";
                Render();
                return;
            }

            // Seed the lookup so validation knows the picked quest before the next reload.
            snapshot!.Lookup.Quests.TryAdd(quest.Id, new QuestInfo(quest.Id, quest.Title, quest.Level, 0, "", "", 0, []));
            Commit($"Link quest {quest.Id}", edits);
        };
        row.AddChild(link);
        return row;
    }
}
