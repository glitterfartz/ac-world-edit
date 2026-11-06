using Godot;

namespace ACWorldEdit;

/// <summary>
/// The 96×96 model preview shown at the top of a template page: the cached thumbnail (or a blank
/// tile until it renders) and the display ID. The caller supplies the thumbnail source, so the
/// same row serves creatures and GameObjects.
/// </summary>
public static class ModelPreview
{
    static readonly Texture2D Blank = ImageTexture.CreateFromImage(Image.CreateEmpty(96, 96, false, Image.Format.Rgba8));

    /// <summary>
    /// A preview row for <paramref name="display"/>; <paramref name="load"/> is the matching
    /// thumbnail request (creature or GameObject) and may be null when no client is open.
    /// </summary>
    public static Control Row(uint display, Func<uint, Action<Texture2D>, Texture2D?>? load)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        var image = new TextureRect
        {
            Texture = Blank,
            CustomMinimumSize = new(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        row.AddChild(image);
        var note = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        var displayLabel = ClientStyle.Text($"Display {display}", 14, ClientStyle.Header);
        displayLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.AddChild(displayLabel);
        if (display == 0)
        {
            var none = ClientStyle.Text("No model.", 11, ClientStyle.Faded);
            none.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            note.AddChild(none);
        }

        row.AddChild(note);
        if (display != 0 && load != null)
        {
            void Ready(Texture2D texture)
            {
                if (GodotObject.IsInstanceValid(image) && image.IsInsideTree())
                {
                    image.Texture = texture;
                }
            }

            if (load(display, Ready) is { } known)
            {
                image.Texture = known;
            }
        }

        return row;
    }
}
