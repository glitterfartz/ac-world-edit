using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// One player choice as the editor sees it: the choice, its responses and their rewards (database
/// plus drafts), the names its references resolve to, and the highest choice ID for allocating a
/// new one.
/// </summary>
public sealed record PlayerChoiceSnapshot(RowSet Rows, ContentNames Names, string SourceKey, uint MaxChoiceId);

/// <summary>
/// One line of a conversation chain: the client line (or its hotfix override), the text it speaks,
/// and the server override row when one exists.
/// </summary>
public sealed record ConversationLineView(uint Id, uint NextLineId, uint BroadcastTextId, uint AnimKitId, int AdditionalDuration, string Text, Row? Override);

/// <summary>
/// One conversation as the editor sees it: the template, actors and line overrides (database plus
/// drafts), the resolved client chain with its text, the source-29 conditions per line, and the
/// highest conversation ID for allocating a new one. ClientAvailable is false when the retail
/// client (or a hotfix line) could not supply the chain.
/// </summary>
public sealed record ConversationSnapshot(RowSet Rows, ContentNames Names, string SourceKey, uint MaxConversationId,
    IReadOnlyList<ConversationLineView> Chain, IReadOnlyDictionary<uint, IReadOnlyList<Row>> Conditions, bool ClientAvailable);
