namespace ACWorldEdit.Foundation;

// The interaction tables of AcTables.
public sealed partial class AcTables
{
    public static readonly TableSchema GossipMenu = new("gossip_menu", ["MenuID", "TextID"], [("MenuID", ColumnKind.UInt, 0), ("TextID", ColumnKind.UInt, 0)]);

    public static readonly TableSchema NpcText = new("npc_text", ["ID"], [("ID", ColumnKind.UInt, 0), ("text0_0", ColumnKind.Text, 0), ("text0_1", ColumnKind.Text, 0), ("BroadcastTextID0", ColumnKind.Int, 0), ("Probability0", ColumnKind.Float, 0)]);

    public static readonly TableSchema GossipOption = new("gossip_menu_option", ["MenuID", "OptionID"], [("MenuID", ColumnKind.UInt, 0), ("OptionID", ColumnKind.UInt, 0), ("OptionIcon", ColumnKind.UInt, 0), ("OptionText", ColumnKind.Text, 0), ("OptionBroadcastTextID", ColumnKind.Int, 0), ("OptionType", ColumnKind.UInt, 0), ("OptionNpcFlag", ColumnKind.UInt, 0), ("ActionMenuID", ColumnKind.UInt, 0), ("ActionPoiID", ColumnKind.UInt, 0), ("BoxCoded", ColumnKind.UInt, 0), ("BoxMoney", ColumnKind.UInt, 0), ("BoxText", ColumnKind.Text, 0), ("BoxBroadcastTextID", ColumnKind.UInt, 0)], Added: [("BoxBroadcastTextID", "0")]);

    public static readonly TableSchema Vendor = new("npc_vendor", ["entry", "item", "ExtendedCost"], [("entry", ColumnKind.UInt, 0), ("item", ColumnKind.Int, 0), ("ExtendedCost", ColumnKind.UInt, 0), ("slot", ColumnKind.Int, 0), ("maxcount", ColumnKind.UInt, 0), ("incrtime", ColumnKind.UInt, 0)]);

    public static readonly TableSchema DefaultTrainer = new("creature_default_trainer", ["CreatureId"], [("CreatureId", ColumnKind.UInt, 0), ("TrainerId", ColumnKind.UInt, 0)]);

    public static readonly TableSchema Trainer = new("trainer", ["Id"], [("Id", ColumnKind.UInt, 0), ("Type", ColumnKind.UInt, 0), ("Requirement", ColumnKind.UInt, 0), ("Greeting", ColumnKind.Text, 0)]);

    public static readonly TableSchema TrainerSpell = new("trainer_spell", ["TrainerId", "SpellId"], [("TrainerId", ColumnKind.UInt, 0), ("SpellId", ColumnKind.UInt, 0), ("MoneyCost", ColumnKind.UInt, 0), ("ReqSkillLine", ColumnKind.UInt, 0), ("ReqSkillRank", ColumnKind.UInt, 0), ("ReqAbility1", ColumnKind.UInt, 0), ("ReqAbility2", ColumnKind.UInt, 0), ("ReqAbility3", ColumnKind.UInt, 0), ("ReqLevel", ColumnKind.UInt, 0)], Added: [("ReqAbility1", "0"), ("ReqAbility2", "0"), ("ReqAbility3", "0")]);

    public static readonly TableSchema SpellClick = new("npc_spellclick_spells", ["npc_entry", "spell_id"], [("npc_entry", ColumnKind.UInt, 0), ("spell_id", ColumnKind.UInt, 0), ("cast_flags", ColumnKind.UInt, 0), ("user_type", ColumnKind.UInt, 0)]);
}
