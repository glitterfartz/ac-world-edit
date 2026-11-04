using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// The creature's TrinityCore trainers (creature_trainer): each is opened by a gossip option, or is
/// the default trainer when bound to menu 0 option 0, and lists its trainer_spell rows. Edits the trainer's
/// type and greeting and its spells, and assigns, rebinds, copies and removes the bindings.
/// </summary>
public sealed partial class TcTrainerSection(CreatureContext ctx) : TcCreatureSection(ctx)
{
    public override string Id => PickerPages.Trainer.Id;
    public override string Title => PickerPages.Trainer.Title;
    public override string IconPath => PickerPages.Trainer.IconPath;
    public override string Badge => Ctx.TcView is { Trainers.Length: > 0 } v ? v.Trainers.Length.ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Editable || Ctx.TcView is { Trainers.Length: > 0 };
    readonly SearchState<(uint Id, string Name)> search = new();

    public override void OnCreatureChanged(CreatureSpawn? creature) => search.Reset();

    protected override void Populate(TcCreatureView v)
    {
        if (Ctx.Editable) AssignControls(v);
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

            bool editable = Ctx.Editable;
            if (trainer.SharedBy > 1)
                Line($"Shared by {trainer.SharedBy} creature bindings: changes here affect all of them.", ClientStyle.Warning, 12, block);
            if (editable) BindingControls(trainer, block);
            if (editable) TrainerFields(trainer, block);
            else Facts([("Type", trainer.Type < TcTrainerRules.TypeNames.Length ? TcTrainerRules.TypeNames[trainer.Type] : trainer.Type.ToString(CultureInfo.InvariantCulture)), ("Greeting", trainer.Greeting.Length > 0 ? InteractionText.Render(trainer.Greeting) : "None")], block);
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

    /// <summary>GossipOptionNpc 3 (Trainer): the option type a trainer binding belongs to.</summary>
    const uint TrainerOptionNpc = 3;

    /// <summary>The creature's trainer slots still free: the default one and each Trainer gossip option without a binding.</summary>
    static List<(uint Menu, uint Option, string Label)> FreeSlots(TcCreatureView v)
    {
        var slots = new List<(uint, uint, string)>();
        if (!v.Trainers.Any(t => t.MenuId == 0 && t.OptionId == 0)) slots.Add((0, 0, "Default trainer of the creature"));
        foreach (var menu in v.Menus)
        {
            foreach (var option in menu.Options.Where(o => o.OptionNpc == TrainerOptionNpc && o.TrainerId == null))
                slots.Add((menu.MenuId, option.OptionId, $"Menu {menu.MenuId} option {option.OptionId}: {InteractionText.Render(option.Text)}"));
        }

        return slots;
    }

    void AssignControls(TcCreatureView v)
    {
        var slots = FreeSlots(v);
        if (slots.Count == 0)
        {
            Line("Every trainer slot of this creature is taken. Add a Trainer option on the Gossip page for another.", ClientStyle.Faded, 12);
            return;
        }

        Heading("Assign a trainer");
        var box = new HBoxContainer();
        AddChild(box);
        var slot = new OptionButton { TooltipText = "Where the trainer opens: the creature's default, or a Trainer gossip option." };
        for (int i = 0; i < slots.Count; i++) slot.AddItem(slots[i].Label, i);
        box.AddChild(slot);
        var id = new LineEdit { PlaceholderText = "Trainer ID", CustomMinimumSize = new(110, 0) };
        var assign = new Button { Text = "Assign" };
        assign.Pressed += () => Ctx.Try(() => _ = Bind(v.Entry, slots[slot.Selected], uint.Parse(id.Text, CultureInfo.InvariantCulture)));
        id.TextSubmitted += _ => assign.EmitSignal(BaseButton.SignalName.Pressed);
        var create = new Button { Text = "+ New trainer" };
        create.Pressed += () =>
        {
            uint trainer = Ctx.NextId(TcTables.Trainer, "Id", Ctx.Snapshot!.Lookup.MaxTrainerId);
            var target = slots[slot.Selected];
            Ctx.Commit("Create trainer", [Insert(TcTables.Trainer, Row.Of(("Id", trainer), ("Type", 2u), ("Greeting", ""))), Insert(TcTables.CreatureTrainer, Link(v.Entry, target.Menu, target.Option, trainer))]);
        };
        box.AddChild(id);
        box.AddChild(assign);
        box.AddChild(create);
    }

    static Row Link(uint entry, uint menu, uint option, uint trainer) => Row.Of(("CreatureID", entry), ("MenuID", menu), ("OptionID", option), ("TrainerID", trainer));

    Row? LinkRow(TcTrainerView trainer) => Ctx.Effective.All(TcTables.CreatureTrainer)
        .FirstOrDefault(r => r.U("CreatureID") == Ctx.Snapshot!.Entry && r.U("MenuID") == trainer.MenuId && r.U("OptionID") == trainer.OptionId);

    /// <summary>A trainer must exist in the database (or in the draft) before a creature can use it.</summary>
    async Task<bool> TrainerKnown(uint trainer)
    {
        if (Ctx.Effective.Get(TcTables.Trainer, trainer.ToString(CultureInfo.InvariantCulture)) != null) return true;
        if (!await DBSource.Open(Ctx.Connection(), Ctx.Lookup()).TrainerExistsAsync(trainer, CancellationToken.None))
        {
            Ctx.Status = $"Trainer {trainer} does not exist.";
            Ctx.Redraw();
            return false;
        }

        Ctx.Snapshot?.Lookup.Trainers.Add(trainer);
        return true;
    }

    async Task Bind(uint entry, (uint Menu, uint Option, string Label) target, uint trainer)
    {
        if (!await TrainerKnown(trainer)) return;
        Ctx.Commit($"Assign trainer {trainer}", [Insert(TcTables.CreatureTrainer, Link(entry, target.Menu, target.Option, trainer))]);
    }

    async Task Rebind(Row link, uint trainer)
    {
        if (!await TrainerKnown(trainer)) return;
        Ctx.Commit($"Use trainer {trainer}", ItemListSpec.Change(TcTables.CreatureTrainer, link, link.With("TrainerID", trainer)));
    }

    void BindingControls(TcTrainerView trainer, Control block)
    {
        if (LinkRow(trainer) is not { } link) return;
        var box = new HBoxContainer();
        block.AddChild(box);
        var id = new LineEdit { PlaceholderText = "Other trainer ID", CustomMinimumSize = new(160, 0) };
        var use = new Button { Text = "Use trainer", TooltipText = "Open a different trainer from this slot." };
        use.Pressed += () => Ctx.Try(() => _ = Rebind(link, uint.Parse(id.Text, CultureInfo.InvariantCulture)));
        id.TextSubmitted += _ => use.EmitSignal(BaseButton.SignalName.Pressed);
        box.AddChild(id);
        box.AddChild(use);
        if (trainer.SharedBy > 1)
        {
            var copy = new Button { Text = "Copy for this creature", TooltipText = "Give this creature its own copy of the trainer and its spells." };
            copy.Pressed += () => CopyTrainer(trainer, link);
            box.AddChild(copy);
        }

        var remove = new Button { Text = "Remove binding", TooltipText = "The trainer and its spells stay; this creature stops using it." };
        remove.Pressed += () => Ctx.Commit("Remove trainer binding", [Delete(TcTables.CreatureTrainer, link)]);
        box.AddChild(remove);
    }

    void CopyTrainer(TcTrainerView trainer, Row link)
    {
        uint copy = Ctx.NextId(TcTables.Trainer, "Id", Ctx.Snapshot!.Lookup.MaxTrainerId);
        var source = Ctx.Effective.Get(TcTables.Trainer, trainer.TrainerId.ToString(CultureInfo.InvariantCulture));
        List<RowEdit> edits = [Insert(TcTables.Trainer, source?.With("Id", copy) ?? Row.Of(("Id", copy), ("Type", 2u), ("Greeting", "")))];
        edits.AddRange(Ctx.Effective.All(TcTables.TrainerSpell).Where(s => s.U("TrainerId") == trainer.TrainerId).Select(s => Insert(TcTables.TrainerSpell, s.With("TrainerId", copy))));
        edits.Add(Update(TcTables.CreatureTrainer, link, link.With("TrainerID", copy)));
        Ctx.Commit($"Copy trainer {trainer.TrainerId}", edits);
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
