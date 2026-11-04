using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Draws a <see cref="EntityPage"/> for either dialect: each group's fields as caption/editor rows,
/// flag sets as checkboxes plus a raw value box, and the difficulty picker when any field binds to a
/// difficulty row. Editors commit a <see cref="RowEdit"/> through the <see cref="IEntityContext"/>; with
/// no draft open every field is shown as text. A section hangs its own controls on a group through
/// <see cref="After"/>.
/// </summary>
public sealed class PageRenderer(IEntityContext ctx)
{
    readonly PageFields fields = new(ctx);
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
                remove.Pressed += () => ctx.Commit($"Remove {spec.ItemName}", spec.Remove(row, ctx.Effective));
                head.AddChild(remove);
            }

            var fieldsGrid = new GridContainer { Columns = columns };
            fieldsGrid.AddThemeConstantOverride("h_separation", 8);
            card.AddChild(fieldsGrid);
            foreach (var field in spec.Fields)
            {
                fieldsGrid.AddChild(ClientStyle.Caption(field.Label, field.Tip));
                var control = fieldControl?.Invoke(field, row) ?? (editable ? fields.Editor(field, row) : ClientStyle.Text(fields.Shown(field, row), 13));
                control.TooltipText = field.Tip;
                if (control is Label label)
                {
                    label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                }
                fieldsGrid.AddChild(control);
            }
            afterItem?.Invoke(card, row);
        }
    }

    /// <summary>Draw every group of the page, in order, and show the difficulty row the fields bind to.</summary>
    public void Render(Control parent, EntityPage page, RowSet rows, uint entry, bool editable)
    {
        string key = entry.ToString(CultureInfo.InvariantCulture);
        Row? picked = null;
        Picked = null;
        bool difficultyShown = false;
        foreach (var group in page.Groups)
        {
            RenderGroup(parent, page, group, rows, key, entry, editable, ref picked, ref difficultyShown);
        }
    }

    void RenderGroup(Control parent, EntityPage page, GroupSpec group, RowSet rows, string key, uint entry, bool editable,
        ref Row? picked, ref bool difficultyShown)
    {
        var groupFields = page.In(group.Id).ToList();
        if (groupFields.Count > 0 || After.ContainsKey(group.Id))
        {
            parent.AddChild(ClientStyle.Text(group.Title, 16, ClientStyle.Header));
            if (group.Hint != null)
            {
                parent.AddChild(ClientStyle.Text(group.Hint, 12, ClientStyle.Faded));
            }
        }

        if (page.Difficulty is { } spec && !difficultyShown && groupFields.Any(f => f.Row == RowBinding.Difficulty))
        {
            difficultyShown = true;
            picked = Picked = ShowPicker(parent, spec, rows, entry, editable);
        }

        grid = null;
        foreach (var field in groupFields)
        {
            RenderField(parent, field, rows, key, picked, editable);
        }

        grid = null;
        if (After.TryGetValue(group.Id, out var extras))
        {
            extras();
        }
    }

    void RenderField(Control parent, FieldSpec field, RowSet rows, string key, Row? picked, bool editable)
    {
        var row = field.Row == RowBinding.Template ? rows.Get(field.Table, key) : picked;
        if (row == null)
        {
            return;
        }

        if (field.Kind == FieldKind.Flags)
        {
            grid = null;
            fields.Flags(parent, field, row, editable);
            return;
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
        var control = editable ? fields.Editor(field, row) : ClientStyle.Text(fields.Shown(field, row), 14);
        control.TooltipText = field.Tip;
        if (control is Label label)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        grid.AddChild(control);
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
}
