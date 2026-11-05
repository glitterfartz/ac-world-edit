using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// What a content browser row carries while it is dragged: the template to place. Packed into a Godot
/// dictionary because that is what the engine's drag-and-drop passes between controls.
/// </summary>
public static class TemplateDrag
{
    public static Godot.Collections.Dictionary Pack(PlacementTool.Template template) => new()
    {
        ["kind"] = (int)template.Kind,
        ["entry"] = (long)template.Entry,
        ["name"] = template.Name
    };

    public static bool TryUnpack(Variant data, out PlacementTool.Template template)
    {
        template = null!;
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        var dictionary = data.AsGodotDictionary();
        if (!dictionary.ContainsKey("kind") || !dictionary.ContainsKey("entry") || !dictionary.ContainsKey("name"))
        {
            return false;
        }

        template = new((SpawnKind)(int)dictionary["kind"], (uint)(long)dictionary["entry"], (string)dictionary["name"]);
        return true;
    }
}

/// <summary>
/// A results list whose rows can be dragged out to the world. It gives the engine a template to carry and
/// a small label to show under the cursor; dropping is handled by <see cref="ViewportDropSurface"/>.
/// </summary>
public sealed partial class TemplateList : ItemList
{
    /// <summary>The template of a row, or null when the row is gone.</summary>
    public Func<int, PlacementTool.Template?> TemplateAt { get; set; } = _ => null;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        int row = GetItemAtPosition(atPosition, true);
        if (row < 0 || TemplateAt(row) is not { } template)
        {
            return default;
        }

        Select(row);
        SetDragPreview(ClientStyle.Text($"{template.Name}  ·  {template.Entry}", 13, ClientStyle.Faded));
        return TemplateDrag.Pack(template);
    }
}
