using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class NetEditIndexTests
{
    [Fact]
    public void TheNetIndexTracksInsertsUndoRedoAndDiscard()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        using var draft = new InteractionDraft(path);
        var snapshot = new RowSet();
        var row = Row.Of(("MenuID", 50u), ("TextID", 60u));
        var edit = new RowEdit("gossip_menu", "50|60", null, row);
        draft.Execute("db", snapshot, "Insert", [edit]);

        Assert.Equal(edit, draft.Net("db", "gossip_menu", "50|60"));
        Assert.True(draft.Touches("db", "gossip_menu", "50|60"));
        Assert.Single(draft.NetFor("db"));
        Assert.Empty(draft.NetFor("other"));
        Assert.False(draft.Touches("db", "gossip_menu", "51|61"));

        draft.Undo();
        Assert.False(draft.Touches("db", "gossip_menu", "50|60"));
        Assert.Empty(draft.NetFor("db"));

        draft.Redo();
        Assert.NotNull(draft.Net("db", "gossip_menu", "50|60"));

        draft.Revert("db", draft.Net("db", "gossip_menu", "50|60")!);
        Assert.False(draft.Touches("db", "gossip_menu", "50|60"));
        Assert.Empty(draft.NetEdits());
    }
}
