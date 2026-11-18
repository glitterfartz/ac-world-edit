using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// One player choice as the editor sees it: the choice, its responses and their rewards (database
/// plus drafts), the names its references resolve to, and the highest choice ID for allocating a
/// new one.
/// </summary>
public sealed record PlayerChoiceSnapshot(RowSet Rows, ContentNames Names, string SourceKey, uint MaxChoiceId);
