using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The creature's TrinityCore trainers (creature_trainer): each is opened by a gossip option, or is
/// the default trainer when bound to menu 0 option 0, and lists its trainer_spell rows. Edits the trainer's
/// type and greeting and its spells; bindings to gossip options stay read-only.
/// </summary>
public sealed partial class TcTrainerSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "trainer";
    public override string Title => "Trainer";
    public override string Badge => Ctx.TcView is { Trainers.Length: > 0 } v ? v.Trainers.Length.ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcView is { Trainers.Length: > 0 };
    readonly SearchState<(uint Id, string Name)> search = new();

    public override void OnCreatureChanged(CreatureSpawn? creature) => search.Reset();

    protected override void Populate(TcCreatureView v)
    {
        if (v.Trainers.Length == 0)
        {
            Line("This creature trains nothing.", ClientStyle.Faded);
            return;
        }

        foreach (var issue in TcTrainerRules.Check(Ctx.Effective))
            Line(issue.Message, ClientStyle.Warning, 11);

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

            bool editable = Ctx.TcEditable;
            if (trainer.SharedBy > 1)
                Line($"Shared by {trainer.SharedBy} creature bindings: changes here affect all of them.", ClientStyle.Warning, 12, block);
            if (editable) TrainerFields(trainer, block);
            else Facts([("Type", trainer.Type < TcTrainerRules.TypeNames.Length ? TcTrainerRules.TypeNames[trainer.Type] : trainer.Type.ToString(CultureInfo.InvariantCulture)), ("Greeting", trainer.Greeting.Length > 0 ? NpcInteraction.Render(trainer.Greeting) : "None")], block);
            Line($"{trainer.Spells.Length} spell(s)", ClientStyle.Header, 13, block);
            if (editable) AddSpellControls(trainer, block);
            new PageRenderer(Ctx).RenderItems(block, Ctx.Schema.TrainerPage.Spells, Ctx.Effective, trainer.TrainerId, editable,
                row =>
                {
                    var spell = trainer.Spells.First(s => s.SpellId == row.U("SpellId"));
                    return ClientStyle.Text($"{spell.Name ?? "Spell"} ({spell.SpellId}) · {ClientStyle.Money(spell.MoneyCost)}", 13,
                        spell.Name == null ? ClientStyle.Faded : ClientStyle.Body);
                });
        }
    }

    void TrainerFields(TcTrainerView trainer, Control block)
    {
        var row = Ctx.Effective.Get(TcTables.Trainer, trainer.TrainerId.ToString(CultureInfo.InvariantCulture));
        if (row == null) return;
        var box = new HBoxContainer();
        block.AddChild(box);
        box.AddChild(ClientStyle.Tag("Type"));
        var type = new OptionButton { TooltipText = "Which kind of trainer this is: none, talent, tradeskill or pet." };
        for (int i = 0; i < TcTrainerRules.TypeNames.Length; i++) type.AddItem(TcTrainerRules.TypeNames[i], i);
        type.Selected = (int)Math.Min(row.U("Type"), (uint)TcTrainerRules.TypeNames.Length - 1);
        type.ItemSelected += index => Ctx.Commit("Change trainer type", ItemListSpec.Change(TcTables.Trainer, row, row.With("Type", (uint)index)));
        box.AddChild(type);
        box.AddChild(ClientStyle.Tag("Greeting"));
        var greeting = new LineEdit { Text = row.T("Greeting"), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Text shown when the trainer window opens." };
        greeting.TextSubmitted += text => { if (text != row.T("Greeting")) Ctx.Commit("Change trainer greeting", ItemListSpec.Change(TcTables.Trainer, row, row.With("Greeting", text))); };
        box.AddChild(greeting);
    }

    void AddSpellControls(TcTrainerView trainer, Control block)
    {
        var spec = Ctx.Schema.TrainerPage.Spells;
        void Add(uint spell) => Ctx.Commit($"Add trainer spell {spell}", [spec.Add(Ctx.Effective, trainer.TrainerId, ("SpellId", spell))]);
        SearchBox(block, search, "Search_spell", "Find spell by name or ID…", "spell", true, text =>
        {
            var client = Ctx.Lookup()!;
            return Task.Run(() => client.SearchSpells(text, 30));
        });
        if (Ctx.Lookup() == null) Line("Open a retail client to search and validate spell IDs.", ClientStyle.Warning, 11, block);
        foreach (var (id, name) in search.Results)
        {
            var add = new Button { Text = $"+ {name} ({id})", Alignment = HorizontalAlignment.Left };
            add.Pressed += () => Add(id);
            block.AddChild(add);
        }

        var manual = new HBoxContainer();
        block.AddChild(manual);
        var spellId = new LineEdit { PlaceholderText = "Spell ID", CustomMinimumSize = new(120, 0) };
        var addId = new Button { Text = "Add ID" };
        addId.Pressed += () => Ctx.Try(() => Add(uint.Parse(spellId.Text, CultureInfo.InvariantCulture)));
        manual.AddChild(spellId);
        manual.AddChild(addId);
    }
}
