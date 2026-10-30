namespace ACWorldEdit.Foundation;

/// <summary>A client table a TrinityCore row can point into, for existence checks the world database cannot answer.</summary>
public enum ClientRecord
{
    Item,
    Spell,
    Currency,
    ExtendedCost,
    PlayerCondition,
    BonusList,
    SkillLine,
    Difficulty,
    Language,
    GossipNpcOption,

    /// <summary>A client file addressed by FileDataID (a texture, for example). Unknown, so true, when no client files are open.</summary>
    FileData
}
