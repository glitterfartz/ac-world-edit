using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The speech page over creature_text: lines grouped as &quot;Say text&quot; picks them.</summary>
public partial class BehaviorPanel
{
    // Speech page over creature_text: lines grouped the way "Say text" picks them (one random line
    // of a group, weighted by chance). Rules follow CreatureTextMgr::LoadCreatureTexts: a valid
    // BroadcastTextId replaces the line's own text, so such lines show that clearly.

    /// <summary>
    /// ChatMsg values creatures use (SharedDefines.h).
    /// </summary>
    static readonly (uint Value, string Name)[] SpeechTypes = [(12, "Says"), (14, "Yells"), (16, "Emote text"), (15, "Whispers"), (41, "Boss emote"), (42, "Boss whisper"), (13, "Party")];
    static readonly (uint Value, string Name)[] Languages = [(0, "Universal"), (7, "Common"), (1, "Orcish"), (2, "Darnassian"), (3, "Taurahe"), (6, "Dwarvish"), (10, "Thalassian"), (11, "Draconic"), (13, "Gnomish"), (14, "Troll"), (33, "Gutterspeak"), (35, "Draenei"), (8, "Demonic"), (9, "Titan")];
    static readonly (uint Value, string Name)[] Ranges = [(0, "Nearby"), (1, "Area"), (2, "Zone"), (3, "Map"), (4, "World")];

    void RenderSpeech()
    {
        var help = ClientStyle.Text("Each card is one thing the creature can say. A group is a set of alternative lines; the server picks one, weighted by chance. SmartAI \"Say text\" actions choose a group by its number.", 12, ClientStyle.Faded);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(help);
        var lines = effective.All(smart.CreatureText).Where(t => t.U("CreatureID") == entry).ToList();
        var allRules = Scopes(effective).SelectMany(s => RulesOf(effective, s)).ToList();
        foreach (var group in lines.GroupBy(t => t.U("GroupID")).OrderBy(g => g.Key))
        {
            int users = allRules.Count(r => r.Action == SmartCatalog.Talk && r.ActionParam(1) == group.Key);
            var header = new HBoxContainer();
            header.AddChild(ClientStyle.Text($"Group {group.Key}", 16, ClientStyle.Header));
            header.AddChild(ClientStyle.Tag(users == 0 ? "not used by any rule" : $"used by {users} rule(s)"));
            page.AddChild(header);
            foreach (var line in group.OrderBy(t => t.U("ID")))
            {
                RenderLine(line);
            }

            uint groupId = group.Key;
            var addLine = ButtonOf("+ Add another line", () => AddLine(groupId));
            addLine.TooltipText = "Add another alternative to this group; the server picks one line at random, weighted by chance.";
            page.AddChild(addLine);
        }

        if (lines.Count == 0)
        {
            page.AddChild(ClientStyle.Text("This creature has no lines yet. Create a group, then add the lines it can say.", 14, ClientStyle.Faded));
        }

        uint nextGroup = SpeechEdits.NextGroup(smart, effective, entry);
        var add = ButtonOf("+ New speech group", () => AddLine(nextGroup));
        add.Name = "AddSpeechGroup";
        add.TooltipText = "Start a new text group; \"Say text\" actions pick a line from one group.";
        page.AddChild(add);
    }

    void RenderLine(Row line)
    {
        var table = smart.CreatureText;
        bool edited = snapshot!.Rows.Get(table, table.KeyOf(line)) != line;
        void Put(string label, params (string Column, object Value)[] values)
        {
            if (SpeechEdits.Change(smart, effective, line, values) is { } edit)
            {
                Commit(label, edit);
            }
        }

        var text = EditorFields.Line(line.T("Text"), value => Put("Edit line", ("Text", value)), "What the creature says", 0, "The line the creature says. $n is replaced by the player's name. A linked broadcast text overrides it.");
        if (edited)
        {
            text.AddThemeColorOverride("font_color", ClientStyle.Edited);
        }

        var remove = ButtonOf("Remove", () => Commit("Remove line", SpeechEdits.Remove(smart, effective.Get(table, table.KeyOf(line)) ?? line)));
        remove.TooltipText = "Delete this speech line.";
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 4);
        page.AddChild(card);
        var header = new HBoxContainer();
        card.AddChild(header);
        header.AddChild(ClientStyle.Text($"Line {line.U("ID")} · \"{Shorten(line.T("Text"))}\"", 13, ClientStyle.Gold));
        header.AddChild(ClientStyle.Tag(SpeechTypes.FirstOrDefault(t => t.Value == line.U("Type")).Name ?? "Says"));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);
        header.AddChild(remove);
        card.AddChild(EditorFields.Row("Text", "The line the creature says, plus how and where it is heard.", text));
        var emotes = (catalog?.All("Emote") ?? []).Where(e => e.Name.Length > 0).Prepend((0u, "none")).DistinctBy(e => e.Item1).ToList();
        card.AddChild(EditorFields.Row("Delivery", "How this line is said, the language and range it is heard in, its weight when the group is picked, and its gesture.",
            EditorFields.Choice(SpeechTypes, line.U("Type"), value => Put("Change how the line is said", ("Type", value)), "How the line is said: say, yell, emote text, whisper and so on."),
            ClientStyle.Tag("in"), EditorFields.Choice(Languages, (uint)Math.Max(0, line.I("Language")), value => Put("Change language", ("Language", (int)value)), "The language the line is spoken in."),
            ClientStyle.Tag("heard"), EditorFields.Choice(Ranges, line.U("TextRange"), value => Put("Change range", ("TextRange", value)), "How far the line is heard: nearby, area, zone, map or world."),
            ClientStyle.Tag("weight"), EditorFields.Decimal(line.F("Probability"), value => Put("Change line weight", ("Probability", (float)Math.Max(0, value))), 50, "The relative weight of this line when the server picks one from its group; higher is more likely."),
            ClientStyle.Tag("gesture"), EditorFields.Choice(emotes, line.U("Emote"), value => Put("Change line gesture", ("Emote", value)), "The emote the creature plays while saying this line.")));
        if (smart.HasStringParams)
        {
            // TrinityCore: the sound plays to everyone in range, or from the creature itself (SoundKitPlayType).
            (uint, string)[] playTypes = [(0, "Normal"), (1, "From the creature")];
            var sound = EditorFields.Number(line.U("Sound"), value => Put("Change line sound", ("Sound", (uint)Math.Clamp(value, 0, uint.MaxValue))), 80, "Sound kit ID played with the line; 0 means no sound.");
            sound.Name = "LineSound";
            card.AddChild(EditorFields.Row("Sound", "The sound kit played with the line and whether it plays from the area or from the creature.",
                sound, ClientStyle.Tag("played"), EditorFields.Choice(playTypes, line.U("SoundPlayType"), value => Put("Change how the sound plays", ("SoundPlayType", value)), "Normal plays to everyone in range; From the creature plays from the creature itself.")));
        }

        int broadcast = line.I("BroadcastTextId");
        string shown = broadcast <= 0 ? "none (the line's own text is used)"
            : snapshot!.Names.Broadcasts.TryGetValue((uint)broadcast, out var preview) ? $"#{broadcast}: {Shorten(preview)}" : $"#{broadcast}";
        var reference = EditorFields.Reference(shown, () => ChooseBroadcast(line, id => Put("Link broadcast text", ("BroadcastTextId", (int)id))), broadcast > 0 ? () => Put("Unlink broadcast text", ("BroadcastTextId", 0)) : null, false, "A BroadcastText ID whose localized text replaces the line above. 0 uses the literal text.");
        reference.Name = "BroadcastReference";
        card.AddChild(EditorFields.Row("Broadcast text", "A BroadcastText ID whose localized text replaces the line above when set.", reference));
        if (broadcast > 0)
        {
            card.AddChild(EditorFields.Row("", ClientStyle.Text("The server shows this (localized) text instead of the line above.", 11, ClientStyle.Warning)));
        }

        card.AddChild(new HSeparator());
    }

    void ChooseBroadcast(Row line, Action<uint> choose)
    {
        var source = DBSource.Open(Connection(), Lookup());
        RecordPicker.Show(this, "Choose broadcast text", async term =>
        {
            var found = await source.SearchBroadcastAsync(term, CancellationToken.None);
            foreach (var (id, name) in found)
            {
                snapshot!.Names.Broadcasts[id] = name;
            }

            return found.Select(f => (f.Id, $"{f.Id}: {Shorten(f.Name)}")).ToList();
        }, (id, _) => choose(id), current: (uint)Math.Max(0, line.I("BroadcastTextId")), store: Store);
    }

    void AddLine(uint group)
    {
        var (label, edit) = SpeechEdits.NewLine(smart, effective, entry, group, snapshot!.CreatureName);
        Commit(label, edit);
    }
}
