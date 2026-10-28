namespace ACWorldEdit.Foundation;

/// <summary>
/// What the creature properties window holds of one loaded creature, whichever dialect built it:
/// <see cref="AcCreatureView"/> or <see cref="TcCreatureView"/>. Sections cast to their own.
/// </summary>
public interface ICreatureView
{
    uint Entry { get; }
    string Name { get; }
    string Subname { get; }
}
