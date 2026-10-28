using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The quests a TrinityCore creature starts and ends (creature_queststarter/questender). Links can be
/// removed and added from a quest_template search; each quest opens in the Quest window.
/// </summary>
public sealed partial class TcQuestsSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "quests";
    public override string Title => "Quests";
    public override string Badge => Ctx.TcView is { } v && v.Starts.Length + v.Ends.Length > 0 ? $"{v.Starts.Length}/{v.Ends.Length}" : "";
    public override bool HasContent => Ctx.TcView is { } v && (Ctx.Editable || v.Starts.Length + v.Ends.Length > 0);
    readonly SearchState<(uint Id, string Title, int Level)> search = new();

    public override void OnCreatureChanged(CreatureSpawn? creature) => search.Reset();

    protected override void Populate(TcCreatureView v)
    {
        foreach (var issue in TcQuestLinkRules.Check(Ctx.Effective))
            Line(issue.Message, ClientStyle.Warning, 11);

        bool editable = Ctx.Editable;
        void List(string title, ItemListSpec spec, TcQuestLink[] quests)
        {
            Heading($"{title} ({quests.Length})");
            var block = Block();
            if (quests.Length == 0)
            {
                Line("None.", ClientStyle.Faded, 12, block);
            }

            new PageRenderer(Ctx).RenderItems(block, spec, Ctx.Effective, v.Entry, editable,
                row =>
                {
                    uint id = row.U("quest");
                    var quest = quests.FirstOrDefault(q => q.Quest == id);
                    string? name = quest?.Title ?? Ctx.Snapshot?.Lookup.Quest(id)?.Title;
                    int conditions = Ctx.Snapshot?.Lookup.QuestConditionCounts.GetValueOrDefault(id) ?? 0;
                    var line = new HBoxContainer();
                    line.AddThemeConstantOverride("separation", 6);
                    line.AddChild(ClientStyle.Text($"{name ?? "Missing quest"} ({id.ToString(CultureInfo.InvariantCulture)}){(conditions > 0 ? $" · {conditions} availability condition(s)" : "")}",
                        13, name == null ? ClientStyle.Error : ClientStyle.Gold));
                    if (name != null)
                    {
                        var open = new Button { Name = "OpenQuest", Text = "Open in Quest window" };
                        open.Pressed += () => Ctx.RequestQuestEditor(id);
                        line.AddChild(open);
                    }

                    return line;
                });
        }

        List("Starts", Ctx.Schema.QuestLinksPage.Starts, v.Starts);
        List("Ends", Ctx.Schema.QuestLinksPage.Ends, v.Ends);
        if (editable)
        {
            Heading("Link a quest");
            LinkControls(v, Block());
        }
    }

    void LinkControls(TcCreatureView v, Control block)
    {
        var page = Ctx.Schema.QuestLinksPage;
        void Link(uint quest, string title, int level, bool gives, bool takes)
        {
            bool Has(ItemListSpec spec) => spec.Select(Ctx.Effective, v.Entry).Any(r => r.U("quest") == quest);
            var edits = page.SetRoles(Ctx.Effective, v.Entry, quest, gives || Has(page.Starts), takes || Has(page.Ends));
            if (edits.Length == 0)
            {
                Ctx.Status = $"{title} is already linked that way.";
                Ctx.Redraw();
                return;
            }

            // Seed the lookup so validation knows the picked quest before the next reload.
            Ctx.Snapshot!.Lookup.Quests.TryAdd(quest, new QuestInfo(quest, title, level, 0, "", "", 0, []));
            Ctx.Commit($"Link quest {quest}", edits);
        }

        SearchBox(block, search, "Search_quest", "Find quest by title or ID…", "quest", false,
            text => DBSource.Open(Ctx.Connection()).SearchQuestsAsync(text, CancellationToken.None));
        foreach (var (id, title, level) in search.Results)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label
            {
                Text = level > 0 ? $"{title} ({id}, level {level})" : $"{title} ({id})",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            });
            var start = new Button { Text = "Start", TooltipText = "The creature offers this quest." };
            start.Pressed += () => Link(id, title, level, true, false);
            var end = new Button { Text = "End", TooltipText = "Players hand this quest in to the creature." };
            end.Pressed += () => Link(id, title, level, false, true);
            row.AddChild(start);
            row.AddChild(end);
            block.AddChild(row);
        }
    }
}
