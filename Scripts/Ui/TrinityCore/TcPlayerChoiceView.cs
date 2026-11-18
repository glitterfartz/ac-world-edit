using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The player-choice cards: the question card with its flags, one card per response with the reward
/// it grants, and an inline editor for the selected response and its single reward row. Every change
/// commits one labelled command through the panel; the panel re-renders from the draft afterwards.
/// </summary>
public partial class TcPlayerChoiceView : VBoxContainer
{
    public Func<string, RowEdit[], bool> Commit { get; set; } = (_, _) => false;
    public Action NewRequested { get; set; } = () => { };
    public Action DuplicateRequested { get; set; } = () => { };

    /// <summary>How many response cards the last redraw made.</summary>
    public int CardCount { get; private set; }
    /// <summary>The card positions the last redraw drew, in card order.</summary>
    public int[] Positions { get; private set; } = [];
    /// <summary>The question shown on the top card (the pending text when the question is unset).</summary>
    public string QuestionText { get; private set; } = "";
    public uint? SelectedResponseId { get; private set; }

    static readonly (string Column, string Name)[] Toggles =
    [
        ("HideWarboardHeader", "Hide warboard header"),
        ("KeepOpenAfterChoice", "Keep open after the choice"),
        ("ShowChoicesAsList", "List layout"),
        ("RequiresSelection", "Selection required"),
        ("ShowChoicesAsGrid", "Grid layout"),
        ("HideAnswerArt", "Hide answer art"),
        ("ShowChoicesAsColumns", "Columns layout"),
        ("InfiniteRange", "Infinite range")
    ];

    RowSet? rows;
    ContentNames? names;
    IReadOnlyList<InteractionIssue> lastIssues = [];
    uint choiceId;

    public TcPlayerChoiceView()
    {
        AddThemeConstantOverride("separation", 8);
    }

    public void Show(RowSet? view, ContentNames? lookup, IReadOnlyList<InteractionIssue> issues)
    {
        rows = view;
        names = lookup;
        lastIssues = issues;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        CardCount = 0;
        Positions = [];
        QuestionText = "";
        if (view?.All(TcTables.PlayerChoice).FirstOrDefault() is not { } card)
        {
            SelectedResponseId = null;
            RenderActions(card: false);
            AddChild(ClientStyle.Text("No player choice open.", 14, ClientStyle.Faded));
            return;
        }

        choiceId = card.U("ChoiceId");
        if (SelectedResponseId is uint selected && TcCinematicsRules.Responses(view, choiceId).All(r => r.U("ResponseId") != selected))
        {
            SelectedResponseId = null;
        }

        RenderCard(card);
        RenderActions(card: true);
        RenderStrip();
        RenderEditor();
    }

    void RenderCard(Row card)
    {
        QuestionText = card.IsNull("Question") || card.T("Question").Trim().Length == 0 ? card.T("PendingChoiceText") : card.T("Question");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var panel = new PanelContainer();
        panel.AddChild(box);
        AddChild(panel);

        var question = ClientStyle.Text(QuestionText, 20, ClientStyle.Header);
        question.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(question);
        if (card.T("PendingChoiceText").Length > 0)
        {
            box.AddChild(ClientStyle.Text(card.T("PendingChoiceText"), 12, ClientStyle.Faded));
        }

        var texts = new HBoxContainer();
        texts.AddThemeConstantOverride("separation", 10);
        box.AddChild(texts);
        var questionEdit = EditorFields.Line(card.IsNull("Question") ? "" : card.T("Question"), text => SetRow(TcTables.PlayerChoice, card,
            card.With("Question", text.Trim().Length == 0 ? (object?)null : text.Trim()), "Change question"), "Question", 255,
            "The question the card asks; empty uses the pending choice text.");
        questionEdit.Name = "Question";
        texts.AddChild(EditorFields.Row("Question", "The question the card asks; empty uses the pending choice text.", questionEdit));
        var pending = EditorFields.Line(card.T("PendingChoiceText"), text => SetRow(TcTables.PlayerChoice, card,
            card.With("PendingChoiceText", text), "Change pending choice text"), "PendingChoiceText", 255,
            "The text shown while the choice is pending.");
        pending.Name = "PendingChoiceText";
        texts.AddChild(EditorFields.Row("Pending text", "The text shown while the choice is pending.", pending));

        var meta = new HBoxContainer();
        meta.AddThemeConstantOverride("separation", 8);
        box.AddChild(meta);
        meta.AddChild(ClientStyle.Tag($"Texture kit {card.I("UiTextureKitId")}"));
        meta.AddChild(ClientStyle.Tag(card.IsNull("Duration") ? "Timing: client default" : $"Timing: {card.L("Duration") / 1000.0:0.#} s"));
        meta.AddChild(ClientStyle.Tag($"{TcCinematicsRules.Responses(rows!, choiceId).Count()} responses"));

        var flags = new HBoxContainer();
        flags.AddThemeConstantOverride("separation", 10);
        box.AddChild(flags);
        foreach (var (column, label) in Toggles)
        {
            var before = card;
            var check = new CheckBox
            {
                Text = label,
                ButtonPressed = before.U(column) != 0,
                TooltipText = $"The {label.ToLowerInvariant()} setting of this choice card."
            };
            check.Toggled += on => SetRow(TcTables.PlayerChoice, before, before.With(column, on ? 1u : 0u), $"Change {label.ToLowerInvariant()}");
            flags.AddChild(check);
        }

        var numbers = new HBoxContainer();
        numbers.AddThemeConstantOverride("separation", 10);
        box.AddChild(numbers);
        numbers.AddChild(EditorFields.Row("Max responses", "How many answers the card shows; 0 shows every response.",
            EditorFields.Number(card.IsNull("MaxResponses") ? 0 : card.U("MaxResponses"), value => SetRow(TcTables.PlayerChoice, card,
                card.With("MaxResponses", value <= 0 ? (object?)null : (uint)value), "Change maximum responses"), 70, "0 shows every response.")));
        numbers.AddChild(EditorFields.Row("Script", "The server script that runs when the choice is made.",
            EditorFields.Line(card.IsNull("ScriptName") ? "" : card.T("ScriptName"), text => SetRow(TcTables.PlayerChoice, card,
                card.With("ScriptName", text.Trim().Length == 0 ? (object?)null : text.Trim()), "Change choice script"), "ScriptName", 64, "The server script for this choice.")));
    }

    void RenderActions(bool card)
    {
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        AddChild(actions);
        var create = ContentEditorWindow.ButtonIn(actions, "+ New choice", () => NewRequested());
        create.Name = "NewChoice";
        create.TooltipText = "Create an empty player choice with a new ID.";
        var duplicate = ContentEditorWindow.ButtonIn(actions, "Duplicate", () => DuplicateRequested());
        duplicate.Name = "DuplicateChoice";
        duplicate.Disabled = !card;
        duplicate.TooltipText = "Copy this choice, its answers and their rewards under a new ID.";
        var add = ContentEditorWindow.ButtonIn(actions, "+ Response", AddResponse);
        add.Name = "AddResponse";
        add.Disabled = !card;
        add.TooltipText = "Add another answer card at the end.";
        var remove = ContentEditorWindow.ButtonIn(actions, "Remove response", RemoveSelected);
        remove.Name = "RemoveResponse";
        remove.Disabled = !card || SelectedResponseId == null;
        remove.TooltipText = "Remove the selected answer and its reward.";
    }

    void RenderStrip()
    {
        var strip = new HFlowContainer
        {
            Name = "ResponseCards"
        };
        strip.AddThemeConstantOverride("h_separation", 6);
        strip.AddThemeConstantOverride("v_separation", 6);
        AddChild(strip);
        foreach (var response in TcCinematicsRules.Responses(rows!, choiceId))
        {
            uint id = response.U("ResponseId");
            var button = new Button
            {
                Name = $"Response_{id}",
                Text = $"{response.T("Header")}\n{response.T("Answer")}\n{RewardSummary(id)}",
                CustomMinimumSize = new(170, 86),
                TooltipText = response.T("Description").Length > 0 ? response.T("Description") : $"Edit response {id}.",
                ToggleMode = true,
                ButtonPressed = SelectedResponseId == id
            };
            button.Pressed += () =>
            {
                SelectedResponseId = id;
                Show(rows, names, lastIssues);
            };
            strip.AddChild(button);
            CardCount++;
            Positions = [.. Positions, response.I("Index")];
        }
    }

    void RenderEditor()
    {
        if (SelectedResponseId is not uint id)
        {
            AddChild(ClientStyle.Text("Select an answer card to edit its text and reward.", 12, ClientStyle.Faded));
        }
        else if (TcCinematicsRules.Responses(rows!, choiceId).FirstOrDefault(r => r.U("ResponseId") == id) is { } response)
        {
            RenderResponseEditor(response);
            RenderConditions(id);
        }

        if (lastIssues.Count > 0)
        {
            AddChild(new HSeparator());
            foreach (var issue in lastIssues)
            {
                var label = ClientStyle.Text(issue.Message, 12, issue.Error ? ClientStyle.Warning : ClientStyle.Faded);
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                AddChild(label);
            }
        }
    }

    /// <summary>The source-36 conditions the server checks before offering this answer, read-only.</summary>
    void RenderConditions(uint responseId)
    {
        int index = 0;
        foreach (var row in TcCinematicsRules.Conditions(rows!, choiceId, responseId))
        {
            var condition = ClientStyle.Text(ConditionCatalog.Trinity.Describe(row.I("ConditionTypeOrReference"),
                row.L("ConditionValue1"), row.L("ConditionValue2"), row.L("ConditionValue3"), row.U("NegativeCondition") != 0), 12, ClientStyle.Faded);
            condition.Name = $"Condition_{responseId}_{index++}";
            condition.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            condition.TooltipText = "A condition the server checks before offering this answer. Conditions are read-only here.";
            AddChild(condition);
        }
    }

    void RenderResponseEditor(Row response)
    {
        uint id = response.U("ResponseId");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        box.AddChild(ClientStyle.Text($"Answer {id}", 16, ClientStyle.Header));
        var header = EditorFields.Line(response.T("Header"), text => SetRow(TcTables.PlayerChoiceResponse, response, response.With("Header", text), "Rename response header"), "Card title", 511, "The title on the answer card.");
        header.Name = "Header";
        box.AddChild(EditorFields.Row("Header", "The title on the answer card.", header));
        var answer = EditorFields.Line(response.T("Answer"), text => SetRow(TcTables.PlayerChoiceResponse, response, response.With("Answer", text), "Change response answer"), "Select", 511, "The button text the player clicks.");
        answer.Name = "Answer";
        box.AddChild(EditorFields.Row("Answer", "The button text the player clicks.", answer));
        box.AddChild(EditorFields.Row("Description", "The longer text under the button.",
            EditorFields.Area(response.T("Description"), text => SetRow(TcTables.PlayerChoiceResponse, response, response.With("Description", text), "Change response description"), 70, "The longer text under the button.")));
        box.AddChild(EditorFields.Row("Confirmation", "What the player is asked before the choice is final.",
            EditorFields.Line(response.T("Confirmation"), text => SetRow(TcTables.PlayerChoiceResponse, response, response.With("Confirmation", text), "Change response confirmation"), "", 127, "What the player is asked before the choice is final.")));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        box.AddChild(row);
        row.AddChild(EditorFields.Row("Position", "The order of the answer cards.",
            EditorFields.Number(response.U("Index"), value => SetRow(TcTables.PlayerChoiceResponse, response, response.With("Index", (uint)Math.Max(value, 0)), "Change response position"), 60, "The order of the answer cards.")));
        var quest = EditorFields.Number(response.IsNull("RewardQuestID") ? 0 : response.U("RewardQuestID"), value => SetRow(TcTables.PlayerChoiceResponse, response,
            response.With("RewardQuestID", value <= 0 ? (object?)null : (uint)value), "Change reward quest"), 80, "A quest this answer sends the player to; 0 for none.");
        quest.Name = $"RewardQuest_{id}";
        row.AddChild(EditorFields.Row("Reward quest", "A quest this answer sends the player to; 0 for none.", quest));
        row.AddChild(EditorFields.Row("Answer art", "The client texture file shown on the answer card; 0 for none.",
            EditorFields.Number(response.I("ChoiceArtFileId"), value => SetRow(TcTables.PlayerChoiceResponse, response,
                response.With("ChoiceArtFileId", (int)Math.Max(value, 0)), "Change answer art"), 90, "The client texture file shown on the answer card; 0 for none.")));

        RenderReward(id);
    }

    void RenderReward(uint responseId)
    {
        string key = $"{choiceId}|{responseId}";
        var reward = rows!.Get(TcTables.PlayerChoiceResponseReward, key);
        var item = TcCinematicsRules.RewardItems(rows, choiceId, responseId).FirstOrDefault();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        box.AddChild(ClientStyle.Text("Reward", 16, ClientStyle.Header));
        box.AddChild(ClientStyle.Text(RewardSummary(responseId), 12, ClientStyle.Faded));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        box.AddChild(row);
        row.AddChild(EditorFields.Row("Gold", "Money granted, in gold.",
            EditorFields.Number(reward == null ? 0 : (long)(reward.UL("Money") / 10000), gold => SetReward(responseId, "Change reward money",
                ("Money", (ulong)Math.Max(gold, 0) * 10000)), 80, "Money granted, in gold.")));
        row.AddChild(EditorFields.Row("XP", "Experience granted, when the choice is worth any.",
            EditorFields.Number(reward != null ? reward.U("Xp") : 0u, xp => SetReward(responseId, "Change reward experience", ("Xp", (uint)Math.Max(xp, 0))), 70, "Experience granted.")));
        row.AddChild(EditorFields.Row("Item", "An item granted; 0 for none.",
            EditorFields.Number(item != null ? item.U("ItemId") : 0u, itemId => SetItem(responseId, (uint)Math.Max(itemId, 0), item?.I("Quantity") ?? 1), 90, "An item granted; 0 for none.")));
        row.AddChild(EditorFields.Row("Amount", "How many of that item.",
            EditorFields.Number(item?.I("Quantity") ?? 0, quantity => SetItem(responseId, item != null ? item.U("ItemId") : 0u, (int)Math.Max(quantity, 0)), 60, "How many of that item.")));
    }

    /// <summary>The reward as one line: money, experience, item and quest, or "No reward".</summary>
    public string RewardSummary(uint responseId)
    {
        if (rows == null)
        {
            return "";
        }

        var reward = rows.Get(TcTables.PlayerChoiceResponseReward, $"{choiceId}|{responseId}");
        var item = TcCinematicsRules.RewardItems(rows, choiceId, responseId).FirstOrDefault(i => i.U("ItemId") != 0);
        var response = TcCinematicsRules.Responses(rows, choiceId).FirstOrDefault(r => r.U("ResponseId") == responseId);
        List<string> parts = [];
        if (reward != null)
        {
            if (reward.UL("Money") > 0)
            {
                parts.Add($"{reward.UL("Money") / 10000.0:0.##} gold");
            }

            if (reward.U("Xp") > 0)
            {
                parts.Add($"{reward.U("Xp")} XP");
            }
        }

        if (item != null)
        {
            parts.Add($"{item.I("Quantity")}× {names?.Item(item.U("ItemId")) ?? $"item {item.U("ItemId")}"}");
        }

        if (response != null && !response.IsNull("RewardQuestID") && response.U("RewardQuestID") != 0)
        {
            parts.Add($"quest {response.U("RewardQuestID")}");
        }

        return parts.Count == 0 ? "No reward" : string.Join(", ", parts);
    }

    public void AddResponse()
    {
        if (rows == null || rows.Get(TcTables.PlayerChoice, choiceId.ToString()) == null)
        {
            return;
        }

        uint id = TcCinematicsRules.NextResponseId(rows, choiceId);
        SelectedResponseId = id;
        Commit("Add response", [InteractionEdits.Insert(TcTables.PlayerChoiceResponse, TcCinematicsRules.NewResponse(choiceId, id, TcCinematicsRules.NextIndex(rows, choiceId), "Select"))]);
    }

    public void RemoveSelected()
    {
        if (rows == null || SelectedResponseId is not uint id
            || TcCinematicsRules.Responses(rows, choiceId).FirstOrDefault(r => r.U("ResponseId") == id) is not { } response)
        {
            return;
        }

        SelectedResponseId = null;
        Commit("Remove response", TcCinematicsRules.RemoveResponse(response, rows));
    }

    void SetReward(uint responseId, string label, params (string Column, object Value)[] values)
    {
        string key = $"{choiceId}|{responseId}";
        var before = rows!.Get(TcTables.PlayerChoiceResponseReward, key);
        var row = before ?? TcCinematicsRules.NewReward(choiceId, responseId);
        foreach (var (column, value) in values)
        {
            row = row.With(column, value);
        }

        SetRow(TcTables.PlayerChoiceResponseReward, before, row, label);
    }

    void SetItem(uint responseId, uint itemId, int quantity)
    {
        var before = TcCinematicsRules.RewardItems(rows!, choiceId, responseId).FirstOrDefault();
        string label = "Change reward item";
        if (itemId == 0 && quantity == 0)
        {
            if (before != null)
            {
                Commit(label, [InteractionEdits.Delete(TcTables.PlayerChoiceResponseRewardItem, before)]);
            }

            return;
        }

        var row = (before ?? TcCinematicsRules.NewRewardItem(choiceId, responseId, TcCinematicsRules.NextRewardItemIndex(rows!, choiceId, responseId), 0, 1))
            .With("ItemId", itemId).With("Quantity", quantity);
        SetRow(TcTables.PlayerChoiceResponseRewardItem, before, row, label);
    }

    void SetRow(TableSchema table, Row? before, Row after, string label)
    {
        if (before == after)
        {
            return;
        }

        Commit(label, before == null ? [InteractionEdits.Insert(table, after)] : ItemListSpec.Change(table, before, after));
    }
}
