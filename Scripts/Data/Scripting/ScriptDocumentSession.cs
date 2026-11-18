using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit.Data.Scripting;

/// <summary>
/// One open document's mutable state: the graph, its file path, a bounded undo/redo history of
/// canonical snapshots and dirty tracking by hash. The canvas mutates through <see cref="Edit"/>,
/// which snapshots the state before the change, so one user action is one undo entry.
/// </summary>
public sealed class ScriptDocumentSession
{
    public const int HistoryLimit = 100;

    readonly List<string> undo = [];
    readonly List<string> redo = [];
    string savedHash;

    public ScriptDocumentSession() =>
        savedHash = ScriptDocumentJson.CanonicalHash(Document);

    public ScriptDocument Document { get; private set; } = ScriptDocument.Create("Untitled");
    public string? Path { get; private set; }
    public bool Dirty => ScriptDocumentJson.CanonicalHash(Document) != savedHash;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int UndoDepth => undo.Count;

    /// <summary>Adopt a document (open or new) and reset history; the loaded state is clean.</summary>
    public void Load(ScriptDocument document, string? path = null)
    {
        Document = document;
        Path = path;
        undo.Clear();
        redo.Clear();
        savedHash = ScriptDocumentJson.CanonicalHash(document);
    }

    /// <summary>Run one user action; the pre-edit state becomes the undo entry.</summary>
    public void Edit(Action<ScriptDocument> mutate)
    {
        undo.Add(ScriptDocumentJson.Save(Document));
        if (undo.Count > HistoryLimit)
        {
            undo.RemoveAt(0);
        }

        redo.Clear();
        mutate(Document);
    }

    public bool Undo()
    {
        if (undo.Count == 0)
        {
            return false;
        }

        redo.Add(ScriptDocumentJson.Save(Document));
        Document = ScriptDocumentJson.Load(undo[^1]);
        undo.RemoveAt(undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (redo.Count == 0)
        {
            return false;
        }

        undo.Add(ScriptDocumentJson.Save(Document));
        Document = ScriptDocumentJson.Load(redo[^1]);
        redo.RemoveAt(redo.Count - 1);
        return true;
    }

    /// <summary>The current state was just written to disk, optionally under a new path.</summary>
    public void MarkSaved(string? path = null)
    {
        if (path is not null)
        {
            Path = path;
        }

        savedHash = ScriptDocumentJson.CanonicalHash(Document);
    }
}
