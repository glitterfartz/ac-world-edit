namespace ACWorldEdit.Foundation;

/// <summary>
/// Maps a creature_addon / creature_template_addon row to what the viewport draws: the
/// animation for its stance and emote, and the mount it rides. Death plays once and holds its
/// last frame like the client; every other clip loops. Sheath state has no body-visual
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

    /// <summary>Riders sit with clip 91 (Mount); verified in the wrath DBC and stable across builds.</summary>
    public const ushort AnimMount = 91;
    /// <summary>Airborne mounts flap with clip 135 (Fly); verified in the wrath DBC and stable across builds.</summary>
    public const ushort AnimFly = 135;

    /// <summary>The looping AnimationData clip for a StandState value (0 is none: keep standing),
    /// verified against the wrath DBC: 97 SitGround, 102 SitChairLow, 100 Sleep, 115 KneelLoop.</summary>
    public static ushort StandClip(uint stand) => stand switch
    {
        1 => 97,
        2 => 102,
        3 => 100,
        4 => 97,
        5 => 97,
        6 => 97,
        7 => AnimDeath,
        8 => 115,
        _ => AnimStand
    };

    /// <summary>
    /// Pick the rider's clip: dead always plays Death (the shader holds its last frame instead
    /// of looping, like the client), else the emote's clip when it names
    /// one the model has, else the seat clip when mounted and the model has it, else the stance
    /// clip when the model has it, else Stand. A clip the model lacks is never picked: the loader
    /// would show the reference pose.
    /// </summary>
    public static ushort RiderClip(uint stand, ushort? emoteAnim, bool mounted, Func<ushort, bool> available)
    {
        if (stand == 7)
        {
            return AnimDeath;
        }

        if (emoteAnim is ushort emote && emote != 0 && available(emote))
        {
            return emote;
        }

        if (mounted && available(AnimMount))
        {
            return AnimMount;
        }

        ushort stance = StandClip(stand);
        if (stance != AnimStand && available(stance))
        {
            return stance;
        }

        return AnimStand;
    }

    /// <summary>Pick the mount's looping clip: Fly when airborne and the model has the clip, else Stand.</summary>
    public static ushort MountClip(bool flying, Func<ushort, bool> available) =>
        flying && available(AnimFly) ? AnimFly : AnimStand;

    /// <summary>The mount: a display ID, or a creature entry whose model it rides (TrinityCore only).</summary>
    public static (uint Display, uint Creature) MountOf(WorldDialect dialect, Row addon) =>
        (addon.U("mount"), dialect == WorldDialect.TrinityCore ? addon.U("MountCreatureID") : 0);
}
