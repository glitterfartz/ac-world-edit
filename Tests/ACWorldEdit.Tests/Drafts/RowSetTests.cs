using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class RowSetTests
{
    static Row Formation(uint member) => Row.Of(("leaderGUID", 5u), ("memberGUID", member), ("dist", 0f), ("angle", 0f), ("groupAI", 0u), ("point_1", 0u), ("point_2", 0u));

    [Fact]
    public void RowsCanBeWalkedByNameAndMerged()
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureFormations, Formation(5));
        Assert.Equal(["creature_formations"], rows.Names);
        Assert.Single(rows.All("creature_formations"));

        var other = new RowSet();
        other.Set(AcTables.CreatureFormations, Formation(6));
        other.Set(AcTables.SmartScripts, Row.Of(("entryorguid", -5), ("source_type", 0u), ("id", 0u), ("link", 0u)));
        Assert.Empty(rows.All("smart_scripts"));

        rows.Merge(other);
        Assert.Equal(2, rows.All("creature_formations").Count);
        Assert.Single(rows.All("smart_scripts"));
    }
}
