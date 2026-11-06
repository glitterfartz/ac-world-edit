namespace ACWorldEdit.Foundation;

// The conditions tables of TcTables.
public sealed partial class TcTables
{
    /// <summary>The server's conditions table. Only the gossip menu sources (14 and 15) are read and copied so far; the visual builder is Phase 13.</summary>
    public static readonly TableSchema Conditions = new("conditions", ["SourceTypeOrReferenceId", "SourceGroup", "SourceEntry", "SourceId", "ElseGroup", "ConditionTypeOrReference", "ConditionTarget", "ConditionValue1", "ConditionValue2", "ConditionValue3"],
    [
        ("SourceTypeOrReferenceId", ColumnKind.Int, 0),
        ("SourceGroup", ColumnKind.UInt, 0),
        ("SourceEntry", ColumnKind.Int, 0),
        ("SourceId", ColumnKind.Int, 0),
        ("ElseGroup", ColumnKind.UInt, 0),
        ("ConditionTypeOrReference", ColumnKind.Int, 0),
        ("ConditionTarget", ColumnKind.UInt, 0),
        ("ConditionValue1", ColumnKind.UInt, 0),
        ("ConditionValue2", ColumnKind.UInt, 0),
        ("ConditionValue3", ColumnKind.UInt, 0),
        ("ConditionStringValue1", ColumnKind.Text, 0),
        ("NegativeCondition", ColumnKind.UInt, 0),
        ("ErrorType", ColumnKind.UInt, 0),
        ("ErrorTextId", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 0),
        ("Comment", ColumnKind.Text, 0)
    ]);
}
