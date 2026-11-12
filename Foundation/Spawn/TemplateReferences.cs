namespace ACWorldEdit.Foundation;

/// <summary>
/// The tables outside a template's own rows that name a creature or GameObject template by entry, so a page
/// can say what an edit reaches. Counted only, so no table schema is needed and a missing table is skipped.
/// </summary>
public static class TemplateReferences
{
    public static IReadOnlyList<SpawnReference> For(SpawnKind kind) => kind == SpawnKind.Creature
        ?
        [
            new("creature", "id", "spawns"),
            new("smart_scripts", "entryorguid", "SmartAI rules for this template", "source_type = 0"),
            new("creature_loot_template", "Entry", "loot entries"),
            new("pickpocketing_loot_template", "Entry", "pickpocketing loot entries"),
            new("skinning_loot_template", "Entry", "skinning loot entries"),
            new("npc_vendor", "entry", "vendor lists"),
            new("creature_queststarter", "id", "quests it starts"),
            new("creature_questender", "id", "quests it ends")
        ]
        :
        [
            new("gameobject", "id", "spawns"),
            new("smart_scripts", "entryorguid", "SmartAI rules for this template", "source_type = 1"),
            new("gameobject_loot_template", "Entry", "loot entries"),
            new("gameobject_queststarter", "id", "quests it starts"),
            new("gameobject_questender", "id", "quests it ends")
        ];
}
