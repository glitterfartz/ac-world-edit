using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// The creature template: who the creature is, how it fights and moves, what it drops, plus the
/// NPC flags and spell clicks that decide how players interact with it. Every editable template
/// column lives here or on the Class / Level / Stats page, under plain-language labels.
/// </summary>
public sealed partial class AcTemplateSection : AcInteractionSection
{
    readonly PageRenderer renderer;
    GridContainer grid = null!;

    public AcTemplateSection(CreatureContext ctx) : base(ctx)
    {
        renderer = new PageRenderer(ctx);
    }

    public override string Id => "template";
    public override string Title => "Template";
    public override string IconPath => "Interface/Icons/INV_Misc_Book_09.blp";

    public override void OnCreatureChanged(CreatureSpawn? creature) => renderer.Reset();

    protected override void Populate(NpcInteraction v)
    {
        var details = effective.Get(Ctx.Schema.Tables.Find("creature_details"),v.Entry.ToString(CultureInfo.InvariantCulture));
        if (details == null)
        {
            body.AddChild(ClientStyle.Text("This creature has no template row.", 14, ClientStyle.Faded));
            return;
        }

        if (editable)
        {
            body.AddChild(ClientStyle.Text($"The template applies to all {v.SpawnCount} spawn(s) of entry {v.Entry}.", 11, ClientStyle.Warning));
        }

        renderer.After.Clear();
        renderer.After["interaction"] = () => RenderInteraction(v);
        renderer.Render(body, Ctx.Schema.TemplatePage, effective, v.Entry, editable);
        RenderIssues(v);
    }

    void NewGrid()
    {
        grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        body.AddChild(grid);
    }

    void Add(string label, string tip, string shown, Func<Control> editor)
    {
        grid.AddChild(ClientStyle.Caption(label, tip));
        var control = editable ? editor() : ClientStyle.Text(shown, 14);
        control.TooltipText = tip;
        grid.AddChild(control);
    }

    /// <summary>The parts of the Interaction group that are not template columns: gossip menu, taxi node and spell clicks.</summary>
    void RenderInteraction(NpcInteraction v)
    {
        NewGrid();
        Add("Gossip menu", "ID of the gossip menu shown when players talk to it. Zero means no menu. The menu itself is edited on the Gossip page.", v.GossipMenuId.ToString(CultureInfo.InvariantCulture), GossipMenuBox);

        if ((v.NpcFlag & AcNpcFlags.FlightMaster) != 0 && catalog != null && target != null && catalog.NearestTaxiNode(target.Pose.Map, target.Pose.X, target.Pose.Y, target.Pose.Z) is { } node)
        {
            body.AddChild(ClientStyle.Text($"Flight master: nearest taxi node {node.Name} ({node.Id}), {node.Distance:0} yd.", 14));
        }

        body.AddChild(ClientStyle.Caption("Spell click", "Spells that cast when a player clicks the creature, used for rideable vehicles and similar. Each entry names the spell, who casts it and on whom."));
        if (v.SpellClicks.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var click in v.SpellClicks)
        {
            body.AddChild(ClientStyle.Text($"{click.Name}   ·   cast flags {click.CastFlags}   ·   user type {click.UserType}", 14, click.Edited ? ClientStyle.Edited : null));
        }

        if (editable)
        {
            RenderSpellClickEditor(v);
        }
    }

    LineEdit GossipMenuBox()
    {
        var edit = new LineEdit
        {
            Text = TemplateRow.U("gossip_menu_id").ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Try(() => _ = SetRootMenu(ParseUInt(text)));
        return edit;
    }

    void RenderIssues(NpcInteraction v)
    {
        if (v.Issues.Length == 0)
        {
            return;
        }

        body.AddChild(ClientStyle.Text("Validation", 16, ClientStyle.Header));
        foreach (var issue in v.Issues)
        {
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Text((issue.Error ? "Error: " : "Warning: ") + issue.Message, 13, issue.Error ? ClientStyle.Error : ClientStyle.Warning));
            if (issue.Fix != null && draft != null)
            {
                var fix = new Button
                {
                    Text = "Fix"
                };
                var edits = issue.Fix;
                fix.Pressed += () => Commit("Fix npcflag", edits);
                row.AddChild(fix);
            }

            body.AddChild(row);
        }
    }

    async Task SetRootMenu(uint menu)
    {
        if (menu != 0 && !effective.All(Ctx.Schema.GossipPage.Menus.Table).Any(r => r.U("MenuID") == menu) && !snapshot!.Lookup.MenuExists(menu))
        {
            if (!await DBSource.Open(Connection()).MenuExistsAsync(menu, CancellationToken.None))
            {
                status = $"Not saved: gossip menu {menu} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Menus.Add(menu);
        }

        Commit("Change gossip menu", [Update(Ctx.Schema.Tables.Find("creature_template"),TemplateRow, TemplateRow.With("gossip_menu_id", menu))]);
    }

    TableSchema SpellClick => Ctx.Schema.Tables.Find("npc_spellclick_spells");

    void RenderSpellClickEditor(NpcInteraction v)
    {
        foreach (var click in v.SpellClicks)
        {
            var remove = new Button
            {
                Text = $"Remove {click.Name}"
            };
            remove.Pressed += () => Commit($"Remove spell click {click.SpellId}", [Delete(SpellClick,click.Row)]);
            body.AddChild(remove);
        }

        SearchPicker("click", "Add spell click: spell name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add spell click {s.Id}", [Insert(SpellClick,Row.Of(("npc_entry", v.Entry), ("spell_id", s.Id), ("cast_flags", 1u), ("user_type", 0u)))])))).ToList()));
    }
}
