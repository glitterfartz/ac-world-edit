using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>Every table a spawn delete can name must be editable, or the cleanup silently loses rows.</summary>
public class ReferenceCatalogTests
{
    [Theory]
    [InlineData(WorldDialect.AzerothCore)]
    [InlineData(WorldDialect.TrinityCore)]
    public void EveryReferencedTableIsInTheCatalog(WorldDialect dialect)
    {
        var catalog = dialect == WorldDialect.TrinityCore ? (ITableCatalog)TcTables.Instance : AcTables.Instance;
        foreach (var kind in new[] { SpawnKind.Creature, SpawnKind.GameObject })
        {
            foreach (var reference in SpawnEdits.References(dialect, kind))
            {
                Assert.Contains(catalog.All, table => table.Name == reference.Table);
            }
        }
    }
}
