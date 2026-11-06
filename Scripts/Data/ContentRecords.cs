using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Names resolved inside one snapshot transaction plus the client spell catalog. IDs the snapshot
/// did not read (for example a just-picked item) are seeded by the editor before validation.
/// </summary>
public sealed class ContentNames(InteractionCatalog? catalog, IClientLookup? client = null) : IContentNames
{
    public readonly Dictionary<uint, string> Creatures = [], GameObjects = [], Quests = [], Items = [];
    /// <summary>The tooltip fields of the items this snapshot resolved, when the source could read them.</summary>
    public readonly Dictionary<uint, ItemDetails> ItemDetails = [];
    public readonly Dictionary<(uint Entry, uint Group), string> Texts = [];
    public readonly HashSet<ulong> Spawns = [];
    /// <summary>broadcast_text ID to its text, for the speech lines that link one.</summary>
    public readonly Dictionary<uint, string> Broadcasts = [];
    public InteractionCatalog? Catalog => catalog;

    public string? Spell(uint id) => client != null ? client.SpellName(id) : catalog?.SpellName(id) ?? (catalog == null ? $"spell {id}" : null);
    public string? Creature(uint id) => Creatures.GetValueOrDefault(id);
    public string? GameObject(uint id) => GameObjects.GetValueOrDefault(id);
    public string? Quest(uint id) => Quests.GetValueOrDefault(id);
    public string? Item(uint id) => Items.GetValueOrDefault(id);
    /// <summary>The item template fields behind <see cref="Item"/>, or null when they were not read.</summary>
    public ItemDetails? Details(uint id) => ItemDetails.GetValueOrDefault(id);
    public string? Text(uint entry, uint group) => Texts.GetValueOrDefault((entry, group));
    public bool CreatureSpawnExists(ulong guid) => Spawns.Contains(guid);
    public string? Emote(uint id) => catalog?.EmoteName(id);
    public string? TextEmote(uint id) => catalog?.TextEmoteName(id);
    public string? FactionTemplate(uint id) => catalog?.FactionTemplateName(id);
    public string? Faction(uint id) => catalog?.FactionName(id);

    /// <summary>
    /// Refresh the creature's text-group previews (first line of each group) from a view, so drafted
    /// lines show in "Talk" sentences.
    /// </summary>
    public static void FillTexts(ContentNames names, RowSet view, TableSchema creatureText, uint entry)
    {
        foreach (var key in names.Texts.Keys.Where(k => k.Entry == entry).ToList())
        {
            names.Texts.Remove(key);
        }

        foreach (var line in view.All(creatureText).Where(r => r.U("CreatureID") == entry).OrderBy(r => r.U("ID")))
        {
            names.Texts.TryAdd((entry, line.U("GroupID")), line.T("Text"));
        }
    }
}

public sealed record ContentSnapshot(RowSet Rows, ContentNames Names, string SourceKey, uint MaxQuestId, string CreatureName = "", uint Entry = 0, string ScriptName = "", uint MaxObjectiveId = 0);
