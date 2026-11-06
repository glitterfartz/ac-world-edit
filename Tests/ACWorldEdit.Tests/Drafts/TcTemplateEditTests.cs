using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>TrinityCore creature template and difficulty edits validate against TcTables and export as plain UPDATEs.</summary>
public class TcTemplateEditTests
{
    static Row Template(ulong npcflag) => Row.Of(
        ("entry", 54u), ("name", "Innkeeper"), ("subname", ""), ("IconName", ""), ("npcflag", npcflag), ("faction", 12u), ("Classification", 0u),
        ("unit_class", 1u), ("dmgschool", 0), ("BaseAttackTime", 2000u), ("RangeAttackTime", 0u), ("BaseVariance", 1f), ("RangeVariance", 1f), ("type", 7u), ("family", 0), ("trainer_class", 0u), ("unit_flags", 0u), ("unit_flags2", 0u), ("unit_flags3", 0u),
        ("speed_walk", 1f), ("speed_run", 1.14286f), ("scale", 1f), ("RequiredExpansion", 0), ("VehicleId", 0u), ("MovementType", 0u), ("movementId", 0u),
        ("ExperienceModifier", 1f), ("RacialLeader", 0u), ("RegenHealth", 1u), ("CreatureImmunitiesId", 0),
        ("AIName", ""), ("ScriptName", ""), ("flags_extra", 0u));

    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json");

    [Fact]
    public void TheTemplateRowMatchesTheCatalog()
    {
        Template(0).Validate(TcTables.CreatureTemplate);
    }

    [Fact]
    public void NpcFlagsAboveThirtyTwoBitsExportUnquoted()
    {
        ulong high = (1ul << 32) | 0x81;
        var before = Template(0x81);
        var edit = new RowEdit("creature_template", "54", before, before.With("npcflag", high));
        var scripts = SqlExport.Build("tc", [], [edit], new DateTime(2026, 1, 1), TcSchema.Instance);
        Assert.Contains($"UPDATE `creature_template` SET `npcflag` = {high} WHERE `entry` = 54 AND `npcflag` = 129;", scripts.Apply);
    }

    [Fact]
    public void ADifficultyRowEditUsesBothKeyColumns()
    {
        var before = Row.Of(("Entry", 54u), ("DifficultyID", 0), ("LevelScalingDeltaMin", 0), ("LevelScalingDeltaMax", 0), ("ContentTuningID", 1), ("HealthScalingExpansion", 0), ("HealthModifier", 1f), ("ManaModifier", 1f), ("ArmorModifier", 1f), ("DamageModifier", 1f),
            ("CreatureDifficultyID", 5), ("TypeFlags", 0u), ("TypeFlags2", 0u), ("TypeFlags3", 0u), ("LootID", 0u), ("PickPocketLootID", 0u), ("SkinLootID", 0u), ("GoldMin", 0u), ("GoldMax", 0u),
            ("StaticFlags1", 0u), ("StaticFlags2", 0u), ("StaticFlags3", 0u), ("StaticFlags4", 0u), ("StaticFlags5", 0u), ("StaticFlags6", 0u), ("StaticFlags7", 0u), ("StaticFlags8", 0u));
        var edit = new RowEdit("creature_template_difficulty", TcTables.CreatureDifficulty.KeyOf(before), before, before.With("LootID", 123u));
        var scripts = SqlExport.Build("tc", [], [edit], new DateTime(2026, 1, 1), TcSchema.Instance);
        Assert.Contains("UPDATE `creature_template_difficulty` SET `LootID` = 123 WHERE `Entry` = 54 AND `DifficultyID` = 0 AND (`LootID` = 0 OR `LootID` IS NULL);", scripts.Apply);
    }

    [Fact]
    public void ARowOutsideTheCatalogIsRefused()
    {
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        var before = Template(0);
        var snapshot = new RowSet();
        snapshot.Set(TcTables.CreatureTemplate, before);
        var bad = new RowEdit("creature_template", "54", before, before.With("faction", "-1"));
        var (ok, message) = drafts.Commit(null, "tc", snapshot, snapshot, "faction", [bad], _ => []);
        Assert.False(ok);
        Assert.Contains("Not saved", message);
    }
}
