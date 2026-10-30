namespace ACWorldEdit.Foundation;

/// <summary>
/// The AzerothCore interaction tables, with primary keys from the AzerothCore base schema. Text
/// limits follow the column types (varchar/text); 0 means unbounded text.
/// </summary>
public sealed class AcTables : ITableCatalog
{
    public static readonly AcTables Instance = new();

    public static readonly TableSchema CreatureTemplate = new("creature_template", ["entry"], [("entry", ColumnKind.UInt, 0), ("npcflag", ColumnKind.UInt, 0), ("gossip_menu_id", ColumnKind.UInt, 0), ("subname", ColumnKind.Text, 100)]);
    public static readonly TableSchema GossipMenu = new("gossip_menu", ["MenuID", "TextID"], [("MenuID", ColumnKind.UInt, 0), ("TextID", ColumnKind.UInt, 0)]);
    public static readonly TableSchema NpcText = new("npc_text", ["ID"], [("ID", ColumnKind.UInt, 0), ("text0_0", ColumnKind.Text, 0), ("text0_1", ColumnKind.Text, 0), ("BroadcastTextID0", ColumnKind.Int, 0), ("Probability0", ColumnKind.Float, 0)]);
    public static readonly TableSchema GossipOption = new("gossip_menu_option", ["MenuID", "OptionID"], [("MenuID", ColumnKind.UInt, 0), ("OptionID", ColumnKind.UInt, 0), ("OptionIcon", ColumnKind.UInt, 0), ("OptionText", ColumnKind.Text, 0), ("OptionBroadcastTextID", ColumnKind.Int, 0), ("OptionType", ColumnKind.UInt, 0), ("OptionNpcFlag", ColumnKind.UInt, 0), ("ActionMenuID", ColumnKind.UInt, 0), ("ActionPoiID", ColumnKind.UInt, 0), ("BoxCoded", ColumnKind.UInt, 0), ("BoxMoney", ColumnKind.UInt, 0), ("BoxText", ColumnKind.Text, 0), ("BoxBroadcastTextID", ColumnKind.UInt, 0)], Added: [("BoxBroadcastTextID", "0")]);
    public static readonly TableSchema QuestStarter = new("creature_queststarter", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema QuestEnder = new("creature_questender", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema Vendor = new("npc_vendor", ["entry", "item", "ExtendedCost"], [("entry", ColumnKind.UInt, 0), ("item", ColumnKind.Int, 0), ("ExtendedCost", ColumnKind.UInt, 0), ("slot", ColumnKind.Int, 0), ("maxcount", ColumnKind.UInt, 0), ("incrtime", ColumnKind.UInt, 0)]);
    public static readonly TableSchema DefaultTrainer = new("creature_default_trainer", ["CreatureId"], [("CreatureId", ColumnKind.UInt, 0), ("TrainerId", ColumnKind.UInt, 0)]);
    public static readonly TableSchema Trainer = new("trainer", ["Id"], [("Id", ColumnKind.UInt, 0), ("Type", ColumnKind.UInt, 0), ("Requirement", ColumnKind.UInt, 0), ("Greeting", ColumnKind.Text, 0)]);
    public static readonly TableSchema TrainerSpell = new("trainer_spell", ["TrainerId", "SpellId"], [("TrainerId", ColumnKind.UInt, 0), ("SpellId", ColumnKind.UInt, 0), ("MoneyCost", ColumnKind.UInt, 0), ("ReqSkillLine", ColumnKind.UInt, 0), ("ReqSkillRank", ColumnKind.UInt, 0), ("ReqAbility1", ColumnKind.UInt, 0), ("ReqAbility2", ColumnKind.UInt, 0), ("ReqAbility3", ColumnKind.UInt, 0), ("ReqLevel", ColumnKind.UInt, 0)], Added: [("ReqAbility1", "0"), ("ReqAbility2", "0"), ("ReqAbility3", "0")]);
    public static readonly TableSchema SpellClick = new("npc_spellclick_spells", ["npc_entry", "spell_id"], [("npc_entry", ColumnKind.UInt, 0), ("spell_id", ColumnKind.UInt, 0), ("cast_flags", ColumnKind.UInt, 0), ("user_type", ColumnKind.UInt, 0)]);

    /// <summary>
    /// creature_template.AIName as its own column group: SmartAI scripts only run when it is "SmartAI".
    /// </summary>
    public static readonly TableSchema CreatureAI = new("creature_ai", ["entry"], [("entry", ColumnKind.UInt, 0), ("AIName", ColumnKind.Text, 64)], "creature_template");

    /// <summary>
    /// creature_template level, class and stat scaling as its own column group, so editing stats only
    /// updates these columns. The base values they scale come from creature_classlevelstats.
    /// </summary>
    public static readonly TableSchema CreatureStats = new("creature_stats", ["entry"], [("entry", ColumnKind.UInt, 0), ("minlevel", ColumnKind.UInt, 0), ("maxlevel", ColumnKind.UInt, 0), ("exp", ColumnKind.UInt, 0), ("unit_class", ColumnKind.UInt, 0), ("HealthModifier", ColumnKind.Float, 0), ("ManaModifier", ColumnKind.Float, 0), ("ArmorModifier", ColumnKind.Float, 0), ("DamageModifier", ColumnKind.Float, 0)], "creature_template");

    /// <summary>
    /// Every other editable creature_template column (identity, combat, movement, loot, flags) as one
    /// column group. Excludes the key, VerifiedBuild and the columns owned by the groups above.
    /// </summary>
    public static readonly TableSchema CreatureDetails = new("creature_details", ["entry"], [
        ("entry", ColumnKind.UInt, 0),
        ("name", ColumnKind.Text, 100),
        ("IconName", ColumnKind.Text, 100),
        ("difficulty_entry_1", ColumnKind.UInt, 0),
        ("difficulty_entry_2", ColumnKind.UInt, 0),
        ("difficulty_entry_3", ColumnKind.UInt, 0),
        ("KillCredit1", ColumnKind.UInt, 0),
        ("KillCredit2", ColumnKind.UInt, 0),
        ("faction", ColumnKind.UInt, 0),
        ("speed_walk", ColumnKind.Float, 0),
        ("speed_run", ColumnKind.Float, 0),
        ("speed_swim", ColumnKind.Float, 0),
        ("speed_flight", ColumnKind.Float, 0),
        ("detection_range", ColumnKind.Float, 0),
        ("rank", ColumnKind.UInt, 0),
        ("dmgschool", ColumnKind.Int, 0),
        ("BaseAttackTime", ColumnKind.UInt, 0),
        ("RangeAttackTime", ColumnKind.UInt, 0),
        ("BaseVariance", ColumnKind.Float, 0),
        ("RangeVariance", ColumnKind.Float, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("unit_flags2", ColumnKind.UInt, 0),
        ("dynamicflags", ColumnKind.UInt, 0),
        ("family", ColumnKind.Int, 0),
        ("type", ColumnKind.UInt, 0),
        ("type_flags", ColumnKind.UInt, 0),
        ("lootid", ColumnKind.UInt, 0),
        ("pickpocketloot", ColumnKind.UInt, 0),
        ("skinloot", ColumnKind.UInt, 0),
        ("PetSpellDataId", ColumnKind.UInt, 0),
        ("VehicleId", ColumnKind.UInt, 0),
        ("mingold", ColumnKind.UInt, 0),
        ("maxgold", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("HoverHeight", ColumnKind.Float, 0),
        ("ExperienceModifier", ColumnKind.Float, 0),
        ("RacialLeader", ColumnKind.UInt, 0),
        ("movementId", ColumnKind.UInt, 0),
        ("RegenHealth", ColumnKind.UInt, 0),
        ("CreatureImmunitiesId", ColumnKind.Int, 0),
        ("flags_extra", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64)
    ], "creature_template");

    // Quest tables: every column except VerifiedBuild (build stamp, never edited).
    public static readonly TableSchema QuestTemplate = new("quest_template", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("QuestType", ColumnKind.UInt, 0),
        ("QuestLevel", ColumnKind.Int, 0),
        ("MinLevel", ColumnKind.UInt, 0),
        ("QuestSortID", ColumnKind.Int, 0),
        ("QuestInfoID", ColumnKind.UInt, 0),
        ("SuggestedGroupNum", ColumnKind.UInt, 0),
        ("RequiredFactionId1", ColumnKind.UInt, 0),
        ("RequiredFactionId2", ColumnKind.UInt, 0),
        ("RequiredFactionValue1", ColumnKind.Int, 0),
        ("RequiredFactionValue2", ColumnKind.Int, 0),
        ("RewardNextQuest", ColumnKind.UInt, 0),
        ("RewardXPDifficulty", ColumnKind.UInt, 0),
        ("RewardMoney", ColumnKind.Int, 0),
        ("RewardMoneyDifficulty", ColumnKind.UInt, 0),
        ("RewardDisplaySpell", ColumnKind.UInt, 0),
        ("RewardSpell", ColumnKind.Int, 0),
        ("RewardHonor", ColumnKind.Int, 0),
        ("RewardKillHonor", ColumnKind.Float, 0),
        ("StartItem", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.UInt, 0),
        ("RequiredPlayerKills", ColumnKind.UInt, 0),
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
        ("RewardChoiceItemID2", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity2", ColumnKind.UInt, 0),
        ("RewardChoiceItemID3", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity3", ColumnKind.UInt, 0),
        ("RewardChoiceItemID4", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity4", ColumnKind.UInt, 0),
        ("RewardChoiceItemID5", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity5", ColumnKind.UInt, 0),
        ("RewardChoiceItemID6", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity6", ColumnKind.UInt, 0),
        ("POIContinent", ColumnKind.UInt, 0),
        ("POIx", ColumnKind.Float, 0),
        ("POIy", ColumnKind.Float, 0),
        ("POIPriority", ColumnKind.UInt, 0),
        ("RewardTitle", ColumnKind.UInt, 0),
        ("RewardTalents", ColumnKind.UInt, 0),
        ("RewardArenaPoints", ColumnKind.UInt, 0),
        ("RewardFactionID1", ColumnKind.UInt, 0),
        ("RewardFactionValue1", ColumnKind.Int, 0),
        ("RewardFactionOverride1", ColumnKind.Int, 0),
        ("RewardFactionID2", ColumnKind.UInt, 0),
        ("RewardFactionValue2", ColumnKind.Int, 0),
        ("RewardFactionOverride2", ColumnKind.Int, 0),
        ("RewardFactionID3", ColumnKind.UInt, 0),
        ("RewardFactionValue3", ColumnKind.Int, 0),
        ("RewardFactionOverride3", ColumnKind.Int, 0),
        ("RewardFactionID4", ColumnKind.UInt, 0),
        ("RewardFactionValue4", ColumnKind.Int, 0),
        ("RewardFactionOverride4", ColumnKind.Int, 0),
        ("RewardFactionID5", ColumnKind.UInt, 0),
        ("RewardFactionValue5", ColumnKind.Int, 0),
        ("RewardFactionOverride5", ColumnKind.Int, 0),
        ("TimeAllowed", ColumnKind.UInt, 0),
        ("AllowableRaces", ColumnKind.UInt, 0),
        ("LogTitle", ColumnKind.Text, 0),
        ("LogDescription", ColumnKind.Text, 0),
        ("QuestDescription", ColumnKind.Text, 0),
        ("AreaDescription", ColumnKind.Text, 0),
        ("QuestCompletionLog", ColumnKind.Text, 0),
        ("RequiredNpcOrGo1", ColumnKind.Int, 0),
        ("RequiredNpcOrGo2", ColumnKind.Int, 0),
        ("RequiredNpcOrGo3", ColumnKind.Int, 0),
        ("RequiredNpcOrGo4", ColumnKind.Int, 0),
        ("RequiredNpcOrGoCount1", ColumnKind.UInt, 0),
        ("RequiredNpcOrGoCount2", ColumnKind.UInt, 0),
        ("RequiredNpcOrGoCount3", ColumnKind.UInt, 0),
        ("RequiredNpcOrGoCount4", ColumnKind.UInt, 0),
        ("RequiredItemId1", ColumnKind.UInt, 0),
        ("RequiredItemId2", ColumnKind.UInt, 0),
        ("RequiredItemId3", ColumnKind.UInt, 0),
        ("RequiredItemId4", ColumnKind.UInt, 0),
        ("RequiredItemId5", ColumnKind.UInt, 0),
        ("RequiredItemId6", ColumnKind.UInt, 0),
        ("RequiredItemCount1", ColumnKind.UInt, 0),
        ("RequiredItemCount2", ColumnKind.UInt, 0),
        ("RequiredItemCount3", ColumnKind.UInt, 0),
        ("RequiredItemCount4", ColumnKind.UInt, 0),
        ("RequiredItemCount5", ColumnKind.UInt, 0),
        ("RequiredItemCount6", ColumnKind.UInt, 0),
        ("Unknown0", ColumnKind.UInt, 0),
        ("ObjectiveText1", ColumnKind.Text, 0),
        ("ObjectiveText2", ColumnKind.Text, 0),
        ("ObjectiveText3", ColumnKind.Text, 0),
        ("ObjectiveText4", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema QuestAddon = new("quest_template_addon", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("MaxLevel", ColumnKind.UInt, 0),
        ("AllowableClasses", ColumnKind.UInt, 0),
        ("SourceSpellID", ColumnKind.UInt, 0),
        ("PrevQuestID", ColumnKind.Int, 0),
        ("NextQuestID", ColumnKind.UInt, 0),
        ("ExclusiveGroup", ColumnKind.Int, 0),
        ("BreadcrumbForQuestId", ColumnKind.UInt, 0),
        ("RewardMailTemplateID", ColumnKind.UInt, 0),
        ("RewardMailDelay", ColumnKind.UInt, 0),
        ("RequiredSkillID", ColumnKind.UInt, 0),
        ("RequiredSkillPoints", ColumnKind.UInt, 0),
        ("RequiredMinRepFaction", ColumnKind.UInt, 0),
        ("RequiredMaxRepFaction", ColumnKind.UInt, 0),
        ("RequiredMinRepValue", ColumnKind.Int, 0),
        ("RequiredMaxRepValue", ColumnKind.Int, 0),
        ("ProvidedItemCount", ColumnKind.UInt, 0),
        ("SpecialFlags", ColumnKind.UInt, 0)
    ]);
    public static readonly TableSchema QuestRequestItems = new("quest_request_items", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("EmoteOnComplete", ColumnKind.UInt, 0),
        ("EmoteOnIncomplete", ColumnKind.UInt, 0),
        ("CompletionText", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema QuestOfferReward = new("quest_offer_reward", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("Emote1", ColumnKind.UInt, 0),
        ("Emote2", ColumnKind.UInt, 0),
        ("Emote3", ColumnKind.UInt, 0),
        ("Emote4", ColumnKind.UInt, 0),
        ("EmoteDelay1", ColumnKind.UInt, 0),
        ("EmoteDelay2", ColumnKind.UInt, 0),
        ("EmoteDelay3", ColumnKind.UInt, 0),
        ("EmoteDelay4", ColumnKind.UInt, 0),
        ("RewardText", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema SmartScripts = new("smart_scripts", ["entryorguid", "source_type", "id", "link"], [
        ("entryorguid", ColumnKind.Int, 0),
        ("source_type", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("link", ColumnKind.UInt, 0),
        ("event_type", ColumnKind.UInt, 0),
        ("event_phase_mask", ColumnKind.UInt, 0),
        ("event_chance", ColumnKind.UInt, 0),
        ("event_flags", ColumnKind.UInt, 0),
        ("event_param1", ColumnKind.UInt, 0),
        ("event_param2", ColumnKind.UInt, 0),
        ("event_param3", ColumnKind.UInt, 0),
        ("event_param4", ColumnKind.UInt, 0),
        ("event_param5", ColumnKind.UInt, 0),
        ("event_param6", ColumnKind.UInt, 0),
        ("action_type", ColumnKind.UInt, 0),
        ("action_param1", ColumnKind.UInt, 0),
        ("action_param2", ColumnKind.UInt, 0),
        ("action_param3", ColumnKind.UInt, 0),
        ("action_param4", ColumnKind.UInt, 0),
        ("action_param5", ColumnKind.UInt, 0),
        ("action_param6", ColumnKind.UInt, 0),
        ("target_type", ColumnKind.UInt, 0),
        ("target_param1", ColumnKind.UInt, 0),
        ("target_param2", ColumnKind.UInt, 0),
        ("target_param3", ColumnKind.UInt, 0),
        ("target_param4", ColumnKind.UInt, 0),
        ("target_x", ColumnKind.Float, 0),
        ("target_y", ColumnKind.Float, 0),
        ("target_z", ColumnKind.Float, 0),
        ("target_o", ColumnKind.Float, 0),
        ("comment", ColumnKind.Text, 0)
    ]);

    /// <summary>
    /// Lines a creature can say through SmartAI "Talk" (grouped by GroupID; the server picks one
    /// line of a group by Probability).
    /// </summary>
    public static readonly TableSchema CreatureText = new("creature_text", ["CreatureID", "GroupID", "ID"], [
        ("CreatureID", ColumnKind.UInt, 0),
        ("GroupID", ColumnKind.UInt, 0),
        ("ID", ColumnKind.UInt, 0),
        ("Text", ColumnKind.Text, 0),
        ("Type", ColumnKind.UInt, 0),
        ("Language", ColumnKind.Int, 0),
        ("Probability", ColumnKind.Float, 0),
        ("Emote", ColumnKind.UInt, 0),
        ("Duration", ColumnKind.UInt, 0),
        ("Sound", ColumnKind.UInt, 0),
        ("BroadcastTextId", ColumnKind.Int, 0),
        ("TextRange", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    /// <summary>
    /// The script an AreaTrigger runs (ObjectMgr::LoadAreaTriggerScripts): a C++ AreaTriggerScript
    /// name, or "SmartTrigger" for its smart_scripts rows (source_type 2). One name per trigger.
    /// </summary>
    public static readonly TableSchema AreaTriggerScripts = new("areatrigger_scripts", ["entry"], [
        ("entry", ColumnKind.Int, 0),
        ("ScriptName", ColumnKind.Text, 64)
    ]);

    /// <summary>
    /// A creature spawn. npcflag, unit_flags and dynamicflags are NOT NULL here, and 0 means "use the
    /// template's" (ObjectMgr::ChooseCreatureFlags), so a spawn cannot override a flag down to 0.
    /// </summary>
    public static readonly TableSchema SpawnCreature = new("creature", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("map", ColumnKind.UInt, 0),
        ("zoneId", ColumnKind.UInt, 0),
        ("areaId", ColumnKind.UInt, 0),
        ("spawnMask", ColumnKind.UInt, 0),
        ("phaseMask", ColumnKind.UInt, 0),
        ("equipment_id", ColumnKind.Int, 0),
        ("position_x", ColumnKind.Float, 0),
        ("position_y", ColumnKind.Float, 0),
        ("position_z", ColumnKind.Float, 0),
        ("orientation", ColumnKind.Float, 0),
        ("spawntimesecs", ColumnKind.UInt, 0),
        ("wander_distance", ColumnKind.Float, 0),
        ("currentwaypoint", ColumnKind.UInt, 0),
        ("curhealth", ColumnKind.UInt, 0),
        ("curmana", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("npcflag", ColumnKind.UInt, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("dynamicflags", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("Comment", ColumnKind.Text, 0)
    ]);

    public static readonly TableSchema SpawnGameObject = new("gameobject", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("map", ColumnKind.UInt, 0),
        ("zoneId", ColumnKind.UInt, 0),
        ("areaId", ColumnKind.UInt, 0),
        ("spawnMask", ColumnKind.UInt, 0),
        ("phaseMask", ColumnKind.UInt, 0),
        ("position_x", ColumnKind.Float, 0),
        ("position_y", ColumnKind.Float, 0),
        ("position_z", ColumnKind.Float, 0),
        ("orientation", ColumnKind.Float, 0),
        ("rotation0", ColumnKind.Float, 0),
        ("rotation1", ColumnKind.Float, 0),
        ("rotation2", ColumnKind.Float, 0),
        ("rotation3", ColumnKind.Float, 0),
        ("spawntimesecs", ColumnKind.Int, 0),
        ("animprogress", ColumnKind.UInt, 0),
        ("state", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("Comment", ColumnKind.Text, 0)
    ]);

    /// <summary>Per-spawn creature extras; bytes1/bytes2 pack stand state, sheath and PvP flags as the 3.3.5 client reads them.</summary>
    public static readonly TableSchema SpawnCreatureAddon = new("creature_addon", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("path_id", ColumnKind.UInt, 0),
        ("mount", ColumnKind.UInt, 0),
        ("bytes1", ColumnKind.UInt, 0),
        ("bytes2", ColumnKind.UInt, 0),
        ("emote", ColumnKind.UInt, 0),
        ("visibilityDistanceType", ColumnKind.UInt, 0),
        ("auras", ColumnKind.Text, 0)
    ]);


    /// <summary>What every spawn of a creature template looks like (mount, stance, weapons, emote, auras) unless the spawn has its own `creature_addon` row, which replaces this one entirely.</summary>
    public static readonly TableSchema CreatureTemplateAddon = new("creature_template_addon", ["entry"], [
        ("entry", ColumnKind.UInt, 0),
        ("path_id", ColumnKind.UInt, 0),
        ("mount", ColumnKind.UInt, 0),
        ("bytes1", ColumnKind.UInt, 0),
        ("bytes2", ColumnKind.UInt, 0),
        ("emote", ColumnKind.UInt, 0),
        ("visibilityDistanceType", ColumnKind.UInt, 0),
        ("auras", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema SpawnGameObjectAddon = new("gameobject_addon", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("parent_rotation0", ColumnKind.Float, 0),
        ("parent_rotation1", ColumnKind.Float, 0),
        ("parent_rotation2", ColumnKind.Float, 0),
        ("parent_rotation3", ColumnKind.Float, 0),
        ("invisibilityType", ColumnKind.UInt, 0),
        ("invisibilityValue", ColumnKind.UInt, 0)
    ]);

    /// <summary>The server's conditions table. Only the gossip menu sources (14 and 15) are read and copied so far; the visual builder is Phase 12.</summary>
    public static readonly TableSchema Conditions = new("conditions", ["SourceTypeOrReferenceId", "SourceGroup", "SourceEntry", "SourceId", "ElseGroup", "ConditionTypeOrReference", "ConditionTarget", "ConditionValue1", "ConditionValue2", "ConditionValue3"],
    [
        ("SourceTypeOrReferenceId", ColumnKind.Int, 0),
        ("SourceGroup", ColumnKind.UInt, 0),
        ("SourceEntry", ColumnKind.Int, 0),
        ("SourceId", ColumnKind.Int, 0),
        ("ElseGroup", ColumnKind.UInt, 0),
        ("ConditionTypeOrReference", ColumnKind.Int, 0),
        ("ConditionTarget", ColumnKind.UInt, 0),
        ("ConditionValue1", ColumnKind.UInt, 0),
        ("ConditionValue2", ColumnKind.UInt, 0),
        ("ConditionValue3", ColumnKind.UInt, 0),
        ("NegativeCondition", ColumnKind.UInt, 0),
        ("ErrorType", ColumnKind.UInt, 0),
        ("ErrorTextId", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 0),
        ("Comment", ColumnKind.Text, 0)
    ]);

    public static readonly TableSchema[] Tables = [CreatureTemplate, GossipMenu, NpcText, GossipOption, QuestStarter, QuestEnder, Vendor, DefaultTrainer, Trainer, TrainerSpell, SpellClick, CreatureAI, CreatureStats, CreatureDetails, QuestTemplate, QuestAddon, QuestRequestItems, QuestOfferReward, SmartScripts, CreatureText, AreaTriggerScripts, SpawnCreature, SpawnGameObject, SpawnCreatureAddon, SpawnGameObjectAddon, CreatureTemplateAddon, Conditions];

    IReadOnlyList<TableSchema> ITableCatalog.All => Tables;
    TableSchema ITableCatalog.Find(string name) => Tables.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"Table {name} is not editable.");
}
