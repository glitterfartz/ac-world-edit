using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// smart_scripts row with named accessors. Parameters are 1-based to match the catalog sentences.
/// </summary>
public sealed class SmartRule(Row row)
{
    public Row Row => row;
    public int EntryOrGuid => row.I("entryorguid");
    public uint SourceType => row.U("source_type");
    public uint Id => row.U("id");
    public uint Link => row.U("link");
    public uint Event => row.U("event_type");
    public uint Phases => row.U("event_phase_mask");
    public uint Chance => row.U("event_chance");
    public uint Flags => row.U("event_flags");
    public uint Action => row.U("action_type");
    public uint Target => row.U("target_type");
    public string Comment => row.T("comment");
    // A dialect without that parameter column (AzerothCore has no action_param7, TrinityCore no
    // event_param6) reads as 0, so one description routine serves both.
    public uint EventParam(int i) => Param($"event_param{i}");
    public uint ActionParam(int i) => Param($"action_param{i}");
    public uint TargetParam(int i) => Param($"target_param{i}");

    /// <summary>TrinityCore's string parameters (empty in AzerothCore).</summary>
    public string EventString => Text("event_param_string");
    public string ActionString => Text("action_param_string");
    public string TargetString => Text("target_param_string");

    uint Param(string column) => row.Values.TryGetValue(column, out var value) ? uint.Parse(value, CultureInfo.InvariantCulture) : 0;
    string Text(string column) => row.Values.GetValueOrDefault(column) ?? "";
    public float X => row.F("target_x");
    public float Y => row.F("target_y");
    public float Z => row.F("target_z");
    public float O => row.F("target_o");
    public SmartScope Scope => new(EntryOrGuid, SourceType);
}
