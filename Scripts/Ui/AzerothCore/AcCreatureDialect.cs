using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>The AzerothCore creature steps: the Wrath DBC catalog and the <see cref="AcCreatureView"/> view.</summary>
public sealed class AcCreatureDialect : ICreatureDialect
{
    public async Task<(InteractionCatalog? Catalog, string? Note)> LoadCatalog(IClientFiles? store)
    {
        var catalog = await ContentEditorWindow.LoadCatalog(store);
        return (catalog, catalog == null && store != null ? "Client spell/item tables unavailable; names and icons limited." : null);
    }

    public CreatureViews Build(InteractionSnapshot snapshot, RowSet effective)
    {
        var view = BuildView(snapshot, effective);
        return new CreatureViews(view, Summary(view));
    }

    public IEnumerable<InteractionIssue> Check(InteractionSnapshot snapshot, RowSet rows, IClientLookup? client) => BuildView(snapshot, rows).Issues;

    static AcCreatureView BuildView(InteractionSnapshot s, RowSet rows) => AcCreatureView.Build(s.Entry, s.Guid, s.Name, s.ScriptName, s.AIName, s.SpawnCount, s.Rows, rows, s.Lookup, s.Scripts, s.Conditions);
}
