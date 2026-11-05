namespace ACWorldEdit.Foundation;

/// <summary>
/// Names for IDs shown in readable text (spells, creatures, objects, quests, items, creature_text).
/// Null means unknown; validation treats a non-zero unknown reference as missing.
/// </summary>
public interface IContentNames
{
    string? Spell(uint id);
    string? Creature(uint id);
    string? GameObject(uint id);
    string? Quest(uint id);
    string? Item(uint id);
    /// <summary>
    /// First creature_text line of a group for a creature entry.
    /// </summary>
    string? Text(uint entry, uint group);
    bool CreatureSpawnExists(ulong guid);
    string? Emote(uint id);
    string? TextEmote(uint id);
    string? FactionTemplate(uint id);
    /// <summary>
    /// Faction.dbc (reputation factions, as in quest rewards).
    /// </summary>
    string? Faction(uint id);
}

/// <summary>
/// Name lookup by parameter kind, for the kinds that reference a named database row.
/// </summary>
public static class ContentNamesExtensions
{
    /// <summary>True for the kinds <see cref="TryName"/> can look up.</summary>
    public static bool IsNamedReference(SmartParamKind kind) =>
        kind is SmartParamKind.Spell or SmartParamKind.Creature or SmartParamKind.GameObject or SmartParamKind.Quest or SmartParamKind.Item;

    /// <summary>False when the kind is not a named reference or the ID has no known name.</summary>
    public static bool TryName(this IContentNames names, SmartParamKind kind, uint id, out string name)
    {
        string? found = kind switch
        {
            SmartParamKind.Spell => names.Spell(id),
            SmartParamKind.Creature => names.Creature(id),
            SmartParamKind.GameObject => names.GameObject(id),
            SmartParamKind.Quest => names.Quest(id),
            SmartParamKind.Item => names.Item(id),
            _ => null
        };
        name = found ?? "";
        return found != null;
    }
}
