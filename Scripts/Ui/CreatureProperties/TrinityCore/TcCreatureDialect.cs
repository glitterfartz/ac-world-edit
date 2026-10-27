using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The TrinityCore creature steps: names come from the retail lookup, the view is a <see cref="TcCreatureView"/>, and commits run the Tc rule chain.</summary>
public sealed class TcCreatureDialect : ICreatureDialect
{
    public Task<(InteractionCatalog? Catalog, string? Note)> LoadCatalog(IClientFiles? store) => Task.FromResult<(InteractionCatalog?, string?)>((null, null));

    public CreatureViews Build(InteractionSnapshot snapshot, RowSet effective)
    {
        var view = TcCreatureView.Build(snapshot.Entry, effective, snapshot.Lookup);
        return new CreatureViews(null, view, view.Summary());
    }

    public IEnumerable<InteractionIssue> Check(InteractionSnapshot snapshot, RowSet rows, IClientLookup? client) =>
        TcVendorRules.Check(snapshot.Entry, rows, client == null ? null : id => client.Item(id) != null)
            .Concat(TcGossipRules.Check(rows))
            .Concat(TcTrainerRules.Check(rows, client == null ? null : id => client.SpellName(id) != null))
            .Concat(TcQuestLinkRules.Check(rows, id => snapshot.Lookup.Quest(id) != null))
            .Concat(TcStatRules.Check(rows));
}
