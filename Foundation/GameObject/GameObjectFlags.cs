namespace ACWorldEdit.Foundation;

/// <summary>
/// Named bits of `gameobject_template_addon.flags`. The two servers reuse some bits for
/// different meanings, so each dialect gets its own list; the raw value keeps every bit.
/// </summary>
public static class GameObjectFlags
{
    static readonly FlagBit[] Shared =
    [
        new(0x1, "In use", "Interaction is disabled while the object animates."),
        new(0x2, "Locked", "Requires a key or spell; shows \"Locked\" in the tooltip."),
        new(0x4, "Conditional interaction", "The client needs a condition before it can interact."),
        new(0x8, "Transport", "The object can transport players (elevator, boat, car)."),
        new(0x10, "Not selectable", "Players cannot select it, even in GM mode."),
        new(0x20, "Never despawns", "The object stays loaded; typical for doors."),
        new(0x200, "Damaged", "Shows the damaged state (destructible buildings)."),
        new(0x400, "Destroyed", "Shows the destroyed state (destructible buildings).")
    ];

    static readonly FlagBit[] TrinityExtra =
    [
        new(0x40, "AI obstacle", "Registers the object with the client's AIObstacleMgr."),
        new(0x80, "Freeze animation", "Stops the object's animation."),
        new(0x4000, "Ignore state for use spell", "Allows the use spell without checking the current state."),
        new(0x8000, "Ignore model for distance", "The client ignores the model bounds for interaction distance."),
        new(0x40000, "Ignore state except unlocked", "Allows opening/closing, but not unlocking an unlocked object."),
        new(0x80000, "Use template model distance", "Interaction distance uses the template model."),
        new(0x100000, "Map object", "The object uses map-object model loading."),
        new(0x200000, "Multi use", "The multi-use equivalent of In use."),
        new(0x4000000, "Low priority selection", "The client gives this object lower cursor priority.")
    ];

    public static FlagBit[] For(WorldDialect dialect) =>
        dialect == WorldDialect.TrinityCore ? [.. Shared, .. TrinityExtra] : Shared;
}
