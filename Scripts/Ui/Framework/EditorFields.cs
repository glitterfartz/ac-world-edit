using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Client-style form controls for the content editors. Values commit on Enter or when the field
/// loses focus, only when changed, so re-rendering a page never records spurious edits. Numbers
/// accept decimal or 0x hex; bad input reports through the error callback instead of throwing.
/// </summary>
public static class EditorFields
{
    public static Action<string> Error { get; set; } = _ => { };

    /// <summary>
    /// A label/entry grid (four columns wide) for typed coordinates, filling <paramref name="fields"/>.
    /// Enter in any entry calls <paramref name="submit"/>.
    /// </summary>
    public static void CoordinateGrid(Container card, string[] names, LineEdit[] fields, Action submit)
    {
        var grid = new GridContainer
        {
            Columns = 4
        };
        card.AddChild(grid);
        for (int i = 0; i < names.Length; i++)
        {
            // Plain non-wrapping labels: wrapped text has no minimum width inside a grid cell.
            var label = new Label
            {
                Text = names[i],
                Modulate = InspectorUi.Muted
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            grid.AddChild(label);
            fields[i] = new LineEdit
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new(70, 0),
                SelectAllOnFocus = true
            };
            fields[i].TextSubmitted += _ => submit();
            grid.AddChild(fields[i]);
        }
    }

    /// <summary>Parse every entry as an invariant-culture number; false if any is not one.</summary>
    public static bool TryParseAll(LineEdit[] fields, out double[] values)
    {
        values = new double[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            if (!double.TryParse(fields[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    static void CommitOnLeave(LineEdit edit, string original, Action<string> commit)
    {
        void Done(string text)
        {
            if (text != original)
            {
                original = text;
                commit(text);
            }
        }

        edit.TextSubmitted += Done;
        edit.FocusExited += () =>
        {
            if (edit.IsInsideTree() && !edit.IsQueuedForDeletion())
            {
                Done(edit.Text);
            }
        };
    }

    public static bool TryParse(string text, out long value)
    {
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static LineEdit Number(long value, Action<long> commit, int width = 80, string tooltip = "")
    {
        var edit = new LineEdit
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(width, 0),
            TooltipText = tooltip,
            SelectAllOnFocus = true
        };
        CommitOnLeave(edit, edit.Text, text =>
        {
            if (TryParse(text, out long parsed))
            {
                commit(parsed);
            }
            else
            {
                Error("Enter a whole number (decimal or 0x hex).");
            }
        });
        return edit;
    }

    public static LineEdit Decimal(double value, Action<double> commit, int width = 80, string tooltip = "")
    {
        var edit = new LineEdit
        {
            Text = value.ToString("0.###", CultureInfo.InvariantCulture),
            CustomMinimumSize = new(width, 0),
            SelectAllOnFocus = true,
            TooltipText = tooltip
        };
        CommitOnLeave(edit, edit.Text, text =>
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed))
            {
                commit(parsed);
            }
            else
            {
                Error("Enter a number (use . for decimals).");
            }
        });
        return edit;
    }

    public static LineEdit Line(string value, Action<string> commit, string placeholder = "", int maxLength = 0, string tooltip = "")
    {
        var edit = new LineEdit
        {
            Text = value,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = maxLength,
            TooltipText = tooltip
        };
        CommitOnLeave(edit, value, commit);
        return edit;
    }

    /// <summary>
    /// Multi-line text that commits when focus leaves. LastFocused lets placeholder buttons insert
    /// tokens ($N, $B…) at the caret.
    /// </summary>
    public static TextEdit Area(string value, Action<string> commit, int height = 90, string tooltip = "")
    {
        var edit = new TextEdit
        {
            Text = value,
            CustomMinimumSize = new(0, height),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = tooltip
        };
        string original = value;
        edit.FocusEntered += () => LastFocused = edit;
        edit.FocusExited += () =>
        {
            if (edit.Text != original && edit.IsInsideTree() && !edit.IsQueuedForDeletion())
            {
                original = edit.Text;
                commit(edit.Text);
            }
        };
        return edit;
    }

    public static TextEdit? LastFocused { get; private set; }

    /// <summary>
    /// Buttons that insert client text placeholders into the last focused text area.
    /// </summary>
    public static HBoxContainer Placeholders(IEnumerable<(string Token, string Meaning)> tokens)
    {
        var row = new HBoxContainer();
        row.AddChild(ClientStyle.Tag("Insert:"));
        foreach (var (token, meaning) in tokens)
        {
            var button = new Button
            {
                Text = token,
                TooltipText = meaning,
                FocusMode = Control.FocusModeEnum.None
            };
            string text = token.StartsWith("$G", StringComparison.Ordinal) ? "$Gman:woman;" : token;
            button.Pressed += () =>
            {
                if (LastFocused != null && GodotObject.IsInstanceValid(LastFocused) && LastFocused.IsInsideTree())
                {
                    LastFocused.InsertTextAtCaret(text);
                    LastFocused.GrabFocus();
                }
            };
            row.AddChild(button);
        }

        return row;
    }

    public static OptionButton Choice(IEnumerable<(uint Value, string Name)> options, uint value, Action<uint> commit, string tooltip = "")
    {
        var picker = new OptionButton
        {
            FitToLongestItem = false,
            TooltipText = tooltip
        };
        var list = options.ToList();
        if (!list.Any(o => o.Value == value))
        {
            list.Add((value, $"Other ({value})"));
        }

        foreach (var (option, name) in list)
        {
            picker.AddItem(name);
            picker.SetItemMetadata(picker.ItemCount - 1, (long)option);
        }

        picker.Select(list.FindIndex(o => o.Value == value));
        picker.ItemSelected += index => commit((uint)picker.GetItemMetadata((int)index).AsInt64());
        return picker;
    }

    /// <summary>
    /// Dropdown for text columns (a cursor icon name, an AI name) with the value stored as the item
    /// metadata. A value the list does not name is kept as "Other (value)".
    /// </summary>
    public static OptionButton TextChoice(IEnumerable<(string Value, string Name, string Icon)> options, string value, Action<string> commit,
        Func<string, Texture2D?>? texture = null, string tooltip = "")
    {
        var picker = new OptionButton
        {
            FitToLongestItem = false,
            TooltipText = tooltip
        };
        var list = options.ToList();
        if (!list.Any(o => o.Value == value))
        {
            list.Add((value, value.Length == 0 ? "Default" : $"Other ({value})", ""));
        }

        foreach (var (option, name, icon) in list)
        {
            int index = picker.ItemCount;
            var image = texture?.Invoke(icon);
            if (image != null)
            {
                picker.AddIconItem(image, name);
            }
            else
            {
                picker.AddItem(name);
            }

            picker.SetItemMetadata(index, option);
        }

        picker.Select(list.FindIndex(o => o.Value == value));
        picker.ItemSelected += index => commit(picker.GetItemMetadata((int)index).AsString());
        return picker;
    }

    /// <summary>
    /// Checkbox grid for a bitmask; toggling commits the whole new mask.
    /// </summary>
    public static GridContainer Flags(IEnumerable<(uint Bit, string Name)> flags, uint value, Action<uint> commit, int columns = 3, string tooltip = "")
    {
        var grid = new GridContainer
        {
            Columns = columns
        };
        foreach (var (bit, name) in flags)
        {
            var check = new CheckBox
            {
                Text = name,
                ButtonPressed = (value & bit) != 0,
                TooltipText = tooltip.Length > 0 ? tooltip : $"{name}: tick to turn this behaviour on for the quest or creature."
            };
            check.Toggled += on => commit(on ? value | bit : value & ~bit);
            grid.AddChild(check);
        }

        return grid;
    }

    /// <summary>
    /// Checkbox grid for a 64-bit bitmask (quest race masks); toggling commits the whole new mask.
    /// </summary>
    public static GridContainer Flags64(IEnumerable<(ulong Bit, string Name)> flags, ulong value, Action<ulong> commit, int columns = 3, string tooltip = "")
    {
        var grid = new GridContainer
        {
            Columns = columns
        };
        foreach (var (bit, name) in flags)
        {
            var check = new CheckBox
            {
                Text = name,
                ButtonPressed = (value & bit) != 0,
                TooltipText = tooltip.Length > 0 ? tooltip : $"{name}: tick to allow this race."
            };
            check.Toggled += on => commit(on ? value | bit : value & ~bit);
            grid.AddChild(check);
        }

        return grid;
    }

    /// <summary>
    /// A reference shown by name with Choose… and clear buttons. <paramref name="icon"/> adds the
    /// reference's client icon; <paramref name="gameTooltip"/> makes the whole row show a
    /// WoW-style tooltip on hover.
    /// </summary>
    public static HBoxContainer Reference(string text, Action choose, Action? clear, bool missing = false, string tooltip = "", Texture2D? icon = null, Tooltip? gameTooltip = null)
    {
        HBoxContainer row = gameTooltip == null
            ? new HBoxContainer()
            : new TooltipRow { Source = () => gameTooltip, TooltipText = text };
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.TooltipText = tooltip.Length > 0 ? tooltip : text;
        if (icon != null)
        {
            row.AddChild(new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new(24, 24),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            });
        }

        var label = ClientStyle.Text(text, 14, missing ? ClientStyle.Error : null);
        // Names stay on one line: in wrapping parameter rows a wrapping label is squeezed to one
        // character per line and stretches its neighbours.
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TooltipText = tooltip.Length > 0 ? tooltip : text;
        row.AddChild(label);
        var pick = new Button
        {
            Text = "Choose…",
            TooltipText = tooltip.Length > 0 ? tooltip : text
        };
        pick.Pressed += choose;
        row.AddChild(pick);
        if (clear != null)
        {
            var remove = new Button
            {
                Text = "✕",
                TooltipText = "Clear"
            };
            remove.Pressed += clear;
            row.AddChild(remove);
        }

        return row;
    }

    /// <summary>
    /// Labelled row: caption, then any controls. The tooltip is shown by the caption and by every
    /// child control that does not already have one, so hovering anywhere on the row explains it.
    /// </summary>
    public static HBoxContainer Row(string caption, string tip, params Control[] controls)
    {
        var row = Row(caption, controls);
        if (tip.Length > 0)
        {
            var label = (Label)row.GetChild(0);
            label.TooltipText = tip;
            label.MouseFilter = Control.MouseFilterEnum.Stop;
            foreach (var control in controls)
            {
                Tip(control, tip);
            }
        }

        return row;
    }

    /// <summary>
    /// Labelled row: caption, then any controls.
    /// </summary>
    public static HBoxContainer Row(string caption, params Control[] controls)
    {
        var row = new HBoxContainer();
        var label = ClientStyle.Tag(caption);
        label.CustomMinimumSize = new(130, 0);
        row.AddChild(label);
        foreach (var control in controls)
        {
            row.AddChild(control);
        }

        return row;
    }

    /// <summary>Set <paramref name="tip"/> on a control and every descendant that has no tooltip yet.</summary>
    public static void Tip(Control control, string tip)
    {
        if (tip.Length == 0)
        {
            return;
        }

        if (control.TooltipText.Length == 0)
        {
            control.TooltipText = tip;
        }

        foreach (var child in control.GetChildren())
        {
            if (child is Control nested)
            {
                Tip(nested, tip);
            }
        }
    }
}
