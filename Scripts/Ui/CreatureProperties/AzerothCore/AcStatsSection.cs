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
    static readonly (uint Id, string Name)[] Classes = [(1, "Warrior"), (2, "Paladin"), (4, "Rogue"), (8, "Mage")];
    static readonly string[] Tiers = ["Classic", "Burning Crusade", "Wrath of the Lich King"];

    public override string Id => "stats";
    public override string Title => "Class / Level / Stats";
    public override string IconPath => "Interface/Icons/INV_Sword_04.blp";

    /// <summary>The creature_template level, class and stat column group.</summary>
    TableSchema StatsTable => Ctx.Schema.Tables.Find("creature_stats");

    protected override void Populate(NpcInteraction v)
    {
        var stats = effective.Get(StatsTable, v.Entry.ToString(CultureInfo.InvariantCulture));
        if (stats == null)
        {
            body.AddChild(ClientStyle.Text("This creature has no stats row.", 14, ClientStyle.Faded));
            return;
        }

        var lookup = snapshot!.Lookup;
        uint minLevel = stats.U("minlevel"), maxLevel = stats.U("maxlevel"), tier = Math.Min(stats.U("exp"), 2), unitClass = stats.U("unit_class");

        body.AddChild(ClientStyle.Text("Class and level", 16, ClientStyle.Header));
        if (editable)
        {
            body.AddChild(ClientStyle.Text($"These apply to all {v.SpawnCount} spawn(s) of this creature.", 11, ClientStyle.Warning));
        }

        var grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        body.AddChild(grid);

        string className = Classes.FirstOrDefault(c => c.Id == unitClass).Name ?? $"Unknown ({unitClass})";
        Field(grid, "Class", "Decides which row of the class stats table supplies base health, mana, armor and attributes. Only Warrior (no mana), Paladin (health and mana), Rogue (no mana, more damage) and Mage (low health, lots of mana) exist for creatures.",
            editable ? ClassPicker(stats, unitClass) : ClientStyle.Text(className, 14));
        Field(grid, "Lowest level", "The level range is rolled when the creature spawns. Each spawn gets a random level from the lowest to the highest value. Set both equal for a fixed level.",
            editable ? Number(stats, "minlevel", minLevel, "Lowest level") : ClientStyle.Text(minLevel.ToString(CultureInfo.InvariantCulture), 14));
        Field(grid, "Highest level", "Upper end of the level range. Level 63 is the top of Classic-era scaling and 80 the cap in Wrath; values above 80 only work if the class stats table has rows for them.",
            editable ? Number(stats, "maxlevel", maxLevel, "Highest level") : ClientStyle.Text(maxLevel.ToString(CultureInfo.InvariantCulture), 14));
        Field(grid, "Expansion tier", "Picks which health and damage column of the class stats table is used, so the same level can be tuned for Classic, Burning Crusade or Wrath content. Level 1-60 content normally uses Classic, outland content Burning Crusade, Northrend content Wrath.",
            editable ? TierPicker(stats, tier) : ClientStyle.Text(Tiers[tier], 14));

        body.AddChild(ClientStyle.Text("Stat multipliers", 16, ClientStyle.Header));
        body.AddChild(ClientStyle.Text("Each multiplies the base value for the creature's level and class. 1 keeps the table value, 2 doubles it.", 12, ClientStyle.Faded));
        var scales = new GridContainer
        {
            Columns = 2
        };
        scales.AddThemeConstantOverride("h_separation", 10);
        body.AddChild(scales);
        Field(scales, "Health multiplier", "Scales the creature's maximum health. Bosses and elites usually use values well above 1.",
            editable ? Decimal(stats, "HealthModifier", "Health multiplier") : ClientStyle.Text(Shown(stats.F("HealthModifier")), 14));
        Field(scales, "Mana multiplier", "Scales the creature's maximum mana. It has no effect on classes that have no mana.",
            editable ? Decimal(stats, "ManaModifier", "Mana multiplier") : ClientStyle.Text(Shown(stats.F("ManaModifier")), 14));
        Field(scales, "Armor multiplier", "Scales the creature's armor, which reduces the physical damage it takes.",
            editable ? Decimal(stats, "ArmorModifier", "Armor multiplier") : ClientStyle.Text(Shown(stats.F("ArmorModifier")), 14));
        Field(scales, "Damage multiplier", "Scales the damage of the creature's melee and ranged attacks.",
            editable ? Decimal(stats, "DamageModifier", "Damage multiplier") : ClientStyle.Text(Shown(stats.F("DamageModifier")), 14));

        RenderResult(lookup, stats, minLevel, maxLevel, tier, unitClass);
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

    static void Field(GridContainer grid, string name, string tip, Control editor)
    {
        grid.AddChild(ClientStyle.Caption(name, tip));
        editor.TooltipText = tip;
        grid.AddChild(editor);
    }

    OptionButton ClassPicker(Row stats, uint current)
    {
        var picker = new OptionButton();
        foreach (var (id, name) in Classes)
        {
            picker.AddItem(name, (int)id);
        }

        int index = picker.GetItemIndex((int)current);
        if (index < 0)
        {
            picker.AddItem($"Unknown ({current})", (int)current);
            index = picker.ItemCount - 1;
        }

        picker.Selected = index;
        picker.ItemSelected += i =>
        {
            uint id = (uint)picker.GetItemId((int)i);
            Commit($"Change class to {picker.GetItemText((int)i)}", [Update(StatsTable, stats, stats.With("unit_class", id))]);
        };
        return picker;
    }

    OptionButton TierPicker(Row stats, uint current)
    {
        var picker = new OptionButton();
        foreach (string tier in Tiers)
        {
            picker.AddItem(tier);
        }

        picker.Selected = (int)current;
        picker.ItemSelected += i => Commit($"Change expansion tier to {Tiers[i]}", [Update(StatsTable, stats, stats.With("exp", (uint)i))]);
        return picker;
    }

    LineEdit Number(Row stats, string column, uint current, string label)
    {
        var edit = new LineEdit
        {
            Text = current.ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(70, 0)
        };
        edit.TextSubmitted += text => Try(() =>
        {
            uint value = ParseUInt(text);
            uint min = column == "minlevel" ? value : stats.U("minlevel"), max = column == "maxlevel" ? value : stats.U("maxlevel");
            if (value is < 1 or > 255 || min > max)
            {
                status = "Not saved: levels must be between 1 and 255, and the lowest level cannot exceed the highest.";
                Render();
                return;
            }

            Commit($"Change {label.ToLowerInvariant()}", [Update(StatsTable, stats, stats.With(column, value))]);
        });
        return edit;
    }

    LineEdit Decimal(Row stats, string column, string label)
    {
        var edit = new LineEdit
        {
            Text = Shown(stats.F(column)),
            CustomMinimumSize = new(70, 0)
        };
        edit.TextSubmitted += text => Try(() =>
        {
            float value = float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!float.IsFinite(value) || value < 0)
            {
                status = "Not saved: a multiplier must be zero or more.";
                Render();
                return;
            }

            Commit($"Change {label.ToLowerInvariant()}", [Update(StatsTable, stats, stats.With(column, value))]);
        });
        return edit;
    }
}
