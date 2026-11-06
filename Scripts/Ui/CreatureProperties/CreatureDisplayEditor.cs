using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The template's display model at the top of the Template page: the first creature_template_model
/// row, shown with a large thumbnail and changed through the model picker. Both dialects keep the
/// models in the same table.
/// </summary>
public static class CreatureDisplayEditor
{
    public static void Render(Control parent, CreatureContext ctx, uint entry, int size = 128)
    {
        var table = ctx.Schema.Tables.Find("creature_template_model");
        var rows = ctx.Effective.All(table).Where(r => r.U("CreatureID") == entry).OrderBy(r => r.U("Idx")).ToList();
        var row = rows.FirstOrDefault();
        uint display = row?.U("CreatureDisplayID") ?? ctx.Look?.DisplayId ?? 0;

        var box = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        parent.AddChild(box);
        var preview = new TextureRect
        {
            CustomMinimumSize = new(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        box.AddChild(preview);
        var thumbnails = ctx.Thumbnails();
        if (thumbnails != null && display != 0)
        {
            if (thumbnails.Creature(display, texture =>
            {
                if (GodotObject.IsInstanceValid(preview))
                {
                    preview.Texture = texture;
                }
            }) is { } known)
            {
                preview.Texture = known;
            }
        }

        var note = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        box.AddChild(note);
        string label = display == 0 ? "Display: none" : $"Display {display}" + (rows.Count > 1 ? $" · {rows.Count} models" : "");
        var title = ClientStyle.Text(label, 14, ClientStyle.Header);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.AddChild(title);
        if (rows.Count > 1)
        {
            var sub = ClientStyle.Text("This template has several models; the lowest Idx is the one shown here.", 11, ClientStyle.Faded);
            sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.AddChild(sub);
        }
        else if (display == 0)
        {
            note.AddChild(ClientStyle.Text("No model.", 11, ClientStyle.Faded));
        }

        if (!ctx.Editable)
        {
            return;
        }

        var choose = new Button { Text = "Choose…", TooltipText = "Search creature display models by a creature that uses them." };
        choose.Pressed += () => Pick(parent, ctx, table, entry, row, display);
        note.AddChild(choose);
    }

    static void Pick(Node owner, CreatureContext ctx, TableSchema table, uint entry, Row? current, uint display)
    {
        var thumbnails = ctx.Thumbnails();
        RecordPicker.Show(owner, () => ctx.Store(), "Choose creature display",
            async term =>
            {
                var found = await DBSource.Open(ctx.Connection(), ctx.Lookup()).SearchCreatureDisplaysAsync(term, CancellationToken.None);
                return found.Select(hit => new PickerRecord(hit.Id, hit.Name.Length > 0 ? hit.Name : $"Display {hit.Id}",
                    $"display · {hit.Id}", Thumbnail: thumbnails == null ? null : ready => thumbnails.Creature(hit.Id, ready))).ToList();
            },
            record => Set(ctx, table, entry, current, record.Id), current: display == 0 ? null : display);
    }

    static void Set(CreatureContext ctx, TableSchema table, uint entry, Row? current, uint display)
    {
        var after = current != null
            ? current.With("CreatureDisplayID", display)
            : Row.Of(("CreatureID", entry), ("Idx", 0u), ("CreatureDisplayID", display), ("DisplayScale", 1f), ("Probability", 1f));
        var edit = current != null ? InteractionEdits.Update(table, current, after) : InteractionEdits.Insert(table, after);
        ctx.Commit("Change display model", [edit]);
    }
}
