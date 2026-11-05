namespace ACWorldEdit.Foundation;

/// <summary>One plain sentence saying what a <see cref="SpawnPreview"/> shows, for the top of the visibility card.</summary>
public static class PreviewSummary
{
    public static string Describe(SpawnPreview preview)
    {
        string who = !preview.RespectMasks ? "Showing every stored spawn, whatever its phase or difficulty."
            : preview.Dialect == WorldDialect.TrinityCore
                ? preview.PhaseId == 0 ? "Showing spawns outside any phase." : $"Showing spawns outside any phase and spawns in phase {preview.PhaseId}."
                : $"Showing spawns in {AcPhaseMasks.Describe(preview.PhaseMask)}.";
        string events = preview.Events switch
        {
            EventPreviewMode.None => "No events.",
            EventPreviewMode.All => "All events.",
            EventPreviewMode.Selected => preview.ActiveEvents.Length switch
            {
                0 => "No events chosen yet.",
                1 => "1 chosen event.",
                var n => $"{n} chosen events."
            },
            _ => "Events follow the database schedule."
        };
        var extras = new[]
        {
            (preview.IncludeHelpers, "invisible helpers"),
            (preview.IncludePools, "pool alternatives"),
            (preview.IncludeConditional, "conditional spawns"),
            (!preview.IncludeScripted, "without script-controlled spawns"),
            (preview.IncludeDespawned, "objects that start despawned")
        }.Where(e => e.Item1).Select(e => e.Item2).ToArray();
        return $"{who} {events}" + (extras.Length > 0 ? " Also: " + string.Join(", ", extras) + "." : "");
    }
}
