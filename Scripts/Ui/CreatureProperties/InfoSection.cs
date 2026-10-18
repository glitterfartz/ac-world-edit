using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class InfoSection(CreatureContext ctx) : InteractionSection(ctx)
{
    public override string Id => "info";
    public override string Title => "Info";

    protected override void Populate(NpcInteraction v)
    {
        RenderInfo(v);
    }

    void RenderInfo(NpcInteraction v)
    {
        body.AddChild(ClientStyle.Text("NPC flags", 16, ClientStyle.Header));
        body.AddChild(ClientStyle.Text(v.NpcFlag == 0 ? "None (right-click does nothing)" : string.Join(", ", NpcFlags.Names(v.NpcFlag)) + $"   (npcflag 0x{v.NpcFlag:X})", 14));
        if (editing)
        {
            RenderTemplateEditor(v);
        }

        if ((v.NpcFlag & NpcFlags.FlightMaster) != 0 && catalog != null && target != null && catalog.NearestTaxiNode(target.Pose.Map, target.Pose.X, target.Pose.Y, target.Pose.Z) is { } node)
        {
            body.AddChild(ClientStyle.Text($"Flight master: nearest taxi node {node.Name} ({node.Id}), {node.Distance:0} yd.", 14));
        }

        body.AddChild(ClientStyle.Text("Spell click", 16, ClientStyle.Header));
        if (v.SpellClicks.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var click in v.SpellClicks)
        {
            body.AddChild(ClientStyle.Text($"{click.Name}   ·   cast flags {click.CastFlags}   ·   user type {click.UserType}", 14, click.Edited ? ClientStyle.Edited : null));
        }

        if (editing)
        {
            RenderSpellClickEditor(v);
        }

        body.AddChild(ClientStyle.Text("Scripted behaviour", 16, ClientStyle.Header));
        if (v.ScriptName.Length > 0)
        {
            body.AddChild(ClientStyle.Text($"C++ script {v.ScriptName} may replace gossip or quest handling entirely.", 14, ClientStyle.Warning));
        }

        if (v.AIName.Length > 0)
        {
            body.AddChild(ClientStyle.Text($"AI: {v.AIName}", 14));
        }

        foreach (var hook in v.Scripts)
        {
            string name = hook.EventType switch
            {
                64 => "gossip hello",
                62 => $"gossip select (menu {hook.EventParam1}, option {hook.EventParam2})",
                19 => "quest accepted",
                20 => "quest rewarded",
                _ => $"event {hook.EventType}"
            };
            body.AddChild(ClientStyle.Text($"SmartAI on {name} → action {hook.ActionType}{(hook.Comment.Length > 0 ? $"  ({hook.Comment})" : "")}", 13, ClientStyle.Warning));
        }

        if (v.ScriptName.Length == 0 && v.Scripts.Length == 0)
        {
            body.AddChild(ClientStyle.Text("No interaction scripts found.", 13, ClientStyle.Faded));
        }

        RenderIssues(v);
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

    void RenderTemplateEditor(NpcInteraction v)
    {
        var template = TemplateRow;
        body.AddChild(ClientStyle.Text($"creature_template {v.Entry} applies to all {v.SpawnCount} spawn(s) of this entry.", 11, ClientStyle.Warning));
        var grid = new GridContainer
        {
            Columns = 3
        };
        body.AddChild(grid);
        foreach (var (bit, name) in NpcFlags.All)
        {
            var check = new CheckBox
            {
                Text = name,
                ButtonPressed = (v.NpcFlag & bit) != 0
            };
            check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {name} flag", [Update(InteractionTables.CreatureTemplate, template, template.With("npcflag", on ? template.U("npcflag") | bit : template.U("npcflag") & ~bit))]);
            grid.AddChild(check);
        }

        var line = new HBoxContainer();
        body.AddChild(line);
        line.AddChild(ClientStyle.Tag("Subname"));
        var subname = new LineEdit
        {
            Text = v.Subname,
            MaxLength = 100,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = "<Title under the name>"
        };
        subname.TextSubmitted += text => Commit("Edit subname", [Update(InteractionTables.CreatureTemplate, template, template.With("subname", text.Trim()))]);
        line.AddChild(subname);
        line.AddChild(ClientStyle.Tag("Gossip menu"));
        var menu = new LineEdit
        {
            Text = v.GossipMenuId.ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(70, 0)
        };
        menu.TextSubmitted += text => Try(() => _ = SetRootMenu(ParseUInt(text)));
        line.AddChild(menu);
    }

    async Task SetRootMenu(uint menu)
    {
        if (menu != 0 && !effective.All(InteractionTables.GossipMenu).Any(r => r.U("MenuID") == menu) && !snapshot!.Lookup.MenuExists(menu))
        {
            if (!await new InteractionSource(Connection()).MenuExistsAsync(menu, CancellationToken.None))
            {
                status = $"Not saved: gossip menu {menu} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Menus.Add(menu);
        }

        Commit("Change gossip menu", [Update(InteractionTables.CreatureTemplate, TemplateRow, TemplateRow.With("gossip_menu_id", menu))]);
    }

    void RenderSpellClickEditor(NpcInteraction v)
    {
        foreach (var click in v.SpellClicks)
        {
            var remove = new Button
            {
                Text = $"Remove {click.Name}"
            };
            remove.Pressed += () => Commit($"Remove spell click {click.SpellId}", [Delete(InteractionTables.SpellClick, click.Row)]);
            body.AddChild(remove);
        }

        SearchPicker("click", "Add spell click: spell name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add spell click {s.Id}", [Insert(InteractionTables.SpellClick, Row.Of(("npc_entry", v.Entry), ("spell_id", s.Id), ("cast_flags", 1u), ("user_type", 0u)))])))).ToList()));
    }
}
