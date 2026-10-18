using System.Globalization;
using Godot;

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

    public static LineEdit Decimal(double value, Action<double> commit, int width = 80)
    {
        var edit = new LineEdit
        {
            Text = value.ToString("0.###", CultureInfo.InvariantCulture),
            CustomMinimumSize = new(width, 0),
            SelectAllOnFocus = true
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

    public static LineEdit Line(string value, Action<string> commit, string placeholder = "", int maxLength = 0)
    {
        var edit = new LineEdit
        {
            Text = value,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = maxLength
        };
        CommitOnLeave(edit, value, commit);
        return edit;
    }

    /// <summary>
    /// Multi-line text that commits when focus leaves. LastFocused lets placeholder buttons insert
    /// tokens ($N, $B…) at the caret.
    /// </summary>
    public static TextEdit Area(string value, Action<string> commit, int height = 90)
    {
        var edit = new TextEdit
        {
            Text = value,
            CustomMinimumSize = new(0, height),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
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
    /// Checkbox grid for a bitmask; toggling commits the whole new mask.
    /// </summary>
    public static GridContainer Flags(IEnumerable<(uint Bit, string Name)> flags, uint value, Action<uint> commit, int columns = 3)
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
                ButtonPressed = (value & bit) != 0
            };
            check.Toggled += on => commit(on ? value | bit : value & ~bit);
            grid.AddChild(check);
        }

        return grid;
    }

    /// <summary>
    /// A reference shown by name with Choose… and clear buttons.
    /// </summary>
    public static HBoxContainer Reference(string text, Action choose, Action? clear, bool missing = false)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var label = ClientStyle.Text(text, 14, missing ? ClientStyle.Error : null);
        // Names stay on one line: in wrapping parameter rows a wrapping label is squeezed to one
        // character per line and stretches its neighbours.
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        var pick = new Button
        {
            Text = "Choose…"
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
}

/// <summary>
/// Modal search picker: type to search (bounded results from the database or a client list), then
/// double-click or Enter to choose. Lives outside editor pages, so re-renders never lose results.
/// </summary>
public partial class PickerPopup : PopupPanel
{
    LineEdit search = null!;
    ItemList results = null!;
    Label status = null!;
    Func<string, Task<IReadOnlyList<(uint Id, string Name)>>> run = null!;
    Action<uint, string> pick = null!;
    int generation;
    uint? current;
    bool singleClick;

    /// <summary>
    /// Open a picker. <paramref name="current"/> is selected and scrolled to when listed;
    /// <paramref name="singleClick"/> chooses on one click, for short fixed lists such as types.
    /// </summary>
    public static PickerPopup Show(Node owner, string title, Func<string, Task<IReadOnlyList<(uint Id, string Name)>>> search, Action<uint, string> picked, string initial = "", uint? current = null, bool singleClick = false)
    {
        var popup = new PickerPopup();
        popup.Build(title);
        popup.run = search;
        popup.pick = picked;
        popup.current = current;
        popup.singleClick = singleClick;
        if (singleClick)
        {
            popup.results.ItemClicked += (index, _, button) =>
            {
                if (button == (long)MouseButton.Left)
                {
                    popup.Choose((int)index);
                }
            };
        }

        owner.AddChild(popup);
        popup.PopupCentered(new Vector2I(460, 480));
        popup.search.Text = initial;
        popup.search.GrabFocus();
        _ = popup.Search();
        popup.PopupHide += popup.QueueFree;
        return popup;
    }

    void Build(string title)
    {
        Theme = ClientStyle.Theme();
        AddThemeStyleboxOverride("panel", ClientStyle.Panel());
        var root = new VBoxContainer();
        AddChild(root);
        root.AddChild(ClientStyle.Text(title, 17, ClientStyle.Header));
        search = new LineEdit
        {
            PlaceholderText = "Type a name or ID…",
            ClearButtonEnabled = true,
            Name = "PickerSearch"
        };
        search.TextChanged += text =>
        {
            _ = Search();
        };
        search.TextSubmitted += _ => Choose(0);
        root.AddChild(search);
        results = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 340),
            Name = "PickerResults"
        };
        results.ItemActivated += index => Choose((int)index);
        root.AddChild(results);
        status = ClientStyle.Text("", 11, ClientStyle.Faded);
        root.AddChild(status);
    }

    async Task Search()
    {
        int current = ++generation;
        string term = search.Text;
        try
        {
            var found = await run(term);
            if (current != generation || !IsInstanceValid(this))
            {
                return;
            }

            results.Clear();
            foreach (var (id, name) in found)
            {
                results.AddItem($"{name}   ·   {id}");
                results.SetItemMetadata(results.ItemCount - 1, (long)id);
                if (id == current)
                {
                    results.Select(results.ItemCount - 1);
                    results.EnsureCurrentIsVisible();
                }
            }

            status.Text = found.Count == 0 ? "No matches." : $"{found.Count} result(s) · {(singleClick ? "click" : "double-click")} to choose";
            ShownTerm = term;
        }
        catch (Exception error)
        {
            status.Text = Data.CreatureSource.SafeError(error);
        }
    }

    public int ResultCount => results.ItemCount;
    /// <summary>
    /// Search term whose results are currently listed.
    /// </summary>
    public string? ShownTerm { get; private set; }

    /// <summary>
    /// Choose a result by index (Enter picks the first).
    /// </summary>
    public void Choose(int index)
    {
        if (index < 0 || index >= results.ItemCount)
        {
            return;
        }

        uint id = (uint)results.GetItemMetadata(index).AsInt64();
        string name = results.GetItemText(index).Split("   ·   ")[0];
        Hide();
        pick(id, name);
    }
}
