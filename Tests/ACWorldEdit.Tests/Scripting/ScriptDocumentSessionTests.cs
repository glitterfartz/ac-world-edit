using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Undo/redo history and dirty tracking for one open script document.</summary>
public class ScriptDocumentSessionTests
{
    static ScriptDocument Doc(string name = "Session")
    {
        var document = ScriptDocument.Create(name);
        document.Graph.Nodes.Add(new ScriptNode { Id = "n1", Kind = "event.creature.on_died", Properties = { ["entry"] = System.Text.Json.JsonSerializer.SerializeToElement(1) } });
        return document;
    }

    [Fact]
    public void AFreshSessionIsClean()
    {
        Assert.False(new ScriptDocumentSession().Dirty);
    }

    [Fact]
    public void LoadedDocumentIsClean()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc());

        Assert.False(session.Dirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void EditMarksDirtyAndUndoRestoresTheDocument()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc());
        string original = session.Document.Description;

        session.Edit(document => document.Description = "edited");

        Assert.True(session.Dirty);
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(original, session.Document.Description);
        Assert.False(session.Dirty);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void RedoReappliesTheEdit()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc());
        session.Edit(document => document.Description = "edited");
        session.Undo();

        Assert.True(session.Redo());
        Assert.Equal("edited", session.Document.Description);
        Assert.True(session.Dirty);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void EditingAfterUndoClearsRedo()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc());
        session.Edit(document => document.Description = "first");
        session.Undo();
        session.Edit(document => document.Description = "second");

        Assert.False(session.CanRedo);
        Assert.True(session.Undo());
        Assert.Equal("", session.Document.Description);
    }

    [Fact]
    public void MarkSavedMakesTheCurrentStateClean()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc());
        session.Edit(document => document.Description = "saved");

        session.MarkSaved();

        Assert.False(session.Dirty);
        session.Edit(document => document.Description = "again");
        Assert.True(session.Dirty);
    }

    [Fact]
    public void HistoryIsBounded()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc());
        for (int i = 0; i < ScriptDocumentSession.HistoryLimit + 20; i++)
        {
            session.Edit(document => document.Description = i.ToString());
        }

        int undone = 0;
        while (session.Undo())
        {
            undone++;
        }

        Assert.Equal(ScriptDocumentSession.HistoryLimit, undone);
    }

    [Fact]
    public void LoadingResetsHistoryAndPath()
    {
        var session = new ScriptDocumentSession();
        session.Load(Doc(), "/tmp/a.acwscript");
        session.Edit(document => document.Description = "edited");

        session.Load(Doc("Other"), "/tmp/b.acwscript");

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.False(session.Dirty);
        Assert.Equal("/tmp/b.acwscript", session.Path);
        Assert.Equal("Other", session.Document.Name);
    }
}
