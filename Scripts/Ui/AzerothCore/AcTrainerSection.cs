using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class AcTrainerSection(CreatureContext ctx) : AcInteractionSection(ctx)
{
    public override string Id => PickerPages.Trainer.Id;
    public override string Title => PickerPages.Trainer.Title;
    public override string IconPath => PickerPages.Trainer.IconPath;
    public override bool HasContent => Ctx.AcView is { } v && (Ctx.Editable || v.Trainer != null);

    protected override void Populate(AcCreatureView v)
    {
        RenderTrainer(v);
    }

    TrainerPageSpec Page => Ctx.Schema.TrainerPage;

    void RenderTrainer(AcCreatureView v)
    {
        if (v.Trainer is not { } trainer)
        {
            body.AddChild(ClientStyle.Text("No trainer is assigned (creature_default_trainer).", 15, ClientStyle.Faded));
        }
        else
        {
            if (trainer.Greeting.Length > 0)
            {
                body.AddChild(ClientStyle.Text(InteractionText.Render(trainer.Greeting), 15));
            }

            body.AddChild(ClientStyle.Text($"Trainer {trainer.Id} · type {trainer.Type} · requirement {trainer.Requirement}{(trainer.SharedBy > 1 ? $" · shared by {trainer.SharedBy} creatures" : "")}", 12, trainer.SharedBy > 1 ? ClientStyle.Warning : ClientStyle.Faded));
            if (!editable)
            {
                RenderSpells(trainer);
            }
        }

        if (editable)
        {
            RenderTrainerEditor(v);
        }
    }

    void RenderTrainerEditor(AcCreatureView v)
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
            uint trainer = NextId(Page.Trainer, "Id", snapshot!.Lookup.MaxTrainerId);
            Commit("Create trainer", [Insert(Page.Trainer, Row.Of(("Id", trainer), ("Type", 0u), ("Requirement", 0u), ("Greeting", ""))), .. AssignEdits(trainer, Page.TrainerFlag)]);
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
                uint copy = NextId(Page.Trainer, "Id", snapshot!.Lookup.MaxTrainerId);
                var trainerRow = effective.Get(Page.Trainer, current.Id.ToString(CultureInfo.InvariantCulture));
                List<RowEdit> edits = [Insert(Page.Trainer, trainerRow?.With("Id", copy) ?? Row.Of(("Id", copy), ("Type", current.Type), ("Requirement", current.Requirement), ("Greeting", current.Greeting)))];
                edits.AddRange(current.Spells.Select(s => Insert(Page.Spells.Table, s.Row.With("TrainerId", copy))));
                edits.AddRange(AssignEdits(copy, 0));
                Commit($"Copy trainer {current.Id} for this NPC", edits);
            };
            body.AddChild(fork);
        }

        RenderSpells(current);

        SearchPicker("spell", "Find spell by name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add trainer spell {s.Id}", [Page.Spells.Add(effective, current.Id, ("SpellId", s.Id))])))).ToList()));
    }

    void RenderSpells(TrainerView trainer)
    {
        new PageRenderer(Ctx).RenderItems(body, Page.Spells, effective, trainer.Id, editable,
            row =>
            {
                var spell = trainer.Spells.First(s => s.SpellId == row.U("SpellId"));
                return ClientStyle.Text($"{spell.Name} ({spell.SpellId}) · {ClientStyle.Money(spell.Cost)}", 13, spell.Edited ? ClientStyle.Edited : null);
            });
    }

    /// <summary>
    /// Insert or update creature_default_trainer for this NPC, optionally adding npcflag bits.
    /// </summary>
    IEnumerable<RowEdit> AssignEdits(uint trainer, uint flag)
    {
        var link = effective.Get(Page.Bindings.Table, snapshot!.Entry.ToString(CultureInfo.InvariantCulture));
        var next = Row.Of(("CreatureId", snapshot.Entry), ("TrainerId", trainer));
        yield return link == null ? Insert(Page.Bindings.Table, next) : Update(Page.Bindings.Table, link, next);
        if (flag != 0 && (TemplateRow.U("npcflag") & flag) == 0)
        {
            yield return Update(Ctx.Schema.Tables.Find("creature_template"), TemplateRow, TemplateRow.With("npcflag", TemplateRow.U("npcflag") | flag));
        }
    }

    async Task AssignTrainer(uint trainer)
    {
        if (!effective.All(Page.Trainer).Any(r => r.U("Id") == trainer) && !snapshot!.Lookup.TrainerExists(trainer))
        {
            if (!await DBSource.Open(Connection()).TrainerExistsAsync(trainer, CancellationToken.None))
            {
                status = $"Not saved: trainer {trainer} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Trainers.Add(trainer);
        }

        Commit($"Assign trainer {trainer}", AssignEdits(trainer, Page.TrainerFlag));
    }
}
