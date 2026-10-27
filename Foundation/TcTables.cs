namespace ACWorldEdit.Foundation;

/// <summary>
/// The TrinityCore master creature tables the creature editor reads, with primary keys and column
/// types from TDB 1210.26091. The template page edits creature_template and creature_template_difficulty;
/// the other tables are still read-only in the editor. Waypoint tables are not listed and stay uneditable. Text
/// limits follow the column types (varchar); 0 means unbounded (mediumtext).
/// </summary>
public sealed class TcTables : ITableCatalog
{
    public static readonly TcTables Instance = new();

    /// <summary>The template's descriptive and behavioural columns; per-difficulty data lives in <see cref="CreatureDifficulty"/>.</summary>
    public static readonly TableSchema CreatureTemplate = new("creature_template", ["entry"],
    [
        ("entry", ColumnKind.UInt, 0),
        ("name", ColumnKind.Text, 0),
        ("subname", ColumnKind.Text, 0),
        ("IconName", ColumnKind.Text, 64),
        ("npcflag", ColumnKind.ULong, 0),
        ("faction", ColumnKind.UInt, 0),
        ("Classification", ColumnKind.UInt, 0),
        ("unit_class", ColumnKind.UInt, 0),
        ("type", ColumnKind.UInt, 0),
        ("family", ColumnKind.Int, 0),
        ("trainer_class", ColumnKind.UInt, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("unit_flags2", ColumnKind.UInt, 0),
        ("unit_flags3", ColumnKind.UInt, 0),
        ("speed_walk", ColumnKind.Float, 0),
        ("speed_run", ColumnKind.Float, 0),
        ("scale", ColumnKind.Float, 0),
        ("RequiredExpansion", ColumnKind.Int, 0),
        ("VehicleId", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("AIName", ColumnKind.Text, 64),
        ("ScriptName", ColumnKind.Text, 64),
        ("flags_extra", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema CreatureDifficulty = new("creature_template_difficulty", ["Entry", "DifficultyID"],
    [
        ("Entry", ColumnKind.UInt, 0),
        ("DifficultyID", ColumnKind.Int, 0),
        ("LevelScalingDeltaMin", ColumnKind.Int, 0),
        ("LevelScalingDeltaMax", ColumnKind.Int, 0),
        ("ContentTuningID", ColumnKind.Int, 0),
        ("HealthScalingExpansion", ColumnKind.Int, 0),
        ("HealthModifier", ColumnKind.Float, 0),
        ("ManaModifier", ColumnKind.Float, 0),
        ("ArmorModifier", ColumnKind.Float, 0),
        ("DamageModifier", ColumnKind.Float, 0),
        ("CreatureDifficultyID", ColumnKind.Int, 0),
        ("TypeFlags", ColumnKind.UInt, 0),
        ("TypeFlags2", ColumnKind.UInt, 0),
        ("TypeFlags3", ColumnKind.UInt, 0),
        ("LootID", ColumnKind.UInt, 0),
        ("PickPocketLootID", ColumnKind.UInt, 0),
        ("SkinLootID", ColumnKind.UInt, 0),
        ("GoldMin", ColumnKind.UInt, 0),
        ("GoldMax", ColumnKind.UInt, 0),
        ("StaticFlags1", ColumnKind.UInt, 0),
        ("StaticFlags2", ColumnKind.UInt, 0),
        ("StaticFlags3", ColumnKind.UInt, 0),
        ("StaticFlags4", ColumnKind.UInt, 0),
        ("StaticFlags5", ColumnKind.UInt, 0),
        ("StaticFlags6", ColumnKind.UInt, 0),
        ("StaticFlags7", ColumnKind.UInt, 0),
        ("StaticFlags8", ColumnKind.UInt, 0)
    ], Added: [("LevelScalingDeltaMin", "0"), ("LevelScalingDeltaMax", "0")]);

    /// <summary>A creature's gossip menus: one row per menu, so a creature can have several.</summary>
    public static readonly TableSchema CreatureGossip = new("creature_template_gossip", ["CreatureID", "MenuID"], [("CreatureID", ColumnKind.UInt, 0), ("MenuID", ColumnKind.UInt, 0)]);
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

    /// <summary>A trainer is reached through a gossip option (MenuID, OptionID); 0/0 is the creature's default trainer.</summary>
    public static readonly TableSchema CreatureTrainer = new("creature_trainer", ["CreatureID", "MenuID", "OptionID"], [("CreatureID", ColumnKind.UInt, 0), ("MenuID", ColumnKind.UInt, 0), ("OptionID", ColumnKind.UInt, 0), ("TrainerID", ColumnKind.UInt, 0)]);
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

    /// <summary>quest_template without the world-map POI columns and RewardFavor; text columns are the default locale.</summary>
    public static readonly TableSchema QuestTemplate = new("quest_template", ["ID"],
    [
        ("ID", ColumnKind.UInt, 0),
        ("QuestType", ColumnKind.UInt, 0),
        ("QuestPackageID", ColumnKind.UInt, 0),
        ("ContentTuningID", ColumnKind.UInt, 0),
        ("QuestSortID", ColumnKind.Int, 0),
        ("QuestInfoID", ColumnKind.UInt, 0),
        ("SuggestedGroupNum", ColumnKind.UInt, 0),
        ("RewardNextQuest", ColumnKind.UInt, 0),
        ("RewardXPDifficulty", ColumnKind.UInt, 0),
        ("RewardXPMultiplier", ColumnKind.Float, 0),
        ("RewardMoneyDifficulty", ColumnKind.UInt, 0),
        ("RewardMoneyMultiplier", ColumnKind.Float, 0),
        ("RewardBonusMoney", ColumnKind.UInt, 0),
        ("RewardDisplaySpell1", ColumnKind.UInt, 0),
        ("RewardDisplaySpell2", ColumnKind.UInt, 0),
        ("RewardDisplaySpell3", ColumnKind.UInt, 0),
        ("RewardSpell", ColumnKind.UInt, 0),
        ("RewardHonor", ColumnKind.UInt, 0),
        ("RewardKillHonor", ColumnKind.UInt, 0),
        ("StartItem", ColumnKind.UInt, 0),
        ("RewardArtifactXPDifficulty", ColumnKind.UInt, 0),
        ("RewardArtifactXPMultiplier", ColumnKind.Float, 0),
        ("RewardArtifactCategoryID", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.UInt, 0),
        ("FlagsEx", ColumnKind.UInt, 0),
        ("FlagsEx2", ColumnKind.UInt, 0),
        ("FlagsEx3", ColumnKind.UInt, 0),
        ("RewardItem1", ColumnKind.UInt, 0),
        ("RewardAmount1", ColumnKind.UInt, 0),
        ("RewardItem2", ColumnKind.UInt, 0),
        ("RewardAmount2", ColumnKind.UInt, 0),
        ("RewardItem3", ColumnKind.UInt, 0),
        ("RewardAmount3", ColumnKind.UInt, 0),
        ("RewardItem4", ColumnKind.UInt, 0),
        ("RewardAmount4", ColumnKind.UInt, 0),
        ("ItemDrop1", ColumnKind.UInt, 0),
        ("ItemDropQuantity1", ColumnKind.UInt, 0),
        ("ItemDrop2", ColumnKind.UInt, 0),
        ("ItemDropQuantity2", ColumnKind.UInt, 0),
        ("ItemDrop3", ColumnKind.UInt, 0),
        ("ItemDropQuantity3", ColumnKind.UInt, 0),
        ("ItemDrop4", ColumnKind.UInt, 0),
        ("ItemDropQuantity4", ColumnKind.UInt, 0),
        ("RewardChoiceItemID1", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity1", ColumnKind.UInt, 0),
        ("RewardChoiceItemDisplayID1", ColumnKind.UInt, 0),
        ("RewardChoiceItemID2", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity2", ColumnKind.UInt, 0),
        ("RewardChoiceItemDisplayID2", ColumnKind.UInt, 0),
        ("RewardChoiceItemID3", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity3", ColumnKind.UInt, 0),
        ("RewardChoiceItemDisplayID3", ColumnKind.UInt, 0),
        ("RewardChoiceItemID4", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity4", ColumnKind.UInt, 0),
        ("RewardChoiceItemDisplayID4", ColumnKind.UInt, 0),
        ("RewardChoiceItemID5", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity5", ColumnKind.UInt, 0),
        ("RewardChoiceItemDisplayID5", ColumnKind.UInt, 0),
        ("RewardChoiceItemID6", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity6", ColumnKind.UInt, 0),
        ("RewardChoiceItemDisplayID6", ColumnKind.UInt, 0),
        ("RewardTitle", ColumnKind.UInt, 0),
        ("RewardArenaPoints", ColumnKind.UInt, 0),
        ("RewardSkillLineID", ColumnKind.UInt, 0),
        ("RewardNumSkillUps", ColumnKind.UInt, 0),
        ("PortraitGiver", ColumnKind.UInt, 0),
        ("PortraitGiverMount", ColumnKind.Int, 0),
        ("PortraitGiverModelSceneID", ColumnKind.Int, 0),
        ("PortraitTurnIn", ColumnKind.UInt, 0),
        ("RewardFactionID1", ColumnKind.UInt, 0),
        ("RewardFactionValue1", ColumnKind.Int, 0),
        ("RewardFactionOverride1", ColumnKind.Int, 0),
        ("RewardFactionCapIn1", ColumnKind.Int, 0),
        ("RewardFactionID2", ColumnKind.UInt, 0),
        ("RewardFactionValue2", ColumnKind.Int, 0),
        ("RewardFactionOverride2", ColumnKind.Int, 0),
        ("RewardFactionCapIn2", ColumnKind.Int, 0),
        ("RewardFactionID3", ColumnKind.UInt, 0),
        ("RewardFactionValue3", ColumnKind.Int, 0),
        ("RewardFactionOverride3", ColumnKind.Int, 0),
        ("RewardFactionCapIn3", ColumnKind.Int, 0),
        ("RewardFactionID4", ColumnKind.UInt, 0),
        ("RewardFactionValue4", ColumnKind.Int, 0),
        ("RewardFactionOverride4", ColumnKind.Int, 0),
        ("RewardFactionCapIn4", ColumnKind.Int, 0),
        ("RewardFactionID5", ColumnKind.UInt, 0),
        ("RewardFactionValue5", ColumnKind.Int, 0),
        ("RewardFactionOverride5", ColumnKind.Int, 0),
        ("RewardFactionCapIn5", ColumnKind.Int, 0),
        ("RewardFactionFlags", ColumnKind.UInt, 0),
        ("RewardCurrencyID1", ColumnKind.UInt, 0),
        ("RewardCurrencyQty1", ColumnKind.UInt, 0),
        ("RewardCurrencyID2", ColumnKind.UInt, 0),
        ("RewardCurrencyQty2", ColumnKind.UInt, 0),
        ("RewardCurrencyID3", ColumnKind.UInt, 0),
        ("RewardCurrencyQty3", ColumnKind.UInt, 0),
        ("RewardCurrencyID4", ColumnKind.UInt, 0),
        ("RewardCurrencyQty4", ColumnKind.UInt, 0),
        ("AcceptedSoundKitID", ColumnKind.UInt, 0),
        ("CompleteSoundKitID", ColumnKind.UInt, 0),
        ("AreaGroupID", ColumnKind.UInt, 0),
        ("TimeAllowed", ColumnKind.Long, 0),
        ("AllowableRaces", ColumnKind.ULong, 0),
        ("Expansion", ColumnKind.Int, 0),
        ("ManagedWorldStateID", ColumnKind.Int, 0),
        ("QuestSessionBonus", ColumnKind.Int, 0),
        ("LogTitle", ColumnKind.Text, 0),
        ("LogDescription", ColumnKind.Text, 0),
        ("QuestDescription", ColumnKind.Text, 0),
        ("AreaDescription", ColumnKind.Text, 0),
        ("PortraitGiverText", ColumnKind.Text, 0),
        ("PortraitGiverName", ColumnKind.Text, 0),
        ("PortraitTurnInText", ColumnKind.Text, 0),
        ("PortraitTurnInName", ColumnKind.Text, 0),
        ("QuestCompletionLog", ColumnKind.Text, 0),
        ("ResetByScheduler", ColumnKind.Int, 0)
    ]);

    /// <summary>quest_template_addon (chain, class, skill and reputation requirements, script).</summary>
    public static readonly TableSchema QuestAddon = new("quest_template_addon", ["ID"],
    [
        ("ID", ColumnKind.UInt, 0),
        ("MaxLevel", ColumnKind.UInt, 0),
        ("AllowableClasses", ColumnKind.UInt, 0),
        ("SourceSpellID", ColumnKind.UInt, 0),
        ("PrevQuestID", ColumnKind.Int, 0),
        ("NextQuestID", ColumnKind.UInt, 0),
        ("ExclusiveGroup", ColumnKind.Int, 0),
        ("BreadcrumbForQuestId", ColumnKind.Int, 0),
        ("RewardMailTemplateID", ColumnKind.UInt, 0),
        ("RewardMailDelay", ColumnKind.UInt, 0),
        ("RequiredSkillID", ColumnKind.UInt, 0),
        ("RequiredSkillPoints", ColumnKind.UInt, 0),
        ("RequiredMinRepFaction", ColumnKind.UInt, 0),
        ("RequiredMaxRepFaction", ColumnKind.UInt, 0),
        ("RequiredMinRepValue", ColumnKind.Int, 0),
        ("RequiredMaxRepValue", ColumnKind.Int, 0),
        ("ProvidedItemCount", ColumnKind.UInt, 0),
        ("SpecialFlags", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 0)
    ]);

    /// <summary>The progress page: text and gestures.</summary>
    public static readonly TableSchema QuestRequestItems = new("quest_request_items", ["ID"],
    [
        ("ID", ColumnKind.UInt, 0),
        ("EmoteOnComplete", ColumnKind.UInt, 0),
        ("EmoteOnIncomplete", ColumnKind.UInt, 0),
        ("EmoteOnCompleteDelay", ColumnKind.UInt, 0),
        ("EmoteOnIncompleteDelay", ColumnKind.UInt, 0),
        ("CompletionText", ColumnKind.Text, 0)
    ]);

    /// <summary>The turn-in page: text and gestures.</summary>
    public static readonly TableSchema QuestOfferReward = new("quest_offer_reward", ["ID"],
    [
        ("ID", ColumnKind.UInt, 0),
        ("Emote1", ColumnKind.Int, 0),
        ("Emote2", ColumnKind.Int, 0),
        ("Emote3", ColumnKind.Int, 0),
        ("Emote4", ColumnKind.Int, 0),
        ("EmoteDelay1", ColumnKind.UInt, 0),
        ("EmoteDelay2", ColumnKind.UInt, 0),
        ("EmoteDelay3", ColumnKind.UInt, 0),
        ("EmoteDelay4", ColumnKind.UInt, 0),
        ("RewardText", ColumnKind.Text, 0)
    ]);

    /// <summary>One row per objective; QuestID names the quest.</summary>
    public static readonly TableSchema QuestObjectives = new("quest_objectives", ["ID"],
    [
        ("ID", ColumnKind.UInt, 0),
        ("QuestID", ColumnKind.UInt, 0),
        ("Type", ColumnKind.UInt, 0),
        ("Order", ColumnKind.UInt, 0),
        ("StorageIndex", ColumnKind.Int, 0),
        ("ObjectID", ColumnKind.Int, 0),
        ("Amount", ColumnKind.Int, 0),
        ("ConditionalAmount", ColumnKind.Int, 0),
        ("Flags", ColumnKind.UInt, 0),
        ("Flags2", ColumnKind.UInt, 0),
        ("ProgressBarWeight", ColumnKind.Float, 0),
        ("ParentObjectiveID", ColumnKind.Int, 0),
        ("Visible", ColumnKind.UInt, 0),
        ("Description", ColumnKind.Text, 0)
    ]);

    public static readonly TableSchema QuestStarter = new("creature_queststarter", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema QuestEnder = new("creature_questender", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema SpellClick = new("npc_spellclick_spells", ["npc_entry", "spell_id"], [("npc_entry", ColumnKind.UInt, 0), ("spell_id", ColumnKind.UInt, 0), ("cast_flags", ColumnKind.UInt, 0), ("user_type", ColumnKind.UInt, 0)]);

    /// <summary>The creature's AI name, a column of creature_template edited on its own so a SmartAI change never rewrites the whole template.</summary>
    public static readonly TableSchema CreatureAI = new("creature_ai", ["entry"], [("entry", ColumnKind.UInt, 0), ("AIName", ColumnKind.Text, 64)], "creature_template");

    /// <summary>
    /// smart_scripts of TrinityCore master: entryorguid is a bigint, event_param6 is gone, actions have
    /// a seventh parameter and each part has a string parameter; Difficulties replaces the difficulty flags.
    /// </summary>
    public static readonly TableSchema SmartScripts = new("smart_scripts", ["entryorguid", "source_type", "id", "link"],
    [
        ("entryorguid", ColumnKind.Long, 0),
        ("source_type", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("link", ColumnKind.UInt, 0),
        ("Difficulties", ColumnKind.Text, 100),
        ("event_type", ColumnKind.UInt, 0),
        ("event_phase_mask", ColumnKind.UInt, 0),
        ("event_chance", ColumnKind.UInt, 0),
        ("event_flags", ColumnKind.UInt, 0),
        ("event_param1", ColumnKind.UInt, 0),
        ("event_param2", ColumnKind.UInt, 0),
        ("event_param3", ColumnKind.UInt, 0),
        ("event_param4", ColumnKind.UInt, 0),
        ("event_param5", ColumnKind.UInt, 0),
        ("event_param_string", ColumnKind.Text, 255),
        ("action_type", ColumnKind.UInt, 0),
        ("action_param1", ColumnKind.UInt, 0),
        ("action_param2", ColumnKind.UInt, 0),
        ("action_param3", ColumnKind.UInt, 0),
        ("action_param4", ColumnKind.UInt, 0),
        ("action_param5", ColumnKind.UInt, 0),
        ("action_param6", ColumnKind.UInt, 0),
        ("action_param7", ColumnKind.UInt, 0),
        ("action_param_string", ColumnKind.Text, 255),
        ("target_type", ColumnKind.UInt, 0),
        ("target_param1", ColumnKind.UInt, 0),
        ("target_param2", ColumnKind.UInt, 0),
        ("target_param3", ColumnKind.UInt, 0),
        ("target_param4", ColumnKind.UInt, 0),
        ("target_param_string", ColumnKind.Text, 255),
        ("target_x", ColumnKind.Float, 0),
        ("target_y", ColumnKind.Float, 0),
        ("target_z", ColumnKind.Float, 0),
        ("target_o", ColumnKind.Float, 0),
        ("comment", ColumnKind.Text, 0)
    ]);

    /// <summary>creature_text of TrinityCore master: AzerothCore's columns plus SoundPlayType.</summary>
    public static readonly TableSchema CreatureText = new("creature_text", ["CreatureID", "GroupID", "ID"],
    [
        ("CreatureID", ColumnKind.UInt, 0),
        ("GroupID", ColumnKind.UInt, 0),
        ("ID", ColumnKind.UInt, 0),
        ("Text", ColumnKind.Text, 0),
        ("Type", ColumnKind.UInt, 0),
        ("Language", ColumnKind.UInt, 0),
        ("Probability", ColumnKind.Float, 0),
        ("Emote", ColumnKind.UInt, 0),
        ("Duration", ColumnKind.UInt, 0),
        ("Sound", ColumnKind.UInt, 0),
        ("SoundPlayType", ColumnKind.UInt, 0),
        ("BroadcastTextId", ColumnKind.Int, 0),
        ("TextRange", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    public static readonly TableSchema[] Tables = [CreatureTemplate, CreatureDifficulty, CreatureGossip, GossipMenu, NpcText, GossipOption, CreatureTrainer, Trainer, TrainerSpell, Vendor, QuestTemplate, QuestAddon, QuestRequestItems, QuestOfferReward, QuestObjectives, QuestStarter, QuestEnder, SpellClick, CreatureAI, SmartScripts, CreatureText];

    public IReadOnlyList<TableSchema> All => Tables;

    public TableSchema Find(string name) => Tables.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"Table {name} is not editable for the TrinityCore world database yet.");
}
