using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The creature's TrinityCore trainers (creature_trainer): each is opened by a gossip option, or is
/// the default trainer when bound to menu 0 option 0, and lists its trainer_spell rows.
/// </summary>
public sealed partial class TcTrainerSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "trainer";
    public override string Title => "Trainer";
    public override string Badge => Ctx.TcView is { Trainers.Length: > 0 } v ? v.Trainers.Length.ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcView is { Trainers.Length: > 0 };

    protected override void Populate(TcCreatureView v)
    {
        if (v.Trainers.Length == 0)
        {
            Line("This creature trains nothing.", ClientStyle.Faded);
            return;
        }

        foreach (var trainer in v.Trainers)
        {
            Heading($"Trainer {trainer.TrainerId}");
            var block = Block();
            Line(trainer.MenuId == 0 && trainer.OptionId == 0 ? "Default trainer of the creature." : $"Opened by menu {trainer.MenuId}, option {trainer.OptionId}.", ClientStyle.Faded, 12, block);
            if (trainer.Missing)
            {
                Line($"Trainer {trainer.TrainerId} is missing from the trainer table.", ClientStyle.Error, 12, block);
                continue;
            }

            Facts([("Type", trainer.Type.ToString(CultureInfo.InvariantCulture)), ("Greeting", trainer.Greeting.Length > 0 ? trainer.Greeting : "None")], block);
            Line($"{trainer.Spells.Length} spell(s)", ClientStyle.Header, 13, block);
            new PageRenderer(Ctx).RenderItems(block, Ctx.Schema.TrainerPage.Spells, Ctx.Effective, trainer.TrainerId, false,
                row =>
                {
                    var spell = trainer.Spells.First(s => s.SpellId == row.U("SpellId"));
                    return ClientStyle.Text($"{spell.Name ?? "Spell"} ({spell.SpellId}) · {ClientStyle.Money(spell.MoneyCost)}", 13,
                        spell.Name == null ? ClientStyle.Faded : ClientStyle.Body);
                });
        }
    }
}
