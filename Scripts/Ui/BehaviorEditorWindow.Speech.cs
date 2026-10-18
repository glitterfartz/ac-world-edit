using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Speech page over creature_text: lines grouped the way "Say text" picks them (one random line of
/// a group, weighted by chance). Rules follow CreatureTextMgr::LoadCreatureTexts: a valid
/// BroadcastTextId replaces the line's own text, so such lines show that clearly.
/// </summary>
public partial class BehaviorEditorWindow
{
    /// <summary>
    /// ChatMsg values creatures use (SharedDefines.h).
    /// </summary>
    static readonly (uint Value, string Name)[] SpeechTypes = [(12, "Says"), (14, "Yells"), (16, "Emote text"), (15, "Whispers"), (41, "Boss emote"), (42, "Boss whisper"), (13, "Party")];
    static readonly (uint Value, string Name)[] Languages = [(0, "Universal"), (7, "Common"), (1, "Orcish"), (2, "Darnassian"), (3, "Taurahe"), (6, "Dwarvish"), (10, "Thalassian"), (11, "Draconic"), (13, "Gnomish"), (14, "Troll"), (33, "Gutterspeak"), (35, "Draenei"), (8, "Demonic"), (9, "Titan")];
    static readonly (uint Value, string Name)[] Ranges = [(0, "Nearby"), (1, "Area"), (2, "Zone"), (3, "Map"), (4, "World")];

    void RenderSpeech()
    {
        var help = ClientStyle.Text("\"Say text\" actions pick one line of a group at random (weighted by chance). Use $n for the player's name.", 12, ClientStyle.Faded);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(help);
        var lines = effective.All(InteractionTables.CreatureText).Where(t => t.U("CreatureID") == entry).ToList();
        var allRules = Scopes(effective).SelectMany(s => RulesOf(effective, s)).ToList();
        foreach (var group in lines.GroupBy(t => t.U("GroupID")).OrderBy(g => g.Key))
        {
            int users = allRules.Count(r => r.Action == SmartCatalog.Talk && r.ActionParam(1) == group.Key);
            page.AddChild(ClientStyle.Text($"Group {group.Key}{(users == 0 ? " · not used by any rule" : $" · used by {users} rule(s)")}", 16, ClientStyle.Header));
            foreach (var line in group.OrderBy(t => t.U("ID")))
            {
                RenderLine(line);
            }

            uint groupId = group.Key;
            page.AddChild(ButtonOf("+ Add line to this group", () => AddLine(groupId)));
        }

        if (lines.Count == 0)
        {
            page.AddChild(ClientStyle.Text("This creature has no lines yet.", 14, ClientStyle.Faded));
        }

        uint nextGroup = SpeechEdits.NextGroup(effective, entry);
        var add = ButtonOf("+ New group", () => AddLine(nextGroup));
        add.Name = "AddSpeechGroup";
        page.AddChild(add);
    }

    void RenderLine(Row line)
    {
        var table = InteractionTables.CreatureText;
        bool edited = snapshot!.Rows.Get(table, table.KeyOf(line)) != line;
        void Put(string label, params (string Column, object Value)[] values)
        {
            if (SpeechEdits.Change(effective, line, values) is { } edit)
            {
                Commit(label, edit);
            }
        }

        var text = EditorFields.Line(line.T("Text"), value => Put("Edit line", ("Text", value)), "What the creature says");
        if (edited)
        {
            text.AddThemeColorOverride("font_color", ClientStyle.Edited);
        }

        page.AddChild(EditorFields.Row($"Line {line.U("ID")}", text, ButtonOf("Remove", () => Commit("Remove line", SpeechEdits.Remove(effective.Get(table, table.KeyOf(line)) ?? line)))));
        var emotes = (catalog?.All("Emote") ?? []).Where(e => e.Name.Length > 0).Prepend((0u, "none")).DistinctBy(e => e.Item1).ToList();
        page.AddChild(EditorFields.Row("",
            EditorFields.Choice(SpeechTypes, line.U("Type"), value => Put("Change how the line is said", ("Type", value))),
            ClientStyle.Tag("in"), EditorFields.Choice(Languages, (uint)Math.Max(0, line.I("Language")), value => Put("Change language", ("Language", (int)value))),
            ClientStyle.Tag("heard"), EditorFields.Choice(Ranges, line.U("TextRange"), value => Put("Change range", ("TextRange", value))),
            ClientStyle.Tag("chance"), EditorFields.Decimal(line.F("Probability"), value => Put("Change line chance", ("Probability", (float)Math.Max(0, value))), 50), ClientStyle.Tag("%"),
            ClientStyle.Tag("gesture"), EditorFields.Choice(emotes, line.U("Emote"), value => Put("Change line gesture", ("Emote", value)))));
        int broadcast = line.I("BroadcastTextId");
        string shown = broadcast <= 0 ? "none (the line's own text is used)"
            : snapshot!.Names.Broadcasts.TryGetValue((uint)broadcast, out var preview) ? $"#{broadcast}: {Shorten(preview)}" : $"#{broadcast}";
        var reference = EditorFields.Reference(shown, () => ChooseBroadcast(line, id => Put("Link broadcast text", ("BroadcastTextId", (int)id))), broadcast > 0 ? () => Put("Unlink broadcast text", ("BroadcastTextId", 0)) : null);
        reference.Name = "BroadcastReference";
        page.AddChild(EditorFields.Row("Broadcast text", reference));
        if (broadcast > 0)
        {
            page.AddChild(EditorFields.Row("", ClientStyle.Text("The server shows this (localized) text instead of the line above.", 11, ClientStyle.Warning)));
        }
    }

    void ChooseBroadcast(Row line, Action<uint> choose)
    {
        var source = new ContentSource(Connection());
        PickerPopup.Show(this, "Choose broadcast text", async term =>
        {
            var found = await source.SearchBroadcastAsync(term, CancellationToken.None);
            foreach (var (id, name) in found)
            {
                snapshot!.Names.Broadcasts[id] = name;
            }

            return found.Select(f => (f.Id, $"{f.Id}: {Shorten(f.Name)}")).ToList();
        }, (id, _) => choose(id), current: (uint)Math.Max(0, line.I("BroadcastTextId")));
    }

    void AddLine(uint group)
    {
        var (label, edit) = SpeechEdits.NewLine(effective, entry, group, snapshot!.CreatureName);
        Commit(label, edit);
    }
}
