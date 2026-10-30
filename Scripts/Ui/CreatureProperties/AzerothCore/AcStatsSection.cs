using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// Level, class and stat scaling of the creature. The server computes a creature's health, mana,
/// armor and damage from the base values in creature_classlevelstats for its level and class, scaled
/// by the multipliers on the creature template; this page shows both and the result.
/// </summary>
public sealed partial class AcStatsSection(CreatureContext ctx) : AcInteractionSection(ctx)
{
    readonly PageRenderer renderer = new(ctx);

    public override string Id => PickerPages.Stats.Id;
    public override string Title => PickerPages.Stats.Title;
    public override string IconPath => PickerPages.Stats.IconPath;

    /// <summary>The creature_template level, class and stat column group.</summary>
    TableSchema StatsTable => Ctx.Schema.Tables.Find("creature_stats");

    protected override void Populate(AcCreatureView v)
    {
        var stats = effective.Get(StatsTable, v.Entry.ToString(CultureInfo.InvariantCulture));
        if (stats == null)
        {
            body.AddChild(ClientStyle.Text("This creature has no stats row.", 14, ClientStyle.Faded));
            return;
        }

        if (editable)
        {
            body.AddChild(ClientStyle.Text($"These apply to all {v.SpawnCount} spawn(s) of this creature.", 11, ClientStyle.Warning));
        }

        uint minLevel = stats.U("minlevel"), maxLevel = stats.U("maxlevel"), tier = Math.Min(stats.U("exp"), 2), unitClass = stats.U("unit_class");
        renderer.After.Clear();
        renderer.After["multipliers"] = () => RenderResult(snapshot!.Lookup, stats, minLevel, maxLevel, tier, unitClass);
        renderer.Render(body, Ctx.Schema.StatsPage, effective, v.Entry, editable);
    }

    void RenderResult(InteractionLookupData lookup, Row stats, uint minLevel, uint maxLevel, uint tier, uint unitClass)
    {
        body.AddChild(ClientStyle.Text("Resulting values", 16, ClientStyle.Header));
        body.AddChild(ClientStyle.Text("Base value from the class stats table times the multiplier above, at the lowest and highest level.", 12, ClientStyle.Faded));
        var low = lookup.ClassLevelStats.GetValueOrDefault((unitClass, minLevel));
        var high = lookup.ClassLevelStats.GetValueOrDefault((unitClass, maxLevel));
        if (low == null || high == null)
        {
            body.AddChild(ClientStyle.Text($"The class stats table has no row for {(low == null ? $"level {minLevel}" : $"level {maxLevel}")} of this class, so the server cannot compute its stats.", 13, ClientStyle.Warning));
            return;
        }

        var grid = new GridContainer
        {
            Columns = 3
        };
        grid.AddThemeConstantOverride("h_separation", 24);
        body.AddChild(grid);
        grid.AddChild(ClientStyle.Tag(""));
        grid.AddChild(ClientStyle.Tag($"Level {minLevel}"));
        grid.AddChild(ClientStyle.Tag($"Level {maxLevel}"));
        float health = stats.F("HealthModifier"), mana = stats.F("ManaModifier"), armor = stats.F("ArmorModifier"), damage = stats.F("DamageModifier");
        Result(grid, "Health", "Maximum health points.", s => s.BaseHealth[tier] * health);
        Result(grid, "Mana", "Maximum mana points. Zero for classes without mana.", s => s.BaseMana * mana);
        Result(grid, "Armor", "Physical damage reduction rating.", s => s.BaseArmor * armor);
        Result(grid, "Damage scale", "Base damage per swing before weapon and attack-speed adjustments, scaled by the damage multiplier.", s => s.DamageBase[tier] * damage);
        Result(grid, "Attack power", "Melee attack power. It is not affected by the multipliers.", s => s.AttackPower);
        Result(grid, "Ranged attack power", "Attack power for ranged attacks. It is not affected by the multipliers.", s => s.RangedAttackPower);
        Result(grid, "Strength", "Base attribute from the class stats table.", s => s.Strength);
        Result(grid, "Agility", "Base attribute from the class stats table.", s => s.Agility);
        Result(grid, "Stamina", "Base attribute from the class stats table.", s => s.Stamina);
        Result(grid, "Intellect", "Base attribute from the class stats table.", s => s.Intellect);
        Result(grid, "Spirit", "Base attribute from the class stats table.", s => s.Spirit);

        void Result(GridContainer table, string name, string tip, Func<ClassLevelStats, float> value)
        {
            table.AddChild(ClientStyle.Caption(name, tip));
            table.AddChild(ClientStyle.Text(Shown(value(low)), 14));
            table.AddChild(ClientStyle.Text(Shown(value(high)), 14));
        }
    }
}
