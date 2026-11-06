namespace ACWorldEdit.Foundation;

// The GameObject tables of TcTables. Data0-Data34 and the scaling columns are signed;
// StringId is nullable. VerifiedBuild is deliberately absent.
public sealed partial class TcTables
{
    public static readonly TableSchema GameObjectTemplate = new("gameobject_template", ["entry"],
    [
        ("entry", ColumnKind.UInt, 0),
        ("type", ColumnKind.UInt, 0),
        ("displayId", ColumnKind.UInt, 0),
        ("name", ColumnKind.Text, 200),
        ("IconName", ColumnKind.Text, 100),
        ("castBarCaption", ColumnKind.Text, 100),
        ("unk1", ColumnKind.Text, 100),
        ("size", ColumnKind.Float, 0),
        ("Data0", ColumnKind.Int, 0),
        ("Data1", ColumnKind.Int, 0),
        ("Data2", ColumnKind.Int, 0),
        ("Data3", ColumnKind.Int, 0),
        ("Data4", ColumnKind.Int, 0),
        ("Data5", ColumnKind.Int, 0),
        ("Data6", ColumnKind.Int, 0),
        ("Data7", ColumnKind.Int, 0),
        ("Data8", ColumnKind.Int, 0),
        ("Data9", ColumnKind.Int, 0),
        ("Data10", ColumnKind.Int, 0),
        ("Data11", ColumnKind.Int, 0),
        ("Data12", ColumnKind.Int, 0),
        ("Data13", ColumnKind.Int, 0),
        ("Data14", ColumnKind.Int, 0),
        ("Data15", ColumnKind.Int, 0),
        ("Data16", ColumnKind.Int, 0),
        ("Data17", ColumnKind.Int, 0),
        ("Data18", ColumnKind.Int, 0),
        ("Data19", ColumnKind.Int, 0),
        ("Data20", ColumnKind.Int, 0),
        ("Data21", ColumnKind.Int, 0),
        ("Data22", ColumnKind.Int, 0),
        ("Data23", ColumnKind.Int, 0),
        ("Data24", ColumnKind.Int, 0),
        ("Data25", ColumnKind.Int, 0),
        ("Data26", ColumnKind.Int, 0),
        ("Data27", ColumnKind.Int, 0),
        ("Data28", ColumnKind.Int, 0),
        ("Data29", ColumnKind.Int, 0),
        ("Data30", ColumnKind.Int, 0),
        ("Data31", ColumnKind.Int, 0),
        ("Data32", ColumnKind.Int, 0),
        ("Data33", ColumnKind.Int, 0),
        ("Data34", ColumnKind.Int, 0),
        ("ContentTuningId", ColumnKind.Int, 0),
        ("RequiredLevel", ColumnKind.Int, 0),
        ("AIName", ColumnKind.Text, 64),
        ("ScriptName", ColumnKind.Text, 64),
        ("StringId", ColumnKind.Text, 64)
    ], Nullable: ["StringId"]);

    public static readonly TableSchema GameObjectTemplateAddon = new("gameobject_template_addon", ["entry"], [
        ("entry", ColumnKind.UInt, 0),
        ("faction", ColumnKind.UInt, 0),
        ("flags", ColumnKind.UInt, 0),
        ("mingold", ColumnKind.UInt, 0),
        ("maxgold", ColumnKind.UInt, 0),
        ("artkit0", ColumnKind.Int, 0),
        ("artkit1", ColumnKind.Int, 0),
        ("artkit2", ColumnKind.Int, 0),
        ("artkit3", ColumnKind.Int, 0),
        ("artkit4", ColumnKind.Int, 0),
        ("WorldEffectID", ColumnKind.UInt, 0),
        ("AIAnimKitID", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameObjectQuestStarter = new("gameobject_queststarter", ["id", "quest"], [
        ("id", ColumnKind.UInt, 0),
        ("quest", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameObjectQuestEnder = new("gameobject_questender", ["id", "quest"], [
        ("id", ColumnKind.UInt, 0),
        ("quest", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameObjectQuestItem = new("gameobject_questitem", ["GameObjectEntry", "Idx"], [
        ("GameObjectEntry", ColumnKind.UInt, 0),
        ("Idx", ColumnKind.UInt, 0),
        ("ItemId", ColumnKind.UInt, 0)
    ]);
}
