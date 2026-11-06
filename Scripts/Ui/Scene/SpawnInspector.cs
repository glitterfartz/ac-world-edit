using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The fields of one spawn row, grouped as Spawn, Visibility and Overrides (<see cref="SpawnFields"/>).
/// A field that overrides the template shows the template's value faded beneath it while it is empty and a
/// "Use template" button once it is set. The control only reports what was typed; the owner applies it as a
/// draft command and shows the row again, which also puts back a value that was refused.
/// </summary>
public sealed partial class SpawnInspector : VBoxContainer
{
    sealed record Entry(SpawnField Field, IFieldEditor Editor, Label Template, Button Reset);

    interface IFieldEditor
    {
        Control Root { get; }
        string Text { get; }
        void Show(SpawnField field, string text, bool editable, bool inherited, string templateText);
    }

    /// <summary>
    /// Where the spawn's appearance comes from: still loading the template's, inherited from it, or the spawn's own row,
    /// which replaces the template's completely.
    /// </summary>
    public enum AppearanceState { Loading, Inherited, Own }

    readonly Dictionary<string, Entry> entries = [];
    readonly VBoxContainer body = new();
    WorldDialect? dialect;
    SpawnKind? kind;
    bool showing;
    readonly bool appearanceOnly;
    Label? appearanceNote;
    Button? useTemplate;

    /// <summary>The emote list (or any other named choice) the owner can supply per field.</summary>
    public Func<SpawnField, IReadOnlyList<(uint Value, string Label)>?>? Choices { get; set; }

    /// <summary>The named bits of a flag column; the owner supplies the dialect's list.</summary>
    public Func<SpawnField, IReadOnlyList<FlagBit>?>? FlagBits { get; set; }

    /// <summary>The name shown for a reference value (display, creature, equipment set).</summary>
    public Func<SpawnField, string, string>? Describe { get; set; }

    /// <summary>Open a picker for a reference field and report the chosen ID.</summary>
    public Func<SpawnField, Action<uint>, bool>? Choose { get; set; }

    /// <summary>Open the equipment-set picker; the callback gets the set ID (-1 means a random template set).</summary>
    public Func<SpawnField, Action<long>, bool>? ChooseEquipment { get; set; }

    /// <summary>Open a spell picker for the aura list and report the chosen spell ID.</summary>
    public Action<SpawnField, Action<uint>>? ChooseAura { get; set; }

    /// <summary>The user asked the spawn to show its template's appearance again.</summary>
    public event Action? UseTemplateRequested;

    /// <summary>The user typed or chose a value for a field.</summary>
    public event Action<SpawnField, string>? Edited;

    /// <param name="appearanceOnly">Show only the appearance fields, for the template's own addon page.</param>
    public SpawnInspector(bool appearanceOnly = false)
    {
        this.appearanceOnly = appearanceOnly;
        Name = "SpawnInspector";
        AddChild(body);
        Visible = false;
    }

    /// <summary>Show a spawn row and the addon values to display (its own row, or what it inherits from its template), or hide the control when there is no row. <paramref name="template"/> holds the template's text for each overriding column.</summary>
    public void Show(WorldDialect dialect, SpawnKind kind, Row? row, Row? addon, AppearanceState? appearance, IReadOnlyDictionary<string, string>? template, bool editable, bool force = false)
    {
        Visible = row != null;
        if (row == null)
        {
            return;
        }

        if (this.dialect != dialect || this.kind != kind)
        {
            Build(dialect, kind);
        }

        showing = true;
        if (appearanceNote != null && useTemplate != null)
        {
            appearanceNote.Visible = appearance != null;
            appearanceNote.Text = appearance switch
            {
                AppearanceState.Loading => "Loading the template's appearance…",
                AppearanceState.Inherited => "Uses the template's appearance (shown faded). Setting a field gives this spawn its own.",
                AppearanceState.Own => "Has its own appearance, which replaces the template's.",
                _ => ""
            };
            useTemplate.Visible = appearance == AppearanceState.Own;
            useTemplate.Disabled = !editable;
        }

        foreach (var (field, editor, hint, reset) in entries.Values)
        {
            // The spawn's appearance is read-only until the template's is known, and faded while it is only inherited.
            bool fieldEditable = editable && !(field.Addon && appearance == AppearanceState.Loading);
            editor.Root.Modulate = field.Addon && appearance == AppearanceState.Inherited ? new Color(1, 1, 1, 0.6f) : Colors.White;
            string text = field.Show(field.Addon && addon != null ? addon : row);
            bool inherits = field.Inheritable && !field.IsOverridden(field.Addon && addon != null ? addon : row);
            string templateText = template != null && template.TryGetValue(field.TemplateColumn ?? "", out string value) ? value : "";
            editor.Show(field, text, fieldEditable, inherits, templateText);
            hint.Visible = field.TemplateColumn != null && inherits;
            hint.Text = template != null && template.TryGetValue(field.TemplateColumn ?? "", out string described) ? $"Template: {(described.Length == 0 ? "none" : described)}" : "Template: …";
            reset.Visible = field.Inheritable && !inherits;
            reset.Disabled = !fieldEditable;
        }

        showing = false;
    }

    void Build(WorldDialect dialect, SpawnKind kind)
    {
        this.dialect = dialect;
        this.kind = kind;
        entries.Clear();
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        appearanceNote = null;
        useTemplate = null;
        string? section = null;
        foreach (var source in appearanceOnly ? SpawnFields.AddonFor(dialect, kind) : SpawnFields.All(dialect, kind))
        {
            var field = source;
            if (field.Section != section)
            {
                section = field.Section;
                body.AddChild(InspectorUi.Text(section, 12, true));
                if (section == SpawnFields.AppearanceSection && !appearanceOnly && kind == SpawnKind.Creature)
                {
                    // A spawn's own addon row replaces its template's completely, so say which one is in force.
                    appearanceNote = InspectorUi.Text("", 11, true);
                    appearanceNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    body.AddChild(appearanceNote);
                    useTemplate = new Button { Text = "Use the template's appearance", Visible = false, FocusMode = FocusModeEnum.None, TooltipText = "Remove this spawn's own appearance so it follows the template again" };
                    useTemplate.Pressed += () => UseTemplateRequested?.Invoke();
                    body.AddChild(useTemplate);
                }
            }

            if (field.Column == "emote" && Choices?.Invoke(field) is { Count: > 0 } emotes)
            {
                field = field with { Choices = emotes.Select(e => (e.Value, e.Label)).ToArray() };
            }

            var line = new HBoxContainer();
            var label = InspectorUi.Text(field.Label, 13);
            label.CustomMinimumSize = new(120, 0);
            label.TooltipText = field.Tip;
            line.AddChild(label);
            var editor = BuildEditor(field);
            editor.Root.TooltipText = field.Tip;
            editor.Root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddChild(editor.Root);
            var reset = new Button { Text = "↺", TooltipText = field.TemplateColumn != null ? "Use the template's value" : "Clear it and use the default", Visible = false, FocusMode = FocusModeEnum.None };
            reset.Pressed += () => Edited?.Invoke(field, "");
            line.AddChild(reset);
            body.AddChild(line);
            var hint = InspectorUi.Text("", 11, true);
            hint.Visible = false;
            body.AddChild(hint);
            entries[field.Column] = new(field, editor, hint, reset);
        }
    }

    IFieldEditor BuildEditor(SpawnField field)
    {
        if (FlagBits?.Invoke(field) is { Count: > 0 } bits && field.Kind == SpawnFieldKind.Flags)
        {
            return new FlagsEditor(field, bits, Send);
        }

        if (field.Column == "wander_distance")
        {
            return new SpinEditor(field, Send);
        }

        if (field.Column == "auras" && ChooseAura != null)
        {
            return new AuraEditor(field, id => ChooseAura?.Invoke(field, id), Send);
        }

        if (field.Column is "spawnDifficulties" or "spawnMask")
        {
            return new DifficultyEditor(field, Send, field.Column == "spawnMask");
        }

        if (field.Kind == SpawnFieldKind.Choice && field.Choices is { Length: > 0 })
        {
            return new ChoiceEditor(field, Send);
        }

        if (field.Column is "mount" or "modelid" or "MountCreatureID" && Choose != null)
        {
            return new ReferenceEditor(field, Choose, Send, Describe);
        }

        if (field.Column == "equipment_id" && ChooseEquipment != null)
        {
            return new EquipmentEditor(field, ChooseEquipment, Send);
        }

        return new LineEditor(field, Send);
    }

    void Send(SpawnField field, string text)
    {
        if (!showing)
        {
            Edited?.Invoke(field, text);
        }
    }

    sealed class LineEditor : IFieldEditor
    {
        readonly LineEdit line = new() { SelectAllOnFocus = true, CustomMinimumSize = new(80, 0) };
        readonly SpawnField field;
        readonly Action<SpawnField, string> send;
        string sent = "";

        public LineEditor(SpawnField field, Action<SpawnField, string> send)
        {
            this.field = field;
            this.send = send;
            line.TextSubmitted += _ => Send();
            line.FocusExited += Send;
            line.FocusEntered += () => sent = line.Text;
        }

        public Control Root => line;

        public string Text => line.Text;

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            if (line.Text != text && !line.HasFocus())
            {
                line.Text = text;
            }

            line.Editable = editable;
            line.PlaceholderText = field.Inheritable ? field.EmptyHint : "";
        }

        void Send()
        {
            if (line.Text != sent)
            {
                sent = line.Text;
                send(field, line.Text);
            }
        }
    }

    sealed class ChoiceEditor : IFieldEditor
    {
        readonly OptionButton choice = new() { FitToLongestItem = false };
        readonly SpawnField field;
        readonly Action<SpawnField, string> send;
        bool updating;

        public ChoiceEditor(SpawnField field, Action<SpawnField, string> send)
        {
            this.field = field;
            this.send = send;
            foreach (var (_, text) in field.Choices ?? [])
            {
                choice.AddItem(text);
            }

            choice.ItemSelected += index =>
            {
                if (!updating)
                {
                    send(field, choice.GetItemText((int)index));
                }
            };
        }

        public Control Root => choice;

        public string Text => choice.GetItemText(choice.Selected);

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            updating = true;
            for (int i = 0; i < choice.ItemCount; i++)
            {
                if (choice.GetItemText(i) == text)
                {
                    choice.Select(i);
                }
            }

            updating = false;
            choice.Disabled = !editable;
        }
    }

    sealed class SpinEditor : IFieldEditor
    {
        readonly SpinBox spin;
        readonly SpawnField field;
        readonly Action<SpawnField, string> send;
        bool updating;

        public SpinEditor(SpawnField field, Action<SpawnField, string> send)
        {
            this.field = field;
            this.send = send;
            spin = new SpinBox
            {
                MinValue = Math.Max(0, field.Min),
                MaxValue = field.Max > 1e9 ? 1000 : field.Max,
                Step = 0.5,
                CustomMinimumSize = new(110, 0),
                TooltipText = field.Tip
            };
            spin.ValueChanged += value =>
            {
                if (!updating)
                {
                    send(field, value.ToString("0.##", CultureInfo.InvariantCulture));
                }
            };
        }

        public Control Root => spin;

        public string Text => spin.Value.ToString("0.##", CultureInfo.InvariantCulture);

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            updating = true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                spin.Value = value;
            }

            updating = false;
            spin.Editable = editable;
        }
    }

    sealed class ReferenceEditor : IFieldEditor
    {
        readonly HBoxContainer row = new();
        readonly Label label;
        readonly Button clear;
        readonly SpawnField field;
        readonly Func<SpawnField, Action<uint>, bool> choose;
        readonly Action<SpawnField, string> send;

        public ReferenceEditor(SpawnField field, Func<SpawnField, Action<uint>, bool> choose, Action<SpawnField, string> send, Func<SpawnField, string, string>? describe)
        {
            this.field = field;
            this.choose = choose;
            this.send = send;
            label = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.Off };
            row.AddChild(label);
            clear = new Button { Text = "✕", TooltipText = $"Remove the {field.Label.ToLowerInvariant()} (set to 0).", FocusMode = FocusModeEnum.None };
            clear.Pressed += () => send(field, "0");
            row.AddChild(clear);
            var pick = new Button { Text = "Choose…", TooltipText = $"Search for the {field.Label.ToLowerInvariant()}." };
            pick.Pressed += () => choose(field, id => send(field, id.ToString(CultureInfo.InvariantCulture)));
            row.AddChild(pick);
            _describe = describe;
        }

        readonly Func<SpawnField, string, string>? _describe;

        public Control Root => row;

        public string Text => label.Text;

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            string value = inherited && templateText.Length > 0 ? templateText : text;
            label.Text = value.Length == 0 ? "None" : _describe?.Invoke(field, value) ?? value;
            label.Modulate = inherited ? new Color(1, 1, 1, 0.6f) : Colors.White;
            clear.Visible = value.Length > 0 && value != "0";
            foreach (var child in row.GetChildren())
            {
                if (child is Button button)
                {
                    button.Disabled = !editable;
                }
            }
        }
    }

    sealed class EquipmentEditor : IFieldEditor
    {
        readonly HBoxContainer row = new();
        readonly LineEdit line;
        readonly SpawnField field;
        readonly Action<SpawnField, string> send;

        public EquipmentEditor(SpawnField field, Func<SpawnField, Action<long>, bool> choose, Action<SpawnField, string> send)
        {
            this.field = field;
            this.send = send;
            line = new LineEdit { Text = "0", CustomMinimumSize = new(80, 0), SelectAllOnFocus = true };
            line.TextSubmitted += text => send(field, text.Trim());
            row.AddChild(line);
            var pick = new Button { Text = "Choose…", TooltipText = "Search the equipment sets this creature can wear." };
            pick.Pressed += () => choose(field, id =>
            {
                line.Text = id.ToString(CultureInfo.InvariantCulture);
                send(field, line.Text);
            });
            row.AddChild(pick);
        }

        public Control Root => row;

        public string Text => line.Text;

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            if (line.Text != text && !line.HasFocus())
            {
                line.Text = text;
            }

            line.Editable = editable;
            foreach (var child in row.GetChildren())
            {
                if (child is Button button)
                {
                    button.Disabled = !editable;
                }
            }
        }
    }

    sealed class AuraEditor : IFieldEditor
    {
        readonly VBoxContainer root = new();
        readonly HBoxContainer chips = new();
        readonly Button add;
        readonly SpawnField field;
        readonly Action<SpawnField, string> send;
        string current = "";

        public AuraEditor(SpawnField field, Action<Action<uint>> choose, Action<SpawnField, string> send)
        {
            this.field = field;
            this.send = send;
            root.AddChild(chips);
            add = new Button { Text = "+ Add aura…", TooltipText = "Search spells and add one to the aura list." };
            add.Pressed += () => choose(id =>
            {
                if (!current.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(id.ToString(CultureInfo.InvariantCulture)))
                {
                    send(field, (current.Length > 0 ? current + " " : "") + id.ToString(CultureInfo.InvariantCulture));
                }
            });
            root.AddChild(add);
        }

        public Control Root => root;

        public string Text => current;

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            current = text;
            foreach (var child in chips.GetChildren())
            {
                chips.RemoveChild(child);
                child.QueueFree();
            }

            var ids = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length == 0)
            {
                chips.AddChild(ClientStyle.Text(inherited ? "Inherited" : "None", 12, ClientStyle.Faded));
            }

            foreach (string id in ids)
            {
                var chip = new Button { Text = $"{id} ✕", TooltipText = "Remove this aura.", Disabled = !editable };
                chip.Pressed += () => send(field, string.Join(' ', ids.Where(x => x != id)));
                chips.AddChild(chip);
            }

            add.Disabled = !editable;
        }
    }

    /// <summary>Known difficulties the spawn visibility dropdown offers.</summary>
    static readonly (int Id, string Name)[] Difficulties =
        [(0, "Open world (0)"), (1, "Normal dungeon (1)"), (2, "Heroic dungeon (2)"), (3, "Normal raid (3)"), (14, "Normal raid (14)"), (15, "Heroic raid (15)"), (16, "Mythic raid (16)"), (23, "Mythic dungeon (23)")];

    /// <summary>AzerothCore's spawnMask bits name the difficulty slots, not the difficulty IDs.</summary>
    static readonly (int Bit, string Name)[] MaskDifficulties =
        [(0, "Open world"), (1, "Normal dungeon"), (2, "Heroic dungeon"), (3, "Normal raid"), (4, "Heroic raid"), (5, "Dungeon 5"), (6, "Dungeon 6"), (7, "Dungeon 7")];

    sealed class DifficultyEditor : IFieldEditor
    {
        readonly CheckMenuButton menu;
        readonly SpawnField field;
        readonly Action<SpawnField, string> send;
        readonly bool maskField;
        bool updating;
        string current = "";

        public DifficultyEditor(SpawnField field, Action<SpawnField, string> send, bool maskField)
        {
            this.field = field;
            this.send = send;
            this.maskField = maskField;
            menu = new CheckMenuButton { Text = "Choose…" };
            if (maskField)
            {
                foreach (var (bit, name) in MaskDifficulties)
                {
                    menu.AddCheck(name, name, bit);
                }
            }
            else
            {
                foreach (var (id, name) in Difficulties)
                {
                    menu.AddCheck(name, name, id);
                }
            }

            menu.ItemToggled += id =>
            {
                if (updating)
                {
                    return;
                }

                if (maskField)
                {
                    ulong mask = ParseMask(current);
                    ulong bit = 1UL << Math.Clamp(id, 0, 31);
                    mask = (mask & bit) == 0 ? mask | bit : mask & ~bit;
                    send(field, mask.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    var ids = current.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToHashSet();
                    string value = id.ToString(CultureInfo.InvariantCulture);
                    if (!ids.Remove(value))
                    {
                        ids.Add(value);
                    }

                    send(field, string.Join(',', ids.OrderBy(s => int.TryParse(s, out int n) ? n : 0)));
                }
            };
        }

        public Control Root => menu;

        public string Text => current;

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            current = text;
            updating = true;
            if (maskField)
            {
                ulong mask = ParseMask(text);
                foreach (var (bit, _) in MaskDifficulties)
                {
                    menu.SetChecked(bit, (mask & (1UL << bit)) != 0);
                }
            }
            else
            {
                var ids = text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToHashSet();
                foreach (var (id, _) in Difficulties)
                {
                    menu.SetChecked(id, ids.Contains(id.ToString(CultureInfo.InvariantCulture)));
                }
            }

            updating = false;
            menu.Disabled = !editable;
        }

        static ulong ParseMask(string text) =>
            ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value) ? value : 0;
    }

    /// <summary>
    /// One dropdown per flag column: a boxed control whose checklist popup carries each flag's
    /// description on hover. Dozens of inline checkboxes spilled the narrow inspector off-screen.
    /// </summary>
    sealed class FlagsEditor : IFieldEditor
    {
        readonly SpawnField field;
        readonly IReadOnlyList<FlagBit> bits;
        readonly Action<SpawnField, string> send;
        readonly CheckMenuButton menu;
        bool updating;
        ulong value;

        public FlagsEditor(SpawnField field, IReadOnlyList<FlagBit> bits, Action<SpawnField, string> send)
        {
            this.field = field;
            this.bits = bits;
            this.send = send;
            menu = new CheckMenuButton { Text = "Choose…" };
            for (int i = 0; i < bits.Count; i++)
            {
                menu.AddCheck(bits[i].Name, bits[i].Tip, i);
            }

            menu.ItemToggled += index =>
            {
                if (updating)
                {
                    return;
                }

                var bit = bits[Math.Clamp(index, 0, bits.Count - 1)];
                value = (value & bit.Bit) == 0 ? value | bit.Bit : value & ~bit.Bit;
                send(field, value.ToString(CultureInfo.InvariantCulture));
            };
        }

        public Control Root => menu;

        public string Text => value.ToString(CultureInfo.InvariantCulture);

        public void Show(SpawnField field, string text, bool editable, bool inherited, string templateText)
        {
            string source = text.Length > 0 ? text : inherited ? templateText : "0";
            value = ulong.TryParse(source, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0;
            updating = true;
            for (int i = 0; i < bits.Count; i++)
            {
                menu.SetChecked(i, (value & bits[i].Bit) != 0);
            }

            updating = false;
            menu.Disabled = !editable;
            menu.Text = value == 0 ? "None" : $"{bits.Count(b => (value & b.Bit) != 0)} selected";
            menu.Modulate = inherited ? new Color(1, 1, 1, 0.6f) : Colors.White;
        }
    }

    /// <summary>The control and the text shown for a column, so a test can read what the user sees.</summary>
    public (Control Editor, string Text, string Template, bool CanReset)? Field(string column) => entries.TryGetValue(column, out var e)
        ? (e.Editor.Root, e.Editor.Text, e.Template.Visible ? e.Template.Text : "", e.Reset.Visible)
        : null;

    /// <summary>Type into a field and submit it, the way the user does.</summary>
    public void SmokeType(string column, string text)
    {
        var entry = entries[column];
        switch (entry.Editor.Root)
        {
            case OptionButton choice:
                for (int i = 0; i < choice.ItemCount; i++)
                {
                    if (choice.GetItemText(i) == text)
                    {
                        choice.Select(i);
                        choice.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                    }
                }

                break;
            case LineEdit line:
                line.Text = text;
                line.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
                break;
            case SpinBox spin:
                spin.Value = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
                spin.EmitSignal(SpinBox.SignalName.ValueChanged, spin.Value);
                break;
            default:
                if (entry.Field.Kind == SpawnFieldKind.Flags)
                {
                    Edited?.Invoke(entry.Field, text);
                }

                break;
        }
    }

    public void SmokePressReset(string column) => entries[column].Reset.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Whether the reference field's ✕ clear button is shown, for tests.</summary>
    public bool SmokeClearVisible(string column)
    {
        if (entries[column].Editor.Root is HBoxContainer box)
        {
            foreach (var child in box.GetChildren())
            {
                if (child is Button b && b.Text == "✕")
                {
                    return b.Visible;
                }
            }
        }

        return false;
    }

    /// <summary>Press a reference field's ✕ clear button, the way the user removes a mount.</summary>
    public void SmokePressClear(string column)
    {
        if (entries[column].Editor.Root is HBoxContainer box)
        {
            foreach (var child in box.GetChildren())
            {
                if (child is Button b && b.Text == "✕")
                {
                    b.EmitSignal(BaseButton.SignalName.Pressed);
                    return;
                }
            }
        }
    }

    /// <summary>The note under the Appearance header, for tests: which appearance is in force.</summary>
    public string SmokeNote => appearanceNote is { Visible: true } ? appearanceNote.Text : "";

    public bool SmokeUseTemplateVisible => useTemplate?.Visible == true;

    public void SmokePressUseTemplate() => useTemplate!.EmitSignal(BaseButton.SignalName.Pressed);
}
