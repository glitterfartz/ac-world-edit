using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class TrainerSection(CreatureContext ctx) : InteractionSection(ctx)
{
    public override string Id => "trainer";
    public override string Title => "Trainer";
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editing || v.Trainer != null);

    protected override void Populate(NpcInteraction v)
    {
        RenderTrainer(v);
    }

    void RenderTrainer(NpcInteraction v)
    {
        if (v.Trainer is not { } trainer)
        {
            body.AddChild(ClientStyle.Text("No trainer is assigned (creature_default_trainer).", 15, ClientStyle.Faded));
        }
        else
        {
            if (trainer.Greeting.Length > 0)
            {
                body.AddChild(ClientStyle.Text(NpcInteraction.Render(trainer.Greeting), 15));
            }

            body.AddChild(ClientStyle.Text($"Trainer {trainer.Id} · type {trainer.Type} · requirement {trainer.Requirement}{(trainer.SharedBy > 1 ? $" · shared by {trainer.SharedBy} creatures" : "")}", 12, trainer.SharedBy > 1 ? ClientStyle.Warning : ClientStyle.Faded));
            if (!editing)
            {
                foreach (var spell in trainer.Spells)
                {
                    body.AddChild(ClientStyle.Text($"{spell.Name}   ·   {ClientStyle.Money(spell.Cost)}   ·   level {spell.ReqLevel}{(spell.ReqSkillLine != 0 ? $"   ·   skill {spell.ReqSkillLine} {spell.ReqSkillRank}" : "")}", 14, spell.Edited ? ClientStyle.Edited : null));
                }
            }
        }

        if (editing)
        {
            RenderTrainerEditor(v);
        }
    }

    void RenderTrainerEditor(NpcInteraction v)
    {
        body.AddChild(new HSeparator());
        var assign = new HBoxContainer();
        body.AddChild(assign);
        assign.AddChild(ClientStyle.Tag("Trainer ID"));
        var id = new LineEdit
        {
            Text = v.Trainer?.Id.ToString(CultureInfo.InvariantCulture) ?? "",
            CustomMinimumSize = new(80, 0),
            PlaceholderText = "existing ID"
        };
        id.TextSubmitted += text => Try(() => _ = AssignTrainer(ParseUInt(text)));
        assign.AddChild(id);
        var create = new Button
        {
            Text = "+ New trainer"
        };
        create.Pressed += () =>
        {
            uint trainer = NextId(InteractionTables.Trainer, "Id", snapshot!.Lookup.MaxTrainerId);
            Commit("Create trainer", [Insert(InteractionTables.Trainer, Row.Of(("Id", trainer), ("Type", 0u), ("Requirement", 0u), ("Greeting", ""))), .. AssignEdits(trainer, NpcFlags.Trainer)]);
        };
        assign.AddChild(create);
        if (v.Trainer is not { } current)
        {
            return;
        }

        if (current.SharedBy > 1)
        {
            var fork = new Button
            {
                Text = "Make a copy for this NPC"
            };
            fork.Pressed += () =>
            {
                uint copy = NextId(InteractionTables.Trainer, "Id", snapshot!.Lookup.MaxTrainerId);
                var trainerRow = effective.Get(InteractionTables.Trainer, current.Id.ToString(CultureInfo.InvariantCulture));
                List<RowEdit> edits = [Insert(InteractionTables.Trainer, trainerRow?.With("Id", copy) ?? Row.Of(("Id", copy), ("Type", current.Type), ("Requirement", current.Requirement), ("Greeting", current.Greeting)))];
                edits.AddRange(current.Spells.Select(s => Insert(InteractionTables.TrainerSpell, s.Row.With("TrainerId", copy))));
                edits.AddRange(AssignEdits(copy, 0));
                Commit($"Copy trainer {current.Id} for this NPC", edits);
            };
            body.AddChild(fork);
        }

        foreach (var spell in current.Spells)
        {
            var line = new HBoxContainer();
            body.AddChild(line);
            line.AddChild(ClientStyle.Text(spell.Name, 13, spell.Edited ? ClientStyle.Edited : null));
            foreach (var (column, tooltip, value) in new[] { ("MoneyCost", "Cost in copper", spell.Cost), ("ReqLevel", "Required level", spell.ReqLevel) })
            {
                var field = new LineEdit
                {
                    Text = value.ToString(CultureInfo.InvariantCulture),
                    TooltipText = tooltip,
                    CustomMinimumSize = new(64, 0)
                };
                var row = spell.Row;
                field.TextSubmitted += text => Try(() => Commit($"Edit trainer spell {spell.SpellId}", [Update(InteractionTables.TrainerSpell, row, row.With(column, ParseUInt(text)))]));
                line.AddChild(field);
            }

            var remove = new Button
            {
                Text = "✕"
            };
            remove.Pressed += () => Commit($"Remove trainer spell {spell.SpellId}", [Delete(InteractionTables.TrainerSpell, spell.Row)]);
            line.AddChild(remove);
        }

        SearchPicker("spell", "Find spell by name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add trainer spell {s.Id}", [Insert(InteractionTables.TrainerSpell, Row.Of(("TrainerId", current.Id), ("SpellId", s.Id), ("MoneyCost", 0u), ("ReqSkillLine", 0u), ("ReqSkillRank", 0u), ("ReqLevel", 1u)))])))).ToList()));
    }

    /// <summary>
    /// Insert or update creature_default_trainer for this NPC, optionally adding npcflag bits.
    /// </summary>
    IEnumerable<RowEdit> AssignEdits(uint trainer, uint flag)
    {
        var link = effective.Get(InteractionTables.DefaultTrainer, snapshot!.Entry.ToString(CultureInfo.InvariantCulture));
        var next = Row.Of(("CreatureId", snapshot.Entry), ("TrainerId", trainer));
        yield return link == null ? Insert(InteractionTables.DefaultTrainer, next) : Update(InteractionTables.DefaultTrainer, link, next);
        if (flag != 0 && (TemplateRow.U("npcflag") & flag) == 0)
        {
            yield return Update(InteractionTables.CreatureTemplate, TemplateRow, TemplateRow.With("npcflag", TemplateRow.U("npcflag") | flag));
        }
    }

    async Task AssignTrainer(uint trainer)
    {
        if (!effective.All(InteractionTables.Trainer).Any(r => r.U("Id") == trainer) && !snapshot!.Lookup.TrainerExists(trainer))
        {
            if (!await new InteractionSource(Connection()).TrainerExistsAsync(trainer, CancellationToken.None))
            {
                status = $"Not saved: trainer {trainer} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Trainers.Add(trainer);
        }

        Commit($"Assign trainer {trainer}", AssignEdits(trainer, NpcFlags.Trainer));
    }
}
