namespace ACWorldEdit.Foundation;

/// <summary>
/// One planned change: the guarded statement and the preflight/post-check SELECT for each direction.
/// </summary>
public sealed record SqlStep(string Label, string ApplyStatement, string RevertStatement, string ApplyCheck, string RevertCheck);

/// <summary>
/// The statements of one dialect for the edits that are not plain table rows: graveyards. Each
/// method returns guarded steps in the form <see cref="SqlExport"/> assembles, or throws
/// <see cref="InvalidDataException"/> when the dialect cannot export that edit yet. Waypoint paths
/// are plain table rows since movement authoring moved them onto row edits.
/// </summary>
public interface ISqlDialect
{
    IEnumerable<SqlStep> Graveyard(GraveyardEdit edit);
}
