namespace ACWorldEdit.Foundation;

// The smart tables of TcTables.
public sealed partial class TcTables
{
    /// <summary>
    /// smart_scripts of TrinityCore master: entryorguid is a bigint, event_param6 is gone, actions have
    /// a seventh parameter and each part has a string parameter; Difficulties replaces the difficulty flags.
    /// </summary>
    public static readonly TableSchema SmartScripts = new("smart_scripts", ["entryorguid", "source_type", "id", "link"],
    [
        ("entryorguid", ColumnKind.Long, 0),
        ("source_type", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("link", ColumnKind.UInt, 0),
        ("Difficulties", ColumnKind.Text, 100),
        ("event_type", ColumnKind.UInt, 0),
        ("event_phase_mask", ColumnKind.UInt, 0),
        ("event_chance", ColumnKind.UInt, 0),
        ("event_flags", ColumnKind.UInt, 0),
        ("event_param1", ColumnKind.UInt, 0),
        ("event_param2", ColumnKind.UInt, 0),
        ("event_param3", ColumnKind.UInt, 0),
        ("event_param4", ColumnKind.UInt, 0),
        ("event_param5", ColumnKind.UInt, 0),
        ("event_param_string", ColumnKind.Text, 255),
        ("action_type", ColumnKind.UInt, 0),
        ("action_param1", ColumnKind.UInt, 0),
        ("action_param2", ColumnKind.UInt, 0),
        ("action_param3", ColumnKind.UInt, 0),
        ("action_param4", ColumnKind.UInt, 0),
        ("action_param5", ColumnKind.UInt, 0),
        ("action_param6", ColumnKind.UInt, 0),
        ("action_param7", ColumnKind.UInt, 0),
        ("action_param_string", ColumnKind.Text, 255),
        ("target_type", ColumnKind.UInt, 0),
        ("target_param1", ColumnKind.UInt, 0),
        ("target_param2", ColumnKind.UInt, 0),
        ("target_param3", ColumnKind.UInt, 0),
        ("target_param4", ColumnKind.UInt, 0),
        ("target_param_string", ColumnKind.Text, 255),
        ("target_x", ColumnKind.Float, 0),
        ("target_y", ColumnKind.Float, 0),
        ("target_z", ColumnKind.Float, 0),
        ("target_o", ColumnKind.Float, 0),
        ("comment", ColumnKind.Text, 0)
    ]);

    /// <summary>creature_text of TrinityCore master: AzerothCore's columns plus SoundPlayType.</summary>
    public static readonly TableSchema CreatureText = new("creature_text", ["CreatureID", "GroupID", "ID"],
    [
        ("CreatureID", ColumnKind.UInt, 0),
        ("GroupID", ColumnKind.UInt, 0),
        ("ID", ColumnKind.UInt, 0),
        ("Text", ColumnKind.Text, 0),
        ("Type", ColumnKind.UInt, 0),
        ("Language", ColumnKind.UInt, 0),
        ("Probability", ColumnKind.Float, 0),
        ("Emote", ColumnKind.UInt, 0),
        ("Duration", ColumnKind.UInt, 0),
        ("Sound", ColumnKind.UInt, 0),
        ("SoundPlayType", ColumnKind.UInt, 0),
        ("BroadcastTextId", ColumnKind.Int, 0),
        ("TextRange", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    /// <summary>The script a client AreaTrigger (AreaTrigger.db2 ID) runs; the same table as AzerothCore's.</summary>
    public static readonly TableSchema AreaTriggerScripts = new("areatrigger_scripts", ["entry"], [("entry", ColumnKind.Int, 0), ("ScriptName", ColumnKind.Text, 64)]);

    /// <summary>
    /// A server-spawned AreaTrigger's ScriptName, a column of `areatrigger` edited on its own (like
    /// creature_ai) so switching a spawn to SmartAreaTriggerAI never rewrites its position or shape.
    /// </summary>
    public static readonly TableSchema AreaTriggerAI = new("areatrigger_ai", ["SpawnId"], [("SpawnId", ColumnKind.ULong, 0), ("ScriptName", ColumnKind.Text, 64)], "areatrigger");
}
