using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The quests a TrinityCore creature starts and ends (creature_queststarter/questender).</summary>
public sealed partial class TcQuestsSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "quests";
    public override string Title => "Quests";
    public override string Badge => Ctx.TcView is { } v && v.Starts.Length + v.Ends.Length > 0 ? $"{v.Starts.Length}/{v.Ends.Length}" : "";
    public override bool HasContent => Ctx.TcView is { } v && v.Starts.Length + v.Ends.Length > 0;

    protected override void Populate(TcCreatureView v)
    {
        void List(string title, ItemListSpec spec, TcQuestLink[] quests)
        {
            Heading($"{title} ({quests.Length})");
            var block = Block();
            if (quests.Length == 0)
            {
                Line("None.", ClientStyle.Faded, 12, block);
            }

            new PageRenderer(Ctx).RenderItems(block, spec, Ctx.Effective, v.Entry, false,
                row =>
                {
                    uint id = row.U("quest");
                    var quest = quests.First(q => q.Quest == id);
                    return ClientStyle.Text($"{quest.Title ?? "Missing quest"} ({id.ToString(CultureInfo.InvariantCulture)})",
                        13, quest.Title == null ? ClientStyle.Error : ClientStyle.Gold);
                });
        }

        List("Starts", Ctx.Schema.QuestLinksPage.Starts, v.Starts);
        List("Ends", Ctx.Schema.QuestLinksPage.Ends, v.Ends);
    }
}
