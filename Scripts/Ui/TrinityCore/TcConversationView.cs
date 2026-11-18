using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The conversation editor: the template card (first line, texture kit, flags, script), one card per
/// actor with its kind, and the client line chain as a strip of cards with the server override
/// editor below. Every change commits one labelled command through the panel.
/// </summary>
public partial class TcConversationView : VBoxContainer
{
    public Func<string, RowEdit[], bool> Commit { get; set; } = (_, _) => false;
    public Action NewRequested { get; set; } = () => { };
    public Action DuplicateRequested { get; set; } = () => { };
    public Action FirstLinePick { get; set; } = () => { };

    public int ActorCount { get; private set; }
    public int LineCount { get; private set; }
    public uint? SelectedLineId { get; private set; }
    /// <summary>"Playing 2 of 5" while the play preview steps the chain.</summary>
    public string PlayingText { get; private set; } = "";

    static readonly (string Column, uint Bit, string Label)[] Flags =
    [
        ("MultipleConversationType", 0x01, "Multiple conversation type"),
        ("IsTalkingHeadConversation", 0x02, "Talking head"),
        ("AllowWithoutSpawnedActor", 0x04, "Allow without a spawned actor")
    ];

    static readonly (string Name, TcConversationRules.ActorKind Kind)[] Kinds =
    [
        ("Creature", TcConversationRules.ActorKind.Creature),
        ("Spawn", TcConversationRules.ActorKind.Spawn),
        ("Talking head", TcConversationRules.ActorKind.NoObject),
        ("Player", TcConversationRules.ActorKind.ActivePlayer)
    ];

    ConversationSnapshot? snapshot;
    RowSet? rows;
    uint conversationId;
    int playIndex = -1;

    public TcConversationView()
    {
        AddThemeConstantOverride("separation", 8);
    }

    public void Show(ConversationSnapshot? view)
    {
        snapshot = view;
        rows = view?.Rows;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        ActorCount = 0;
        LineCount = 0;
        if (view == null || rows?.All(TcTables.ConversationTemplate).FirstOrDefault() is not { } template)
        {
            SelectedLineId = null;
            PlayingText = "";
            AddChild(ClientStyle.Text("No conversation open.", 14, ClientStyle.Faded));
            return;
        }

        conversationId = template.U("Id");
        if (SelectedLineId is uint selected && view.Chain.All(line => line.Id != selected))
        {
            SelectedLineId = null;
        }

        RenderTemplate(template, view);
        RenderActors(view);
        RenderLines(view);
    }

    public string LineText(int index) => snapshot != null && index >= 0 && index < snapshot.Chain.Count ? snapshot.Chain[index].Text : "";

    void RenderTemplate(Row template, ConversationSnapshot view)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var panel = new PanelContainer();
        panel.AddChild(box);
        AddChild(panel);
        box.AddChild(ClientStyle.Text($"Conversation {conversationId}", 20, ClientStyle.Header));

        uint first = template.U("FirstLineId");
        string firstText = view.Chain.FirstOrDefault(line => line.Id == first)?.Text ?? "";
        box.AddChild(EditorFields.Row("First line", "The client line where the conversation starts.",
            EditorFields.Reference($"Line {first}{(firstText.Length > 0 ? $" · {Shorten(firstText, 60)}" : "")}", () => FirstLinePick(), null, first == 0,
                "The client line the conversation starts from."),
            EditorFields.Number(first, value => SetTemplate(template, "Change first line", ("FirstLineId", (uint)Math.Max(value, 0))), 90, "The client line the conversation starts from.")));

        var meta = new HBoxContainer();
        meta.AddThemeConstantOverride("separation", 10);
        box.AddChild(meta);
        meta.AddChild(EditorFields.Row("Texture kit", "The client UI texture kit of the conversation frame.",
            EditorFields.Number(template.U("TextureKitId"), value => SetTemplate(template, "Change texture kit", ("TextureKitId", (uint)Math.Max(value, 0))), 80, "The client UI texture kit.")));
        meta.AddChild(EditorFields.Row("Script", "The server script that runs while the conversation plays.",
            EditorFields.Line(template.T("ScriptName"), text => SetTemplate(template, "Change conversation script", ("ScriptName", text.Trim())), "ScriptName", 64, "The server script for this conversation.")));

        var flags = new HBoxContainer();
        flags.AddThemeConstantOverride("separation", 10);
        box.AddChild(flags);
        foreach (var (column, bit, label) in Flags)
        {
            var before = template;
            var check = new CheckBox
            {
                Name = $"Flag_{column}",
                Text = label,
                ButtonPressed = (before.I("Flags") & bit) != 0,
                TooltipText = $"The {label.ToLowerInvariant()} flag of the conversation."
            };
            check.Toggled += on => SetTemplate(before, $"Change {label.ToLowerInvariant()}",
                ("Flags", on ? before.I("Flags") | bit : before.I("Flags") & ~bit));
            flags.AddChild(check);
        }

        if (!view.ClientAvailable)
        {
            var note = ClientStyle.Text("The retail client is unavailable, so the line chain is not shown; the override rows can still be edited.", 12, ClientStyle.Warning);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(note);
        }

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        AddChild(actions);
        var create = ContentEditorWindow.ButtonIn(actions, "+ New conversation", () => NewRequested());
        create.Name = "NewConversation";
        create.TooltipText = "Create an empty conversation with a new ID.";
        var duplicate = ContentEditorWindow.ButtonIn(actions, "Duplicate", () => DuplicateRequested());
        duplicate.Name = "DuplicateConversation";
        duplicate.TooltipText = "Copy this conversation and its actors under a new ID.";
    }

    void RenderActors(ConversationSnapshot view)
    {
        AddChild(ClientStyle.Text("Actors", 16, ClientStyle.Header));
        var strip = new HFlowContainer();
        strip.AddThemeConstantOverride("h_separation", 6);
        strip.AddThemeConstantOverride("v_separation", 6);
        AddChild(strip);
        var actors = rows!.All(TcTables.ConversationActors).Where(actor => actor.U("ConversationId") == conversationId).OrderBy(actor => actor.U("Idx")).ToList();
        foreach (var actor in actors)
        {
            uint idx = actor.U("Idx");
            ActorCount++;
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            var card = new PanelContainer { CustomMinimumSize = new(230, 0) };
            card.AddChild(box);
            strip.AddChild(card);

            var kind = new OptionButton { Name = $"ActorKind_{idx}", TooltipText = "What this actor is: a creature, an existing spawn, a talking head or the player." };
            foreach (var (name, _) in Kinds)
            {
                kind.AddItem(name);
            }

            kind.Selected = Array.FindIndex(Kinds, pair => pair.Kind == TcConversationRules.Kind(actor));
            var beforeKind = actor;
            kind.ItemSelected += index => SetKind(beforeKind, (int)index);
            box.AddChild(kind);

            box.AddChild(EditorFields.Row("Creature", "The creature template that stands on screen.",
                EditorFields.Number(actor.U("CreatureId"), value => SetActor(actor, "Change actor creature", ("CreatureId", (uint)Math.Max(value, 0))), 80, "The creature template that stands on screen."),
                ClientStyle.Text(view.Names.Creature(actor.U("CreatureId")) ?? "", 11, ClientStyle.Faded)));
            box.AddChild(EditorFields.Row("Display", "A display override for a talking head; 0 uses the creature's model.",
                EditorFields.Number(actor.U("CreatureDisplayInfoId"), value => SetActor(actor, "Change actor display", ("CreatureDisplayInfoId", (uint)Math.Max(value, 0))), 90, "A display override for a talking head.")));
            box.AddChild(EditorFields.Row("Spawn", "An existing creature spawn used as the actor; 0 uses the creature template.",
                EditorFields.Number((long)actor.UL("ConversationActorGuid"), value => SetActor(actor, "Change actor spawn", ("ConversationActorGuid", (ulong)Math.Max(value, 0))), 110, "An existing creature spawn used as the actor."),
                ClientStyle.Text(actor.UL("ConversationActorGuid") == 0 ? "" : view.Names.CreatureSpawnExists(actor.UL("ConversationActorGuid")) ? "exists" : "missing", 11, ClientStyle.Faded)));
            var remove = ContentEditorWindow.ButtonIn(box, "Remove actor", () => Commit("Remove actor", TcConversationRules.RemoveActor(actor, rows!)));
            remove.Name = $"RemoveActor_{idx}";
            remove.TooltipText = "Remove this actor and the line overrides that name it.";
        }

        var add = ContentEditorWindow.ButtonIn(this, "+ Actor", AddActor);
        add.Name = "AddActor";
        add.TooltipText = "Add another actor at the next index.";
    }

    void RenderLines(ConversationSnapshot view)
    {
        AddChild(ClientStyle.Text("Lines", 16, ClientStyle.Header));
        var strip = new HFlowContainer { Name = "Lines" };
        strip.AddThemeConstantOverride("h_separation", 6);
        strip.AddThemeConstantOverride("v_separation", 6);
        AddChild(strip);
        var actors = rows!.All(TcTables.ConversationActors).Where(actor => actor.U("ConversationId") == conversationId).ToDictionary(actor => actor.U("Idx"), actor => actor);
        for (int i = 0; i < view.Chain.Count; i++)
        {
            var line = view.Chain[i];
            LineCount++;
            var overridden = rows.Get(TcTables.ConversationLineTemplate, line.Id.ToString());
            string actor = overridden is { } row && actors.TryGetValue(row.U("ActorIdx"), out var who)
                ? view.Names.Creature(who.U("CreatureId")) ?? (who.UL("ConversationActorGuid") != 0 ? $"Spawn {who.UL("ConversationActorGuid")}" : $"Actor {row.U("ActorIdx")}")
                : "client defaults";
            var button = new Button
            {
                Name = $"Line_{line.Id}",
                Text = $"#{i + 1} · {actor}\n{Shorten(line.Text, 44)}",
                CustomMinimumSize = new(190, 76),
                TooltipText = $"{line.Text}\n\nLine {line.Id}{(line.AdditionalDuration != 0 ? $", +{line.AdditionalDuration} ms" : "")}{(overridden != null ? " · overridden" : " · client defaults")}",
                ToggleMode = true,
                ButtonPressed = SelectedLineId == line.Id
            };
            button.Pressed += () => SelectLine(line.Id);
            strip.AddChild(button);
        }

        var play = new HBoxContainer();
        play.AddThemeConstantOverride("separation", 8);
        AddChild(play);
        var start = ContentEditorWindow.ButtonIn(play, "Play", Play);
        start.Name = "Play";
        start.TooltipText = "Step through the line cards in order; nothing is spawned in the world.";
        if (PlayingText.Length > 0)
        {
            play.AddChild(ClientStyle.Text(PlayingText, 12, ClientStyle.Faded));
        }

        RenderOverride();
    }

    void RenderOverride()
    {
        if (snapshot == null || rows == null || SelectedLineId is not uint lineId)
        {
            AddChild(ClientStyle.Text("Select a line card to edit its override.", 12, ClientStyle.Faded));
            return;
        }

        var line = snapshot.Chain.FirstOrDefault(entry => entry.Id == lineId);
        var existing = rows.Get(TcTables.ConversationLineTemplate, lineId.ToString());
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        box.AddChild(ClientStyle.Text($"Line {lineId} · {(existing == null ? "client defaults" : "override")}", 15, ClientStyle.Header));
        if (line != null && line.Text.Length > 0)
        {
            var text = ClientStyle.Text(line.Text, 12, ClientStyle.Faded);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(text);
        }

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        box.AddChild(row);
        var actor = existing ?? TcConversationRules.NewLineOverride(lineId);
        row.AddChild(EditorFields.Row("Actor", "Which actor says this line.",
            EditorFields.Number(actor.U("ActorIdx"), value => SetOverride(existing, actor, "Change line actor", ("ActorIdx", (uint)Math.Max(value, 0))), 60, "Which actor says this line.")));
        row.AddChild(EditorFields.Row("Camera", "The client camera this line is shown from; 0 keeps the client's.",
            EditorFields.Number(actor.U("UiCameraID"), value => SetOverride(existing, actor, "Change line camera", ("UiCameraID", (uint)Math.Max(value, 0))), 90,
                "The client camera this line is shown from; 0 keeps the client's.")));
        row.AddChild(EditorFields.Row("Flags", "The line's server flags.", EditorFields.Number(actor.U("Flags"), value => SetOverride(existing, actor, "Change line flags", ("Flags", (uint)Math.Max(value, 0))), 60, "The line's server flags.")));
        row.AddChild(EditorFields.Row("Chat", "The chat bubble type; 0 is the client default.",
            EditorFields.Number(actor.U("ChatType"), value => SetOverride(existing, actor, "Change line chat type", ("ChatType", (uint)Math.Max(value, 0))), 60, "The chat bubble type; 0 is the client default.")));

        if (existing == null)
        {
            var add = ContentEditorWindow.ButtonIn(box, "Add override", AddOverride);
            add.Name = "AddOverride";
            add.TooltipText = "Insert an override row for this line.";
        }
        else
        {
            var remove = ContentEditorWindow.ButtonIn(box, "Remove override", RemoveOverride);
            remove.Name = "RemoveOverride";
            remove.TooltipText = "Delete this line's override row; the client's values apply again.";
        }
    }

    public void SelectLine(uint lineId)
    {
        SelectedLineId = lineId;
        Show(snapshot);
    }

    public void SetFirstLine(uint lineId)
    {
        if (rows == null)
        {
            return;
        }

        if (rows.Get(TcTables.ConversationTemplate, conversationId.ToString()) is { } template)
        {
            SetTemplate(template, "Change first line", ("FirstLineId", lineId));
        }
    }

    public void AddActor()
    {
        if (rows == null || rows.Get(TcTables.ConversationTemplate, conversationId.ToString()) == null)
        {
            return;
        }

        Commit("Add actor", [InteractionEdits.Insert(TcTables.ConversationActors, TcConversationRules.NewActor(conversationId, TcConversationRules.NextActorIdx(rows, conversationId)))]);
    }

    public void AddOverride()
    {
        if (rows == null || SelectedLineId is not uint lineId || rows.Get(TcTables.ConversationLineTemplate, lineId.ToString()) != null)
        {
            return;
        }

        Commit("Add line override", [InteractionEdits.Insert(TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(lineId))]);
    }

    public void RemoveOverride()
    {
        if (rows == null || SelectedLineId is not uint lineId || rows.Get(TcTables.ConversationLineTemplate, lineId.ToString()) is not { } existing)
        {
            return;
        }

        Commit("Remove line override", [InteractionEdits.Delete(TcTables.ConversationLineTemplate, existing)]);
    }

    public void Play()
    {
        if (snapshot == null || snapshot.Chain.Count == 0)
        {
            return;
        }

        playIndex = playIndex < 0 || playIndex + 1 >= snapshot.Chain.Count ? 0 : playIndex + 1;
        SelectedLineId = snapshot.Chain[playIndex].Id;
        PlayingText = $"Playing {playIndex + 1} of {snapshot.Chain.Count}";
        Show(snapshot);
    }

    void SetKind(Row actor, int kindIndex)
    {
        var kind = Kinds[Math.Clamp(kindIndex, 0, Kinds.Length - 1)].Kind;
        var after = actor.With("NoActorObject", kind == TcConversationRules.ActorKind.NoObject ? 1u : 0u)
            .With("ActivePlayerObject", kind == TcConversationRules.ActorKind.ActivePlayer ? 1u : 0u);
        if (kind != TcConversationRules.ActorKind.Spawn)
        {
            after = after.With("ConversationActorGuid", 0UL);
        }

        SetRow(TcTables.ConversationActors, actor, after, "Change actor kind");
    }

    void SetOverride(Row? before, Row basis, string label, params (string Column, object Value)[] values)
    {
        var after = basis;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        if (before == null)
        {
            after = after.With("Id", SelectedLineId!.Value);
        }

        SetRow(TcTables.ConversationLineTemplate, before, after, label);
    }

    void SetTemplate(Row before, string label, params (string Column, object Value)[] values)
    {
        var after = before;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        SetRow(TcTables.ConversationTemplate, before, after, label);
    }

    void SetActor(Row before, string label, params (string Column, object Value)[] values)
    {
        var after = before;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        SetRow(TcTables.ConversationActors, before, after, label);
    }

    void SetRow(TableSchema table, Row? before, Row after, string label)
    {
        if (before == after)
        {
            return;
        }

        Commit(label, before == null ? [InteractionEdits.Insert(table, after)] : ItemListSpec.Change(table, before, after));
    }

    static string Shorten(string text, int length) => text.Length <= length ? text : text[..Math.Max(0, length - 1)].TrimEnd() + "…";

}
