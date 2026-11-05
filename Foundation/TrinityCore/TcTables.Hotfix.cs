namespace ACWorldEdit.Foundation;

// The hotfix tables of TcTables.
public sealed partial class TcTables
{
    /// <summary>
    /// A row of the <c>hotfixes</c> database's broadcast_text, keyed (ID, VerifiedBuild) as the server's
    /// table is. New rows use <see cref="TcBroadcastText.CustomBuild"/>: the server loads rows with a
    /// VerifiedBuild of 0 or less as its own, after the client's.
    /// </summary>
    public static readonly TableSchema BroadcastText = new("broadcast_text", ["ID", "VerifiedBuild"],
    [
        ("Text", ColumnKind.Text, 0),
        ("Text1", ColumnKind.Text, 0),
        ("ID", ColumnKind.UInt, 0),
        ("LanguageID", ColumnKind.Int, 0),
        ("ConditionID", ColumnKind.Int, 0),
        ("EmotesID", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.Int, 0),
        ("ChatBubbleDurationMs", ColumnKind.UInt, 0),
        ("VoiceOverPriorityID", ColumnKind.Int, 0),
        ("SoundKitID1", ColumnKind.UInt, 0),
        ("SoundKitID2", ColumnKind.UInt, 0),
        ("EmoteID1", ColumnKind.UInt, 0),
        ("EmoteID2", ColumnKind.UInt, 0),
        ("EmoteID3", ColumnKind.UInt, 0),
        ("EmoteDelay1", ColumnKind.UInt, 0),
        ("EmoteDelay2", ColumnKind.UInt, 0),
        ("EmoteDelay3", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.Int, 0)
    ], "hotfixes.broadcast_text");

    /// <summary>The <c>hotfixes.hotfix_data</c> row that announces a DB2 row to clients (TableHash names the table, RecordId the row).</summary>
    public static readonly TableSchema HotfixData = new("hotfix_data", ["Id", "TableHash", "RecordId"],
    [
        ("Id", ColumnKind.Int, 0),
        ("UniqueId", ColumnKind.UInt, 0),
        ("TableHash", ColumnKind.UInt, 0),
        ("RecordId", ColumnKind.Int, 0),
        ("Status", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.Int, 0)
    ], "hotfixes.hotfix_data");
}
