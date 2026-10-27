using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// Draws a <see cref="CreaturePage"/> for either dialect: each group's fields as caption/editor rows,
/// flag sets as checkboxes plus a raw value box, and the difficulty picker when any field binds to a
/// difficulty row. Editors commit a <see cref="RowEdit"/> through the <see cref="CreatureContext"/>; with
/// no draft open every field is shown as text. A section hangs its own controls on a group through
/// <see cref="After"/>.
/// </summary>
public sealed class PageRenderer(CreatureContext ctx)
{
    int? difficulty;
    GridContainer? grid;

    /// <summary>Controls a section adds after a group's fields (group id → builder).</summary>
    public Dictionary<string, Action> After { get; } = [];

    /// <summary>The difficulty row the last <see cref="Render"/> showed (null when the creature has none), for the <see cref="After"/> builders.</summary>
    public Row? Picked { get; private set; }

    /// <summary>Forget the picked difficulty (a different creature was selected).</summary>
    public void Reset() => difficulty = null;

    /// <summary>Draw child rows using the same field editors as the template page. Sections supply
    /// the card heading (names, icons and prices); this renderer owns the fields and remove action.
    /// Optional field controls preserve specialized editors and resolved text; afterItem adds navigation.</summary>
    public void RenderItems(Control parent, ItemListSpec spec, RowSet rows, uint entry, bool editable, Func<Row, Control> heading,
        Func<FieldSpec, Row, Control?>? fieldControl = null, Action<Control, Row>? afterItem = null, int columns = 4)
    {
        foreach (var row in spec.Select(rows, entry))
        {
            var card = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            parent.AddChild(card);
            var head = new HBoxContainer();
            card.AddChild(head);
            var title = heading(row);
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (title is Label titleLabel)
            {
                titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            }
            head.AddChild(title);
            if (editable)
            {
                var remove = new Button { Text = "Remove", TooltipText = $"Remove this {spec.ItemName}." };
                remove.Pressed += () => ctx.Commit($"Remove {spec.ItemName}", [spec.Remove(row)]);
                head.AddChild(remove);
            }

            var fields = new GridContainer { Columns = columns };
            fields.AddThemeConstantOverride("h_separation", 8);
            card.AddChild(fields);
            foreach (var field in spec.Fields)
            {
                fields.AddChild(ClientStyle.Caption(field.Label, field.Tip));
                var control = fieldControl?.Invoke(field, row) ?? (editable ? Editor(field, row) : ClientStyle.Text(Shown(field, row), 13));
                control.TooltipText = field.Tip;
                if (control is Label label)
                {
                    label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                }
                fields.AddChild(control);
            }
            afterItem?.Invoke(card, row);
        }
    }

    public void Render(Control parent, CreaturePage page, RowSet rows, uint entry, bool editable)
    {
        string key = entry.ToString(CultureInfo.InvariantCulture);
        Row? picked = null;
        Picked = null;
        bool difficultyShown = false;
        foreach (var group in page.Groups)
        {
            var fields = page.In(group.Id).ToList();
            bool any = fields.Count > 0;
            if (any || After.ContainsKey(group.Id))
            {
                parent.AddChild(ClientStyle.Text(group.Title, 16, ClientStyle.Header));
                if (group.Hint != null)
                {
                    parent.AddChild(ClientStyle.Text(group.Hint, 12, ClientStyle.Faded));
                }
            }

            if (page.Difficulty is { } spec && !difficultyShown && fields.Any(f => f.Row == RowBinding.Difficulty))
            {
                difficultyShown = true;
                picked = Picked = ShowPicker(parent, spec, rows, entry, editable);
            }

            grid = null;
            foreach (var field in fields)
            {
                var row = field.Row == RowBinding.Template ? rows.Get(field.Table, key) : picked;
                if (row == null)
                {
                    continue;
                }

                if (field.Kind == FieldKind.Flags)
                {
                    grid = null;
                    Flags(parent, field, row, editable);
                    continue;
                }

                if (grid == null)
                {
                    grid = new GridContainer
                    {
                        Columns = 2
                    };
                    grid.AddThemeConstantOverride("h_separation", 10);
                    parent.AddChild(grid);
                }

                grid.AddChild(ClientStyle.Caption(field.Label, field.Tip));
                var control = editable ? Editor(field, row) : ClientStyle.Text(Shown(field, row), 14);
                control.TooltipText = field.Tip;
                if (control is Label label)
                {
                    label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                }

                grid.AddChild(control);
            }

            grid = null;
            if (After.TryGetValue(group.Id, out var extras))
            {
                extras();
            }
        }
    }

    /// <summary>
    /// The difficulty picker, with the add-difficulty controls when editable; returns the shown row, or
    /// null (with a note) when the creature has none.
    /// </summary>
    Row? ShowPicker(Control parent, DifficultySpec spec, RowSet rows, uint entry, bool editable)
    {
        var list = spec.Select(rows, entry).ToList();
        if (list.Count == 0)
        {
            var note = ClientStyle.Text(editable ? "This creature has no difficulty rows yet. Add one to edit stats, loot and gold." : "This creature has no difficulty rows.", 13, ClientStyle.Warning);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            parent.AddChild(note);
            if (editable)
            {
                AddDifficulty(parent, spec, rows, entry, null);
            }

            return null;
        }

        var picker = new OptionButton
        {
            Name = "DifficultyPicker",
            TooltipText = "Stats, loot and gold are stored per difficulty. Choose which row to show."
        };
        foreach (var row in list)
        {
            picker.AddItem(spec.Name(row.I(spec.IdColumn)), row.I(spec.IdColumn));
        }

        var shown = list.FirstOrDefault(r => r.I(spec.IdColumn) == difficulty) ?? list[0];
        difficulty = shown.I(spec.IdColumn);
        picker.Select(list.IndexOf(shown));
        picker.ItemSelected += index =>
        {
            difficulty = list[(int)index].I(spec.IdColumn);
            ctx.Redraw();
        };
        if (!editable)
        {
            parent.AddChild(picker);
            return shown;
        }

        var pick = new HBoxContainer();
        pick.AddThemeConstantOverride("separation", 6);
        pick.AddChild(picker);
        var remove = new Button { Name = "RemoveDifficulty", Text = "Remove", TooltipText = $"Delete the {spec.Name(shown.I(spec.IdColumn))} row. The server then falls back to another difficulty for it." };
        remove.Pressed += () =>
        {
            if (ctx.Commit($"Remove {spec.Name(shown.I(spec.IdColumn))}", [spec.Remove(shown)]))
            {
                difficulty = null;
                ctx.Redraw();
            }
        };
        pick.AddChild(remove);
        parent.AddChild(pick);
        AddDifficulty(parent, spec, rows, entry, shown);

        return shown;
    }

    /// <summary>Add a named or numbered difficulty row, copied from the shown row when there is one, then show it.</summary>
    void AddDifficulty(Control parent, DifficultySpec spec, RowSet rows, uint entry, Row? shown)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        parent.AddChild(box);
        void Add(int id)
        {
            try
            {
                var edit = spec.Add(rows, entry, id, shown);
                if (ctx.Commit($"Add {spec.Name(id)}", [edit]))
                {
                    difficulty = id;
                    ctx.Redraw();
                }
            }
            catch (InvalidDataException error)
            {
                ctx.Status = error.Message;
                ctx.Redraw();
            }
        }

        string from = shown == null ? "with default values" : $"as a copy of {spec.Name(shown.I(spec.IdColumn))}";
        var missing = spec.Common.Where(id => spec.CannotAdd(rows, entry, id) == null).ToArray();
        var common = new OptionButton { Name = "AddDifficulty", TooltipText = $"Add a difficulty row {from}." };
        common.AddItem("Add difficulty…", -1);
        foreach (int id in missing)
        {
            common.AddItem(spec.Name(id), id);
        }

        common.Selected = 0;
        common.ItemSelected += index =>
        {
            int id = common.GetItemId((int)index);
            if (id >= 0)
            {
                Add(id);
            }
        };
        box.AddChild(common);
        var other = new LineEdit { PlaceholderText = "Other ID", CustomMinimumSize = new(90, 0), TooltipText = $"Any difficulty ID from 0 to {spec.MaxId}, added {from}." };
        var add = new Button { Text = "Add" };
        void AddOther() => ctx.Try(() => Add(int.Parse(other.Text.Trim(), CultureInfo.InvariantCulture)));
        add.Pressed += AddOther;
        other.TextSubmitted += _ => AddOther();
        box.AddChild(other);
        box.AddChild(add);
    }

    static ColumnKind KindOf(FieldSpec field) => field.Table.Columns.First(c => c.Name == field.Column).Kind;

    static ulong Number(FieldSpec field, Row row) => KindOf(field) == ColumnKind.ULong ? row.UL(field.Column) : row.U(field.Column);

    static string Hex(ulong value) => value == 0 ? "0" : "0x" + value.ToString("X", CultureInfo.InvariantCulture);

    string Shown(FieldSpec field, Row row)
    {
        string raw = row.T(field.Column);
        switch (field.Kind)
        {
            case FieldKind.Check:
                return raw != "0" ? "Yes" : "No";
            case FieldKind.Choice:
                int value = int.Parse(raw, CultureInfo.InvariantCulture);
                return field.Options!.Where(o => o.Value == value).Select(o => o.Name).FirstOrDefault() ?? $"Unknown ({value})";
            case FieldKind.Decimal:
                return field.Display?.Invoke(raw) ?? float.Parse(raw, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);
            default:
                return field.Display?.Invoke(raw) ?? raw;
        }
    }

    Control Editor(FieldSpec field, Row row) => field.Kind switch
    {
        FieldKind.Text => TextBox(field, row),
        FieldKind.Whole => WholeBox(field, row),
        FieldKind.Decimal => DecimalBox(field, row),
        FieldKind.Choice => Choice(field, row),
        FieldKind.Check => Check(field, row),
        _ => throw new InvalidOperationException($"{field.Kind} is not a single-row editor.")
    };

    bool Commit(string label, FieldSpec field, Row row, object value) => ctx.Commit(label, ItemListSpec.Change(field.Table, row, row.With(field.Column, value)));

    void Reject(string message)
    {
        ctx.Status = $"Not saved: {message}";
        ctx.Redraw();
    }

    LineEdit TextBox(FieldSpec field, Row row)
    {
        int max = field.Table.Columns.First(c => c.Name == field.Column).MaxLength;
        var edit = new LineEdit
        {
            Text = row.T(field.Column),
            MaxLength = max,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new(220, 0)
        };
        edit.TextSubmitted += text =>
        {
            if (!field.AllowEmpty && text.Trim().Length == 0)
            {
                Reject($"the {field.Label.ToLowerInvariant()} cannot be empty.");
                return;
            }

            Commit($"Change {field.Label.ToLowerInvariant()}", field, row, text.Trim());
        };
        return edit;
    }

    LineEdit WholeBox(FieldSpec field, Row row)
    {
        var edit = new LineEdit
        {
            Text = row.T(field.Column),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => ctx.Try(() =>
        {
            object value = KindOf(field) switch
            {
                ColumnKind.Int => int.Parse(text.Trim(), CultureInfo.InvariantCulture),
                ColumnKind.Long => long.Parse(text.Trim(), CultureInfo.InvariantCulture),
                ColumnKind.ULong => ParseULong(text),
                _ => ParseUInt(text)
            };
            if (field.Max > 0 && Convert.ToDecimal(value, CultureInfo.InvariantCulture) > field.Max)
            {
                Reject($"{field.Label.ToLowerInvariant()} cannot be more than {field.Max}.");
                return;
            }

            Commit($"Change {field.Label.ToLowerInvariant()}", field, row, value);
        });
        return edit;
    }

    LineEdit DecimalBox(FieldSpec field, Row row)
    {
        var edit = new LineEdit
        {
            Text = float.Parse(row.T(field.Column), CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => ctx.Try(() =>
        {
            float value = float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!float.IsFinite(value) || value < 0)
            {
                Reject($"{field.Label.ToLowerInvariant()} must be zero or more.");
                return;
            }

            Commit($"Change {field.Label.ToLowerInvariant()}", field, row, value);
        });
        return edit;
    }

    OptionButton Choice(FieldSpec field, Row row)
    {
        bool signed = KindOf(field) == ColumnKind.Int;
        int current = int.Parse(row.T(field.Column), CultureInfo.InvariantCulture);
        var picker = new OptionButton();
        foreach (var (value, name) in field.Options!)
        {
            picker.AddItem(name, value);
        }

        int index = picker.GetItemIndex(current);
        if (index < 0)
        {
            picker.AddItem($"Unknown ({current})", current);
            index = picker.ItemCount - 1;
        }

        picker.Selected = index;
        picker.ItemSelected += i =>
        {
            int chosen = picker.GetItemId((int)i);
            Commit($"Change {field.Label.ToLowerInvariant()}", field, row, signed ? chosen : (uint)chosen);
        };
        return picker;
    }

    CheckBox Check(FieldSpec field, Row row)
    {
        var check = new CheckBox
        {
            ButtonPressed = row.U(field.Column) != 0
        };
        check.Toggled += on => Commit($"{(on ? "Enable" : "Disable")} {field.Label.ToLowerInvariant()}", field, row, on ? 1u : 0u);
        return check;
    }

    void Flags(Control parent, FieldSpec field, Row row, bool editable)
    {
        ulong value = Number(field, row);
        var bits = field.Flags ?? [];
        parent.AddChild(ClientStyle.Caption(field.Label, field.Tip));
        if (!editable)
        {
            var names = bits.Where(f => (value & f.Bit) != 0).Select(f => f.Name).ToList();
            ulong unnamed = value & ~bits.Aggregate(0ul, (all, f) => all | f.Bit);
            if (unnamed != 0)
            {
                names.Add($"other bits {Hex(unnamed)}");
            }

            var text = ClientStyle.Text(names.Count == 0 ? "None" : string.Join(", ", names), 14);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            parent.AddChild(text);
            return;
        }

        bool wide = KindOf(field) == ColumnKind.ULong;
        object Store(ulong v) => wide ? v : (uint)v;
        if (bits.Length > 0)
        {
            var checks = new GridContainer
            {
                Columns = 3
            };
            parent.AddChild(checks);
            foreach (var bit in bits)
            {
                var check = new CheckBox
                {
                    Text = bit.Name,
                    TooltipText = bit.Tip,
                    ButtonPressed = (value & bit.Bit) != 0
                };
                check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {bit.Name.ToLowerInvariant()}", field, row, Store(on ? value | bit.Bit : value & ~bit.Bit));
                checks.AddChild(check);
            }
        }

        if (!field.Raw && bits.Length > 0)
        {
            return;
        }

        var raw = new HBoxContainer();
        parent.AddChild(raw);
        raw.AddChild(ClientStyle.Tag("Raw value"));
        var edit = new LineEdit
        {
            Text = Hex(value),
            CustomMinimumSize = new(wide ? 160 : 110, 0),
            TooltipText = "The combined number stored in the database. Decimal or 0x hex."
        };
        edit.TextSubmitted += text => ctx.Try(() =>
        {
            ulong parsed = ParseULong(text);
            if (!wide && parsed > uint.MaxValue)
            {
                throw new OverflowException();
            }

            Commit($"Change {field.Label.ToLowerInvariant()}", field, row, Store(parsed));
        });
        raw.AddChild(edit);
    }

    static ulong ParseULong(string text)
    {
        string trimmed = text.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ulong.Parse(trimmed, CultureInfo.InvariantCulture);
    }
}
