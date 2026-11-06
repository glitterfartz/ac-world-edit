using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The template's own identity and appearance: name, display, size, type, faction, flags,
/// money, art kits and the AI/script names. The type selector is added after the identity
/// fields; changing it confirms and resets the Data columns as one command. The addon row is
/// optional: the page shows defaults and the first edit inserts it.
/// </summary>
public sealed partial class GameObjectTemplateSection(GameObjectContext ctx) : GameObjectSection(ctx)
{
    public override string Id => "template";
    public override string Title => "Template";
    public override string IconPath => "Interface/Icons/INV_Misc_Note_01.blp";
    public override string Tip => "The template shared by every spawn: name, model, size, type, faction, flags, money and AI/script.";
    public override bool HasContent => Ctx.Template != null;
    public override string Badge => Ctx.SpawnCount > 0 ? $"{Ctx.SpawnCount} spawns" : "";

    public override void Refresh()
    {
        Clear();
        if (Ctx.Template is not { } template || Ctx.Entry is not uint entry)
        {
            AddChild(Placeholder());
            return;
        }

        var rows = Ctx.DisplayRows();
        IThumbnailSource? thumbnails = Ctx.Thumbnails();
        Func<uint, Action<Texture2D>, Texture2D?>? load = thumbnails == null ? null : thumbnails.GameObject;
        AddChild(ModelPreview.Row(template.U("displayId"), load));
        var page = GameObjectPage.TemplatePage(Ctx.Dialect);
        var renderer = new PageRenderer(Ctx);
        renderer.After["identity"] = AddTypePicker;
        renderer.Render(this, page, rows, entry, Ctx.Editable, (field, row) => GameObjectReferenceEditor.Build(this, field, row, Ctx));

        if (Ctx.Addon is { } addon && Ctx.Editable)
        {
            AddChild(new HSeparator());
            var remove = new Button { Text = "Use the defaults (remove the addon row)", TooltipText = "Delete this template's gameobject_template_addon row. The template then uses the server defaults." };
            remove.Pressed += () => Ctx.Commit("Remove the template's addon row", [InteractionEdits.Delete(Ctx.Schema.Tables.Find("gameobject_template_addon"), addon)]);
            AddChild(remove);
        }

        AddChild(ClientStyle.Text(
            $"Used by {Ctx.SpawnCount} spawns (see the Spawns page). Respawn, phase, rotation and pose are per spawn; select a spawn to edit them.",
            12, ClientStyle.Faded));
    }

    void AddTypePicker()
    {
        if (Ctx.Template is not { } template)
        {
            return;
        }

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(ClientStyle.Caption("Type", "The object type. It decides which Data fields apply; changing it resets every Data field to the new type's defaults."));
        if (!Ctx.Editable)
        {
            line.AddChild(ClientStyle.Text($"{Ctx.TypeCatalog.Name(Ctx.Type)} ({Ctx.Type})", 14));
            AddChild(line);
            return;
        }

        var picker = new OptionButton
        {
            Name = "TypePicker",
            TooltipText = "The object type. It decides which Data fields apply; changing it resets every Data field to the new type's defaults."
        };
        foreach (var type in Ctx.TypeCatalog.Types)
        {
            picker.AddItem($"{type.Name} ({type.Id})", type.Id);
        }

        int current = picker.GetItemIndex(Ctx.Type);
        if (current < 0)
        {
            picker.AddItem($"Unknown ({Ctx.Type})", Ctx.Type);
            current = picker.ItemCount - 1;
        }

        picker.Selected = current;
        picker.ItemSelected += index =>
        {
            int chosen = picker.GetItemId((int)index);
            if (chosen == Ctx.Type)
            {
                return;
            }

            string from = Ctx.TypeCatalog.Name(Ctx.Type);
            string to = Ctx.TypeCatalog.Name(chosen);
            var dialog = Dialogs.Confirm(this, "Change GameObject type",
                $"Change the type from {from} to {to}? This resets all Data fields to the {to} defaults; the values {from} used in those columns are lost. You can undo.",
                () =>
                {
                    if (!Ctx.Commit($"Change type to {to}", [GameObjectEdits.ChangeType(Ctx.Dialect, template, chosen)]))
                    {
                        picker.Select(current);
                    }
                }, ok: "Change type");
            dialog.Canceled += () => picker.Select(current);
        };
        line.AddChild(picker);
        AddChild(line);
    }
}
