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
        void List(string title, TcQuestLink[] quests)
        {
            Heading($"{title} ({quests.Length})");
            var block = Block();
            if (quests.Length == 0)
            {
                Line("None.", ClientStyle.Faded, 12, block);
            }

            foreach (var quest in quests)
            {
                Line($"{quest.Title ?? "Missing quest"} ({quest.Quest.ToString(CultureInfo.InvariantCulture)})", quest.Title == null ? ClientStyle.Error : ClientStyle.Gold, 13, block);
            }
        }

        List("Starts", v.Starts);
        List("Ends", v.Ends);
    }
}
