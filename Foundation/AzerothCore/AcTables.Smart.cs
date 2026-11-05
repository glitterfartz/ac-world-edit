namespace ACWorldEdit.Foundation;

// The smart tables of AcTables.
public sealed partial class AcTables
{
    public static readonly TableSchema SmartScripts = new("smart_scripts", ["entryorguid", "source_type", "id", "link"], [
        ("entryorguid", ColumnKind.Int, 0),
        ("source_type", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("link", ColumnKind.UInt, 0),
        ("event_type", ColumnKind.UInt, 0),
        ("event_phase_mask", ColumnKind.UInt, 0),
        ("event_chance", ColumnKind.UInt, 0),
        ("event_flags", ColumnKind.UInt, 0),
        ("event_param1", ColumnKind.UInt, 0),
        ("event_param2", ColumnKind.UInt, 0),
        ("event_param3", ColumnKind.UInt, 0),
        ("event_param4", ColumnKind.UInt, 0),
        ("event_param5", ColumnKind.UInt, 0),
        ("event_param6", ColumnKind.UInt, 0),
        ("action_type", ColumnKind.UInt, 0),
        ("action_param1", ColumnKind.UInt, 0),
        ("action_param2", ColumnKind.UInt, 0),
        ("action_param3", ColumnKind.UInt, 0),
        ("action_param4", ColumnKind.UInt, 0),
        ("action_param5", ColumnKind.UInt, 0),
        ("action_param6", ColumnKind.UInt, 0),
        ("target_type", ColumnKind.UInt, 0),
        ("target_param1", ColumnKind.UInt, 0),
        ("target_param2", ColumnKind.UInt, 0),
        ("target_param3", ColumnKind.UInt, 0),
        ("target_param4", ColumnKind.UInt, 0),
        ("target_x", ColumnKind.Float, 0),
        ("target_y", ColumnKind.Float, 0),
        ("target_z", ColumnKind.Float, 0),
        ("target_o", ColumnKind.Float, 0),
        ("comment", ColumnKind.Text, 0)
    ]);

    /// <summary>
    /// Lines a creature can say through SmartAI "Talk" (grouped by GroupID; the server picks one
    /// line of a group by Probability).
    /// </summary>
    public static readonly TableSchema CreatureText = new("creature_text", ["CreatureID", "GroupID", "ID"], [
        ("CreatureID", ColumnKind.UInt, 0),
        ("GroupID", ColumnKind.UInt, 0),
        ("ID", ColumnKind.UInt, 0),
        ("Text", ColumnKind.Text, 0),
        ("Type", ColumnKind.UInt, 0),
        ("Language", ColumnKind.Int, 0),
        ("Probability", ColumnKind.Float, 0),
        ("Emote", ColumnKind.UInt, 0),
        ("Duration", ColumnKind.UInt, 0),
        ("Sound", ColumnKind.UInt, 0),
        ("BroadcastTextId", ColumnKind.Int, 0),
        ("TextRange", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    /// <summary>
    /// The script an AreaTrigger runs (ObjectMgr::LoadAreaTriggerScripts): a C++ AreaTriggerScript
    /// name, or "SmartTrigger" for its smart_scripts rows (source_type 2). One name per trigger.
    /// </summary>
    public static readonly TableSchema AreaTriggerScripts = new("areatrigger_scripts", ["entry"], [
        ("entry", ColumnKind.Int, 0),
        ("ScriptName", ColumnKind.Text, 64)
    ]);
}
