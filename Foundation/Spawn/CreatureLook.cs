namespace ACWorldEdit.Foundation;

/// <summary>
/// What a new spawn of a creature template needs from the template: the name, first model and model
/// scale it is drawn with, its default equipment set (1 when the template has a set 1, the set the
/// server loads for a fresh spawn; otherwise 0, none) and its movement type.
/// </summary>
public sealed record CreatureLook(string Name, uint DisplayId, float Scale, int EquipmentId = 0, uint MovementType = 0)
{
    /// <summary>Shown for a spawn whose template is missing from the database.</summary>
    public static readonly CreatureLook Missing = new("Missing template", 0, 1);
}
