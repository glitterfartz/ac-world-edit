using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

public sealed record AreaTriggerRegionResult(AreaTriggerRecord[] Triggers, bool Truncated, string SourceKey);
