using Godot;

namespace ACWorldEdit;

/// <summary>
/// Construction helpers for the vertical forms the viewer builds by hand (the left panel, the
/// teleport block, editor pages). A form wraps an existing <see cref="VBoxContainer"/> instead of
/// creating one, so converting a builder never inserts a node and the UI golden stays byte-identical.
/// Helpers create and attach controls in one call; they never own state, defer work or reorder
/// children. Every control a form creates is kept in <see cref="Manifest"/>, and predicates passed
/// to <see cref="Button"/> are re-evaluated by <see cref="Refresh"/>.
/// </summary>
public sealed class UiForm
{
    readonly List<(BaseButton Button, Func<bool> Enabled)> refresh = [];
    readonly List<Control> manifest = [];

    public UiForm(VBoxContainer root) => Root = root;

    /// <summary>The container the form fills. Existing children and their order are untouched.</summary>
    public VBoxContainer Root { get; }

    /// <summary>Every control this form created, in creation order.</summary>
    public IReadOnlyList<Control> Manifest => manifest;

    /// <summary>Attach an already configured control and record it.</summary>
    public T Add<T>(T control) where T : Control
    {
        Root.AddChild(control);
        manifest.Add(control);
        return control;
    }

    /// <summary>A plain label; no wrapping or modulate override, matching hand-built labels.</summary>
    public Label Text(string text, int size, Color? color = null)
    {
        var label = new Label { Text = text };
        if (size > 0)
        {
            label.AddThemeFontSizeOverride("font_size", size);
        }

        if (color is { } value)
        {
            label.AddThemeColorOverride("font_color", value);
        }

        return Add(label);
    }

    public Label Heading(string text, Color? color = null) => Text(text, 24, color);
    public Label Caption(string text, Color? color = null) => Text(text, 12, color);

    /// <summary>A caption above a separator, the visual break the left panel uses between groups.</summary>
    public Label Section(string text, Color? color = null)
    {
        var label = Caption(text, color);
        Add(new HSeparator());
        return label;
    }

    /// <summary>A horizontal row whose children are attached immediately.</summary>
    public HBoxContainer Row(params Control[] controls)
    {
        var row = Add(new HBoxContainer());
        foreach (var control in controls)
        {
            row.AddChild(control);
        }

        return row;
    }

    /// <summary>A caption cell followed by controls, e.g. "Tile X [34] Y [49]".</summary>
    public HBoxContainer LabeledRow(string caption, params Control[] controls)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = caption });
        foreach (var control in controls)
        {
            row.AddChild(control);
        }

        return Add(row);
    }

    /// <summary>
    /// A button, not yet attached. The optional predicate is re-evaluated by <see cref="Refresh"/>;
    /// a disabled button is also disabled while the predicate is true, exactly like the toolbar.
    /// </summary>
    public Button Button(string text, Action pressed, string tooltip = "", bool disabled = false, Func<bool>? enabled = null)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            Disabled = disabled
        };
        button.Pressed += pressed;
        manifest.Add(button);
        if (enabled != null)
        {
            button.Disabled = disabled || !enabled();
            refresh.Add((button, enabled));
        }

        return button;
    }

    /// <summary>A button attached to the form.</summary>
    public Button Action(string text, Action pressed, string tooltip = "", bool disabled = false, Func<bool>? enabled = null) =>
        Add(Button(text, pressed, tooltip, disabled, enabled));

    /// <summary>
    /// An option list, not yet attached. <paramref name="clip"/> trims long entries instead of
    /// growing the control, which is what the map list needs.
    /// </summary>
    public OptionButton Options(IReadOnlyList<string> items, int chosen, Action<int> onSelected,
        string? name = null, string tooltip = "", bool clip = false, float minWidth = 0)
    {
        var option = new OptionButton
        {
            TooltipText = tooltip,
            ClipText = clip,
            FitToLongestItem = !clip,
            TextOverrunBehavior = clip ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming
        };
        if (name != null)
        {
            option.Name = name;
        }

        if (minWidth > 0)
        {
            option.CustomMinimumSize = new Vector2(minWidth, 0);
        }

        foreach (string item in items)
        {
            option.AddItem(item);
        }

        if (chosen >= 0 && chosen < items.Count)
        {
            option.Select(chosen);
        }

        option.ItemSelected += index => onSelected((int)index);
        manifest.Add(option);
        return option;
    }

    /// <summary>A bounded number entry, not yet attached.</summary>
    public SpinBox Spin(double min, double max, double value, Action<double>? onChanged = null, double step = 1)
    {
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            Step = step
        };
        if (onChanged != null)
        {
            spin.ValueChanged += value => onChanged(value);
        }

        manifest.Add(spin);
        return spin;
    }

    /// <summary>A horizontal slider, not yet attached.</summary>
    public HSlider Slider(double min, double max, double value, double step, Action<double> onChanged,
        string tooltip = "", float width = 0)
    {
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            Step = step,
            TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        if (width > 0)
        {
            slider.CustomMinimumSize = new Vector2(width, 0);
        }

        slider.ValueChanged += value => onChanged(value);
        manifest.Add(slider);
        return slider;
    }

    /// <summary>Re-evaluate every enabled predicate. Disabled buttons stay disabled.</summary>
    public void Refresh()
    {
        foreach (var (button, enabled) in refresh)
        {
            button.Disabled = !enabled();
        }
    }
}
