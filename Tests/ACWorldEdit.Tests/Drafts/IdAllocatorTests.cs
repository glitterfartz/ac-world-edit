using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class IdAllocatorTests
{
    [Fact]
    public void TheNextIdClearsTheDatabaseAndTheDraft()
    {
        Assert.Equal(101UL, IdAllocator.Next(databaseMax: 100, stagedMax: 40));
        Assert.Equal(151UL, IdAllocator.Next(databaseMax: 100, stagedMax: 150));
        Assert.Equal(1UL, IdAllocator.Next(0, 0));
    }

    [Fact]
    public void AReservedRangeStartsExactlyAtItsFirstId()
    {
        Assert.Equal(5_000_000UL, IdAllocator.Next(databaseMax: 100, stagedMax: 0, rangeStart: 5_000_000));
        // Once the range is in use, the next ID follows what is there.
        Assert.Equal(5_000_011UL, IdAllocator.Next(databaseMax: 5_000_010, stagedMax: 0, rangeStart: 5_000_000));
    }

    [Fact]
    public void ABlockIsContiguous()
    {
        Assert.Equal(new ulong[] { 11, 12, 13 }, IdAllocator.Block(3, 10, 0));
        Assert.Empty(IdAllocator.Block(0, 10, 0));
    }

    [Fact]
    public void StagedInsertsCountEvenAfterUndoSoARedoCannotCollide()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        using var draft = new InteractionDraft(path, TcTables.Instance);
        var table = TcTables.SpawnGameObjectAddon;
        Assert.Equal(0UL, draft.MaxStaged("src", table, "guid"));

        var row = Row.Of(table.Columns.Select(c => (c.Name, (object)0u)).ToArray()).With("guid", 9_000_000_000UL);
        draft.Execute("src", new RowSet(), "Add", [new RowEdit(table.Name, table.KeyOf(row), null, row)]);
        Assert.Equal(9_000_000_000UL, draft.MaxStaged("src", table, "guid"));

        draft.Undo();
        Assert.Equal(9_000_000_000UL, draft.MaxStaged("src", table, "guid"));
        Assert.Equal(0UL, draft.MaxStaged("other", table, "guid"));
    }
}
