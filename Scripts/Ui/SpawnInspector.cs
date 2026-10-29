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
    sealed record Entry(SpawnField Field, Control Editor, Label Template, Button Reset);

    readonly Dictionary<string, Entry> entries = [];
    readonly VBoxContainer body = new();
    readonly Button header = new() { ToggleMode = true, ButtonPressed = true, Flat = true, Alignment = HorizontalAlignment.Left };
    WorldDialect? dialect;
    SpawnKind? kind;
    bool showing;

    /// <summary>The user typed or chose a value for a field.</summary>
    public event Action<SpawnField, string>? Edited;

    public SpawnInspector()
    {
        Name = "SpawnInspector";
        header.Text = "▾ Spawn fields";
        header.Toggled += open =>
        {
            body.Visible = open;
            header.Text = (open ? "▾ " : "▸ ") + "Spawn fields";
        };
        AddChild(header);
        AddChild(body);
        Visible = false;
    }

    /// <summary>Show a spawn row and its addon row (a spawn without one passes a blank), or hide the control when there is no row. <paramref name="template"/> holds the template's text for each overriding column.</summary>
    public void Show(WorldDialect dialect, SpawnKind kind, Row? row, Row? addon, IReadOnlyDictionary<string, string>? template, bool editable, bool force = false)
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
        foreach (var (field, editor, hint, reset) in entries.Values)
        {
            string text = field.Show(field.Addon && addon != null ? addon : row);
            switch (editor)
            {
                case OptionButton choice:
                    for (int i = 0; i < choice.ItemCount; i++)
                    {
                        if (choice.GetItemText(i) == text)
                        {
                            choice.Select(i);
                        }
                    }

                    choice.Disabled = !editable;
                    break;
                case LineEdit line:
                    if (line.Text != text && (force || !line.HasFocus()))
                    {
                        line.Text = text;
                    }

                    line.Editable = editable;
                    line.PlaceholderText = field.Inheritable ? field.EmptyHint : "";
                    break;
            }

            bool inherits = field.Inheritable && !field.IsOverridden(field.Addon && addon != null ? addon : row);
            hint.Visible = field.TemplateColumn != null && inherits;
            hint.Text = template != null && template.TryGetValue(field.TemplateColumn ?? "", out string value) ? $"Template: {(value.Length == 0 ? "none" : value)}" : "Template: …";
            reset.Visible = field.Inheritable && !inherits;
            reset.Disabled = !editable;
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

        string? section = null;
        foreach (var field in SpawnFields.All(dialect, kind))
        {
            if (field.Section != section)
            {
                section = field.Section;
                body.AddChild(InspectorUi.Text(section, 12, true));
            }

            var line = new HBoxContainer();
            var label = InspectorUi.Text(field.Label, 13);
            label.CustomMinimumSize = new(120, 0);
            label.TooltipText = field.Tip;
            line.AddChild(label);
            Control editor = field.Kind == SpawnFieldKind.Choice ? MakeChoice(field) : MakeLine(field);
            editor.TooltipText = field.Tip;
            editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddChild(editor);
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

    OptionButton MakeChoice(SpawnField field)
    {
        var choice = new OptionButton { FitToLongestItem = false };
        foreach (var (_, text) in field.Choices ?? [])
        {
            choice.AddItem(text);
        }

        choice.ItemSelected += index =>
        {
            if (!showing)
            {
                Edited?.Invoke(field, choice.GetItemText((int)index));
            }
        };
        return choice;
    }

    LineEdit MakeLine(SpawnField field)
    {
        var line = new LineEdit { SelectAllOnFocus = true, CustomMinimumSize = new(80, 0) };
        string sent = "";
        void Send()
        {
            if (!showing && line.Text != sent)
            {
                sent = line.Text;
                Edited?.Invoke(field, line.Text);
            }
        }

        line.TextSubmitted += _ => Send();
        line.FocusExited += Send;
        line.FocusEntered += () => sent = line.Text;
        return line;
    }

    /// <summary>The control and the text shown for a column, so a test can read what the user sees.</summary>
    public (Control Editor, string Text, string Template, bool CanReset)? Field(string column) => entries.TryGetValue(column, out var e)
        ? (e.Editor, e.Editor is OptionButton o ? o.GetItemText(o.Selected) : ((LineEdit)e.Editor).Text, e.Template.Visible ? e.Template.Text : "", e.Reset.Visible)
        : null;

    /// <summary>Type into a field and submit it, the way the user does.</summary>
    public void SmokeType(string column, string text)
    {
        var entry = entries[column];
        if (entry.Editor is OptionButton choice)
        {
            for (int i = 0; i < choice.ItemCount; i++)
            {
                if (choice.GetItemText(i) == text)
                {
                    choice.Select(i);
                    choice.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                }
            }
        }
        else if (entry.Editor is LineEdit line)
        {
            line.Text = text;
            line.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
        }
    }

    public void SmokePressReset(string column) => entries[column].Reset.EmitSignal(BaseButton.SignalName.Pressed);

    public bool SmokeOpen => body.Visible;
}
