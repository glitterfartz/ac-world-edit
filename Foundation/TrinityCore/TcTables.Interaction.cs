namespace ACWorldEdit.Foundation;

// The interaction tables of TcTables.
public sealed partial class TcTables
{
    public static readonly TableSchema GossipMenu = new("gossip_menu", ["MenuID", "TextID"], [("MenuID", ColumnKind.UInt, 0), ("TextID", ColumnKind.UInt, 0)]);

    /// <summary>npc_text holds only broadcast text IDs and their weights; the text itself is BroadcastText.</summary>
    public static readonly TableSchema NpcText = new("npc_text", ["ID"],
        [("ID", ColumnKind.UInt, 0), .. Enumerable.Range(0, 8).SelectMany(i => new[] { ($"BroadcastTextID{i}", ColumnKind.UInt, 0), ($"Probability{i}", ColumnKind.Float, 0) })]);

    public static readonly TableSchema GossipOption = new("gossip_menu_option", ["MenuID", "OptionID"],
    [
        ("MenuID", ColumnKind.UInt, 0),
        ("OptionID", ColumnKind.UInt, 0),
        ("GossipOptionID", ColumnKind.Int, 0),
        ("OptionNpc", ColumnKind.UInt, 0),
        ("OptionText", ColumnKind.Text, 0),
        ("OptionBroadcastTextID", ColumnKind.UInt, 0),
        ("Language", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.Int, 0),
        ("ActionMenuID", ColumnKind.UInt, 0),
        ("ActionPoiID", ColumnKind.UInt, 0),
        ("GossipNpcOptionID", ColumnKind.Int, 0),
        ("BoxCoded", ColumnKind.UInt, 0),
        ("BoxMoney", ColumnKind.ULong, 0),
        ("BoxText", ColumnKind.Text, 0),
        ("BoxBroadcastTextID", ColumnKind.UInt, 0),
        ("SpellID", ColumnKind.Int, 0),
        ("OverrideIconID", ColumnKind.Int, 0)
    ]);

    public static readonly TableSchema Trainer = new("trainer", ["Id"], [("Id", ColumnKind.UInt, 0), ("Type", ColumnKind.UInt, 0), ("Greeting", ColumnKind.Text, 0)]);

    public static readonly TableSchema TrainerSpell = new("trainer_spell", ["TrainerId", "SpellId"],
    [
        ("TrainerId", ColumnKind.UInt, 0),
        ("SpellId", ColumnKind.UInt, 0),
        ("MoneyCost", ColumnKind.UInt, 0),
        ("ReqSkillLine", ColumnKind.UInt, 0),
        ("ReqSkillRank", ColumnKind.UInt, 0),
        ("ReqAbility1", ColumnKind.UInt, 0),
        ("ReqAbility2", ColumnKind.UInt, 0),
        ("ReqAbility3", ColumnKind.UInt, 0),
        ("ReqLevel", ColumnKind.UInt, 0)
    ]);

    /// <summary>The item type (1 item, 2 currency) is part of the key.</summary>
    public static readonly TableSchema Vendor = new("npc_vendor", ["entry", "item", "ExtendedCost", "type"],
    [
        ("entry", ColumnKind.UInt, 0),
        ("item", ColumnKind.Int, 0),
        ("ExtendedCost", ColumnKind.UInt, 0),
        ("type", ColumnKind.UInt, 0),
        ("slot", ColumnKind.Int, 0),
        ("maxcount", ColumnKind.UInt, 0),
        ("incrtime", ColumnKind.UInt, 0),
        ("BonusListIDs", ColumnKind.Text, 0),
        ("PlayerConditionID", ColumnKind.UInt, 0),
        ("IgnoreFiltering", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema SpellClick = new("npc_spellclick_spells", ["npc_entry", "spell_id"], [("npc_entry", ColumnKind.UInt, 0), ("spell_id", ColumnKind.UInt, 0), ("cast_flags", ColumnKind.UInt, 0), ("user_type", ColumnKind.UInt, 0)]);
}
