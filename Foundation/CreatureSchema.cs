namespace ACWorldEdit.Foundation;

/// <summary>
/// What the creature template editor needs to know about one dialect: the classification choices and
/// the npcflag bits (64-bit so TrinityCore's NPCFlags2 fits).
/// </summary>
public sealed record CreatureSchema((int Value, string Name)[] Classifications, (ulong Bit, string Name)[] NpcFlags)
{
    /// <summary>AzerothCore's creature_template.rank and the 32-bit npcflag.</summary>
    public static readonly CreatureSchema Ac = new(
        [(0, "Normal"), (1, "Elite"), (2, "Rare Elite"), (3, "Boss"), (4, "Rare")],
        NpcInteractionFlags());

    /// <summary>TrinityCore master Classification (trinitycore.info creature_template) and the 64-bit npcflag.</summary>
    public static readonly CreatureSchema Tc = new(
        [(0, "Normal"), (1, "Elite"), (2, "Rare Elite"), (3, "Obsolete"), (4, "Rare"), (5, "Trivial"), (6, "Minus Mob")],
        TcCreatureView.NpcFlagBits);

    static (ulong, string)[] NpcInteractionFlags() => Foundation.NpcFlags.All.Select(f => ((ulong)f.Bit, f.Name)).ToArray();
}
