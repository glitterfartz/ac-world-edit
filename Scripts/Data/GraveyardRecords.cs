using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// A spirit healer creature near a graveyard. Healers are ordinary creature spawns
/// (npcflag & UNIT_NPC_FLAG_SPIRITHEALER); moving a graveyard does not move them.
/// </summary>
public sealed record SpiritHealer(ulong Guid, uint Entry, string Name, double X, double Y, double Z);
/// <summary>
/// One graveyard source row with its links and nearby healers, as read from the database.
/// </summary>
public sealed record GraveyardSite(GraveyardRecord Record, SpiritHealer[] Healers);
public sealed record GraveyardRegionResult(GraveyardSite[] Sites, bool Truncated, string SourceKey);
