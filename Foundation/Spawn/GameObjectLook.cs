namespace ACWorldEdit.Foundation;

/// <summary>What a spawn of a GameObject template is drawn with: its name, model, size and type (which decides the door and button animations).</summary>
public sealed record GameObjectLook(string Name, uint DisplayId, float Scale, int Type)
{
    /// <summary>Shown for a spawn whose template is missing from the database.</summary>
    public static readonly GameObjectLook Missing = new("Missing template", 0, 1, 0);
}
