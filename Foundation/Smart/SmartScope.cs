namespace ACWorldEdit.Foundation;

/// <summary>
/// Which script a rule belongs to: a creature entry (all spawns), one spawn (negative GUID), a client
/// AreaTrigger (source_type 2), a timed action list (source_type 9) started by "Run action list", or a
/// TrinityCore server-spawned AreaTrigger's template (source_type 11, 12 for custom templates).
/// </summary>
public readonly record struct SmartScope(int EntryOrGuid, uint SourceType)
{
    public const uint Creature = 0, AreaTrigger = 2, ActionList = 9, AreaTriggerEntity = 11, AreaTriggerEntityCustom = 12;
    public bool IsSpawn => SourceType == Creature && EntryOrGuid < 0;

    /// <summary>Any AreaTrigger script: a client trigger or a spawned trigger's template.</summary>
    public bool IsAreaTrigger => SourceType is AreaTrigger or AreaTriggerEntity or AreaTriggerEntityCustom;
    public bool IsTriggerEntity => SourceType is AreaTriggerEntity or AreaTriggerEntityCustom;
    public bool IsActionList => SourceType == ActionList;
    public string Describe(string creature) => IsActionList ? $"Action list {EntryOrGuid}" : IsTriggerEntity ? $"AreaTrigger template {EntryOrGuid}" : IsAreaTrigger ? $"AreaTrigger {EntryOrGuid}" : IsSpawn ? $"This spawn only (#{-EntryOrGuid})" : $"All {creature} spawns";
}
