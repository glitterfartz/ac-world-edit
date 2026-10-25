using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore creature_template plus one creature_template_difficulty row, chosen in a picker that
/// lists the difficulties the creature has (stats, loot and gold live per difficulty).
/// </summary>
public sealed partial class TcTemplateSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "template";
    public override string Title => "Template";
    int? difficulty;

    public override void OnCreatureChanged(CreatureSpawn? creature) => difficulty = null;

    protected override void Populate(TcCreatureView v)
    {
        var t = v.Template;
        Heading("Creature");
        var flags = TcCreatureView.DescribeNpcFlags(v.NpcFlags);
        Facts(
        [
            ("Name", t.T("name")),
            ("Subname", t.T("subname")),
            ("Cursor icon", t.T("IconName")),
            ("NPC flags", flags.Count == 0 ? "None" : string.Join(", ", flags)),
            ("Faction", t.T("faction")),
            ("Classification", t.T("Classification")),
            ("Class", t.T("unit_class")),
            ("Creature type", t.T("type")),
            ("Family", t.T("family")),
            ("Trainer class", t.T("trainer_class")),
            ("Unit flags", $"{Hex(t.U("unit_flags"))} · {Hex(t.U("unit_flags2"))} · {Hex(t.U("unit_flags3"))}"),
            ("Extra flags", Hex(t.U("flags_extra"))),
            ("Speed (walk / run)", $"{Number(t.F("speed_walk"))} / {Number(t.F("speed_run"))}"),
            ("Scale", Number(t.F("scale"))),
            ("Movement type", t.T("MovementType")),
            ("Vehicle", t.T("VehicleId")),
            ("Required expansion", t.T("RequiredExpansion")),
            ("AI", t.T("AIName").Length > 0 ? t.T("AIName") : "None"),
            ("C++ script", t.T("ScriptName").Length > 0 ? t.T("ScriptName") : "None")
        ]);

        AddChild(new HSeparator());
        Heading("Difficulty");
        if (v.Difficulties.Length == 0)
        {
            Line("This creature has no creature_template_difficulty rows.", ClientStyle.Warning);
            return;
        }

        var picker = new OptionButton
        {
            Name = "DifficultyPicker",
            TooltipText = "Stats, loot and gold are stored per difficulty. Choose which row to show."
        };
        foreach (var d in v.Difficulties)
        {
            picker.AddItem(DifficultyName(d.DifficultyId), d.DifficultyId);
        }

        var shown = v.Difficulties.FirstOrDefault(d => d.DifficultyId == difficulty) ?? v.Difficulties[0];
        difficulty = shown.DifficultyId;
        picker.Select(Array.IndexOf(v.Difficulties, shown));
        picker.ItemSelected += index =>
        {
            difficulty = v.Difficulties[(int)index].DifficultyId;
            Refresh();
        };
        AddChild(picker);
        var r = shown.Row;
        Facts(
        [
            ("Content tuning", r.T("ContentTuningID")),
            ("Health scaling expansion", r.T("HealthScalingExpansion")),
            ("Health / mana / armor / damage", $"{Number(r.F("HealthModifier"))} / {Number(r.F("ManaModifier"))} / {Number(r.F("ArmorModifier"))} / {Number(r.F("DamageModifier"))}"),
            ("Creature difficulty", r.T("CreatureDifficultyID")),
            ("Loot / pickpocket / skinning", $"{r.T("LootID")} / {r.T("PickPocketLootID")} / {r.T("SkinLootID")}"),
            ("Gold", r.U("GoldMin") == 0 && r.U("GoldMax") == 0 ? "None" : $"{ClientStyle.Money(r.U("GoldMin"))} – {ClientStyle.Money(r.U("GoldMax"))}"),
            ("Type flags", $"{Hex(r.U("TypeFlags"))} · {Hex(r.U("TypeFlags2"))} · {Hex(r.U("TypeFlags3"))}"),
            ("Static flags", string.Join(" · ", Enumerable.Range(1, 8).Select(i => Hex(r.U($"StaticFlags{i}")))))
        ]);
    }

    /// <summary>The difficulty ID with the names the scene sidebar uses for the common ones.</summary>
    static string DifficultyName(int id) => id switch
    {
        0 => "Open world (0)",
        1 => "Normal dungeon (1)",
        2 => "Heroic dungeon (2)",
        14 => "Normal raid (14)",
        15 => "Heroic raid (15)",
        16 => "Mythic raid (16)",
        23 => "Mythic dungeon (23)",
        _ => $"Difficulty {id}"
    };
}
