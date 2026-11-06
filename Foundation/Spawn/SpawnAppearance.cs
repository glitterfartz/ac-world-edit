namespace ACWorldEdit.Foundation;

/// <summary>
/// Maps a creature_addon / creature_template_addon row to what the viewport draws: the looping
/// animation for its stance and emote, and the mount it rides. Sheath state has no body-visual
/// change (weapons are not composed yet), so it is stored but ignored here.
/// </summary>
public static class SpawnAppearance
{
    public const ushort AnimStand = 0;
    /// <summary>Death is animation 1 in every client build.</summary>
    public const ushort AnimDeath = 1;

    /// <summary>The stance value: StandState on TrinityCore, the low byte of bytes1 on AzerothCore.</summary>
    public static uint StandStateOf(WorldDialect dialect, Row addon) =>
        dialect == WorldDialect.TrinityCore ? addon.U("StandState") : addon.U("bytes1") & 0xFFu;

    /// <summary>The emote the creature loops.</summary>
    public static uint EmoteOf(Row addon) => addon.U("emote");

    /// <summary>Pick the looping animation: dead always plays Death; else the emote's animation when
    /// the client names one, else the stance's, else Stand. Unknown names fall back instead of
    /// failing, and the model loader shows the reference pose for an animation the model lacks.</summary>
    public static ushort AnimationFor(uint stand, uint emote, Func<string, ushort?> animByName, Func<uint, string?> emoteName)
    {
        if (stand == 7)
        {
            return AnimDeath;
        }

        if (emote != 0 && emoteName(emote) is { } name && animByName(name) is ushort fromEmote)
        {
            return fromEmote;
        }

        if (stand != 0 && StandAnimation(stand) is { } stance && animByName(stance) is ushort fromStance)
        {
            return fromStance;
        }

        return AnimStand;
    }

    /// <summary>The AnimationData name a stance best matches; the caller resolves it (case-insensitively)
    /// against the client's names and falls back when it is absent.</summary>
    public static string? StandAnimation(uint stand) => stand switch
    {
        1 => "Sit",
        2 => "SitChair",
        3 => "Sleep",
        4 => "Sit",
        5 => "Sit",
        6 => "Sit",
        7 => "Death",
        8 => "Kneel",
        9 => "Submerged",
        _ => null
    };
}
