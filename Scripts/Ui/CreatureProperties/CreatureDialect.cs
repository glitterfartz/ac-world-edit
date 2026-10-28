using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>What a dialect built from one creature's effective rows: its read model and the one-line summary.</summary>
public sealed record CreatureViews(ICreatureView View, string Summary);

/// <summary>
/// The per-dialect steps of <see cref="CreatureContext"/>: the catalog it loads, the view it builds
/// from the effective rows, and the rule check run on every commit. The context calls these instead
/// of branching on <see cref="WorldDialect"/>.
/// </summary>
public interface ICreatureDialect
{
    /// <summary>The client catalog this dialect needs (null when it reads names through the client lookup), and a status line when it was expected but unavailable.</summary>
    Task<(InteractionCatalog? Catalog, string? Note)> LoadCatalog(IClientFiles? store);

    CreatureViews Build(InteractionSnapshot snapshot, RowSet effective);

    /// <summary>Server-rule issues of the rows, as shown after a commit.</summary>
    IEnumerable<InteractionIssue> Check(InteractionSnapshot snapshot, RowSet rows, IClientLookup? client);
}

public static class CreatureDialects
{
    public static ICreatureDialect For(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? new TcCreatureDialect() : new AcCreatureDialect();
}
