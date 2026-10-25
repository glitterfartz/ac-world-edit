using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// A spirit healer creature near a graveyard. Healers are ordinary creature spawns
/// (npcflag & UNIT_NPC_FLAG_SPIRITHEALER); moving a graveyard does not move them.
/// </summary>
public sealed record SpiritHealer(ulong Guid, uint Entry, string Name, double X, double Y, double Z);
/// <summary>
/// One graveyard source row with its links and nearby healers, as read from the database. TrinityCore
/// adds two facts: LockedZones are links whose conditions are more than one plain team row
/// (battleground world states, area checks), so their faction cannot be edited or the link removed;
/// TeleportUses counts the area trigger teleports that target the same world_safe_locs row, which
/// a move would carry along.
/// </summary>
public sealed record GraveyardSite(GraveyardRecord Record, SpiritHealer[] Healers, uint[]? LockedZones = null, int TeleportUses = 0)
{
    public bool IsLocked(uint zone) => LockedZones?.Contains(zone) == true;
}
public sealed record GraveyardRegionResult(GraveyardSite[] Sites, bool Truncated, string SourceKey);
