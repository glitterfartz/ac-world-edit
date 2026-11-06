using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The template's display model: the first creature_template_model row, shown with its thumbnail and
/// changed through the model picker. Both dialects keep the models in the same table.
/// </summary>
public static class CreatureDisplayEditor
{
    public static void Render(Control parent, CreatureContext ctx, uint entry)
    {
        var table = ctx.Schema.Tables.Find("creature_template_model");
        var rows = ctx.Effective.All(table).Where(r => r.U("CreatureID") == entry).OrderBy(r => r.U("Idx")).ToList();
        var row = rows.FirstOrDefault();
        uint display = row?.U("CreatureDisplayID") ?? ctx.Look?.DisplayId ?? 0;

        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        parent.AddChild(box);
        box.AddChild(ClientStyle.Tag("Display"));
        var preview = new TextureRect
        {
            CustomMinimumSize = new(36, 36),
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

        string label = display == 0 ? "None" : $"Display {display}" + (rows.Count > 1 ? $" · {rows.Count} models" : "");
        var text = ClientStyle.Text(label, 14);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(text);
        if (!ctx.Editable)
        {
            return;
        }

        var choose = new Button { Text = "Choose…", TooltipText = "Search creature display models by a creature that uses them." };
        choose.Pressed += () => Pick(box, ctx, table, entry, row, display);
        box.AddChild(choose);
        if (rows.Count > 1)
        {
            text.TooltipText = "This template has several models; the lowest Idx is the one shown here.";
        }
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
