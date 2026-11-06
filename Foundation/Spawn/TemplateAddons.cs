namespace ACWorldEdit.Foundation;

/// <summary>
/// A creature template's addon row (`creature_template_addon`: mount, stance, weapons, emote, auras) as
/// row edits on the shared content draft, the way <see cref="SpawnPoses"/> keeps spawn rows. Every spawn of
/// the template shows this appearance unless the spawn has its own `creature_addon` row, which replaces it
/// completely, so an edit here changes the spawns without one.
/// </summary>
public sealed class TemplateAddons
{
    readonly ContentDrafts drafts;
    readonly Func<string, WorldDialect>? dialectOf;
    readonly Dictionary<string, RowSet> snapshots = [];

    public TemplateAddons(ContentDrafts drafts, Func<string, WorldDialect>? dialectOf = null)
    {
        this.drafts = drafts;
        this.dialectOf = dialectOf;
    }

    public bool Available => drafts.Draft != null;

    TableSchema Table(string source) => drafts.Draft!.Tables(source).Find("creature_template_addon");

    RowSet Snapshot(string source) => snapshots.TryGetValue(source, out var rows) ? rows : snapshots[source] = new RowSet();

    static string Key(uint entry) => entry.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Remember the database's addon row of a template (none: the template has no addon) as the baseline of later edits.</summary>
    public void Load(string source, uint entry, Row? row)
    {
        if (!Available)
        {
            return;
        }

        var table = Table(source);
        var snapshot = Snapshot(source);
        if (row != null)
        {
            snapshot.Set(table, row);
        }

        loaded.Add((source, entry));
    }

    readonly HashSet<(string, uint)> loaded = [];

    /// <summary>Whether the database's answer for this template (a row, or none) has been loaded, so it can be edited.</summary>
    public bool IsLoaded(string source, uint entry) => Available && loaded.Contains((source, entry));

    /// <summary>The template's addon row with the draft applied, or null when it has none.</summary>
    public Row? RowOf(string source, uint entry) => drafts.Draft?.Effective(source, Snapshot(source)).Get(Table(source), Key(entry));

    /// <summary>
    /// Many templates' addon rows with the draft applied, sharing one effective view. The per-frame
    /// appearance pass uses this; one lookup per marker would clone the whole snapshot for every
    /// creature on screen.
    /// </summary>
    public IReadOnlyDictionary<uint, Row?> RowsOf(string source, IEnumerable<uint> entries)
    {
        if (!Available)
        {
            return new Dictionary<uint, Row?>();
        }

        var table = Table(source);
        var effective = drafts.Draft!.Effective(source, Snapshot(source));
        var map = new Dictionary<uint, Row?>();
        foreach (uint entry in entries.Distinct())
        {
            map[entry] = effective.Get(table, Key(entry));
        }

        return map;
    }

    /// <summary>
    /// Change the template's addon row as one undoable command. A template without a row starts from
    /// <paramref name="blank"/> and gets the row inserted; a change that leaves the row as it was records nothing.
    /// </summary>
    public (bool Ok, string Message) Edit(object? sender, string source, uint entry, string label, Row blank, Func<Row, Row> change)
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        if (!IsLoaded(source, entry))
        {
            return (false, "The template's appearance is still loading; try again.");
        }

        var table = Table(source);
        var existing = RowOf(source, entry);
        try
        {
            var after = change(existing ?? blank);
            if (after == (existing ?? blank))
            {
                return (true, "");
            }

            var snapshot = Snapshot(source);
            var edit = existing == null ? InteractionEdits.Insert(table, after) : InteractionEdits.Update(table, existing, after);
            Func<RowSet, IEnumerable<InteractionIssue>> rules = dialectOf == null ? _ => [] : rows => SpawnRules.CheckTemplateAddons(dialectOf(source), rows, table);
            return drafts.Commit(sender, source, snapshot, drafts.Draft.Effective(source, snapshot), label, [edit], rules);
        }
        catch (InvalidDataException error)
        {
            return (false, "Not saved: " + error.Message);
        }
    }

    public string Undo(object? sender) => drafts.History(sender, false);

    public string Redo(object? sender) => drafts.History(sender, true);
}
