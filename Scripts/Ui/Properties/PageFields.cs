using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// Builds one <see cref="EntityPage"/> field's editor and commits its value through the entity
/// context. <see cref="PageRenderer"/> owns the page, group and difficulty structure; this class owns
/// the field kinds, their validation and the "Not saved" message.
/// </summary>
public sealed class PageFields(IEntityContext ctx)
{
    /// <summary>The field's value as the read-only page shows it.</summary>
    public string Shown(FieldSpec field, Row row)
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

    /// <summary>The editor for one field; a kind that is not a single-row field is a programming error.</summary>
    public Control Editor(FieldSpec field, Row row) => field.Kind switch
    {
        FieldKind.Text => TextBox(field, row),
        FieldKind.Whole => WholeBox(field, row),
        FieldKind.Decimal => DecimalBox(field, row),
        FieldKind.Choice => Choice(field, row),
        FieldKind.Check => Check(field, row),
        _ => throw new InvalidOperationException($"{field.Kind} is not a single-row editor.")
    };

    /// <summary>
    /// A flag set as checkboxes plus a raw value box. With no draft open it is one read-only line;
    /// the raw box is omitted for a named set unless the field asks for it.
    /// </summary>
    public void Flags(Control parent, FieldSpec field, Row row, bool editable)
    {
        ulong value = Number(field, row);
        var bits = field.Flags ?? [];
        parent.AddChild(ClientStyle.Caption(field.Label, field.Tip));
        if (!editable)
        {
            ReadOnlyFlags(parent, value, bits);
            return;
        }

        bool wide = KindOf(field) == ColumnKind.ULong;
        object Store(ulong v) => wide ? v : (uint)v;
        if (bits.Length > 0)
        {
            FlagChecks(parent, field, row, value, bits, Store);
        }

        if (field.Raw || bits.Length == 0)
        {
            RawFlags(parent, field, row, value, wide, Store);
        }
    }

    static ColumnKind KindOf(FieldSpec field) => field.Table.Columns.First(c => c.Name == field.Column).Kind;

    static ulong Number(FieldSpec field, Row row) => KindOf(field) == ColumnKind.ULong ? row.UL(field.Column) : row.U(field.Column);

    static string Hex(ulong value) => value == 0 ? "0" : "0x" + value.ToString("X", CultureInfo.InvariantCulture);

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

    static void ReadOnlyFlags(Control parent, ulong value, FlagBit[] bits)
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
    }

    void FlagChecks(Control parent, FieldSpec field, Row row, ulong value, FlagBit[] bits, Func<ulong, object> store)
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
            check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {bit.Name.ToLowerInvariant()}", field, row, store(on ? value | bit.Bit : value & ~bit.Bit));
            checks.AddChild(check);
        }
    }

    void RawFlags(Control parent, FieldSpec field, Row row, ulong value, bool wide, Func<ulong, object> store)
    {
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

            Commit($"Change {field.Label.ToLowerInvariant()}", field, row, store(parsed));
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
