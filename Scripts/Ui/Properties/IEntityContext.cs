using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// What the properties window frame and <see cref="PageRenderer"/> need from the entity being edited: its
/// effective rows, the commit path, the status line and a change signal. <see cref="CreatureContext"/> is the
/// first implementation; an entity kind keeps its own loading and read model behind this.
/// </summary>
public interface IEntityContext
{
    WorldDialect Dialect { get; }
    ClientIcons Icons { get; }

    /// <summary>The entity's rows as they are now: the database snapshot with the open draft applied.</summary>
    RowSet Effective { get; }

    /// <summary>The window's status line; sections set it and call <see cref="Redraw"/>.</summary>
    string Status { get; set; }

    /// <summary>Data, status or mode changed: sections and the window frame redraw.</summary>
    event Action? Changed;

    void Redraw();

    /// <summary>Recompute the view from the snapshot and draft, then redraw.</summary>
    void Rebuild();

    /// <summary>Stop any load still running (the window left the tree).</summary>
    void Cancel();

    /// <summary>Validate and record one command; false when the edits were rejected (the status says why).</summary>
    bool Commit(string label, IEnumerable<RowEdit> edits);

    /// <summary>Run a parse/build step from a control; bad numbers show a message instead of throwing.</summary>
    void Try(Action action);
}
