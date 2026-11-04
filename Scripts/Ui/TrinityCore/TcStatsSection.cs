using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// How the server scales a TrinityCore creature: the level and scaling columns of the picked
/// difficulty row, drawn from <see cref="TcStatsPage"/> by the shared <see cref="PageRenderer"/>, and
/// the level, health, mana, armor, damage and attack power they give (<see cref="TcStatCalculator"/>).
/// The values need the retail client's expected-stat tables.
/// </summary>
public sealed partial class TcStatsSection(CreatureContext ctx) : TcCreatureSection(ctx)
{
    static readonly (uint Id, string Name)[] Classes = [(1, "Warrior"), (2, "Paladin"), (4, "Rogue"), (8, "Mage")];

    readonly PageRenderer renderer = new(ctx);

    public override string Id => PickerPages.Stats.Id;
    public override string Title => PickerPages.Stats.Title;
    public override string IconPath => PickerPages.Stats.IconPath;

    public override void OnCreatureChanged(CreatureSpawn? creature) => renderer.Reset();

    protected override void Populate(TcCreatureView v)
    {
        if (Ctx.Editable)
        {
            Line($"These apply to every spawn of entry {v.Entry}.", ClientStyle.Warning, 11);
        }

        renderer.After["multipliers"] = () => Results(v);
        renderer.Render(this, Ctx.Schema.StatsPage, Ctx.Effective, v.Entry, Ctx.Editable);
    }

    void Results(TcCreatureView v)
    {
        Heading("Resulting values");
        if (renderer.Picked is not { } row)
        {
            return;
        }

        if (Ctx.Lookup() is not { } data || Ctx.Snapshot == null)
        {
            Line("Open a retail client to compute the level and stats.", ClientStyle.Faded);
            return;
        }

        if (Ctx.Effective.Get(TcTables.CreatureTemplate, v.Entry.ToString(CultureInfo.InvariantCulture)) is not { } templateRow)
        {
            return;
        }

        var template = TcStatTemplate.From(templateRow);
        uint unitClass = template.UnitClass;
        var rates = UserSettings.LoadCreatureRates();
        TcCreatureStats stats;
        try
        {
            var classStats = Ctx.Snapshot.Lookup.TcClassStats;
            stats = TcStatCalculator.Compute(row, template, data, (level, unitClass) => classStats.GetValueOrDefault((level, unitClass)), Site(row), rates);
        }
        catch (InvalidDataException error)
        {
            Line(error.Message, ClientStyle.Error);
            return;
        }

        string className = Classes.FirstOrDefault(c => c.Id == unitClass).Name ?? $"unit class {unitClass}";
        Line($"Base values for a {className.ToLowerInvariant()}; the class is set on the Template page.", ClientStyle.Faded, 12);
        if (stats.Note != null)
        {
            Line(stats.Note, stats.Low == null ? ClientStyle.Warning : ClientStyle.Faded);
        }

        if (stats.Low is not { } low || stats.High is not { } high)
        {
            return;
        }

        bool oneLevel = low.Level == high.Level;
        var grid = new GridContainer
        {
            Columns = oneLevel ? 2 : 3
        };
        grid.AddThemeConstantOverride("h_separation", 24);
        AddChild(grid);
        grid.AddChild(ClientStyle.Tag(""));
        grid.AddChild(ClientStyle.Tag($"Level {low.Level}"));
        if (!oneLevel)
        {
            grid.AddChild(ClientStyle.Tag($"Level {high.Level}"));
        }

        void Result(string name, string tip, Func<TcStatLine, string> value)
        {
            grid.AddChild(ClientStyle.Caption(name, tip));
            grid.AddChild(ClientStyle.Text(value(low), 14));
            if (!oneLevel)
            {
                grid.AddChild(ClientStyle.Text(value(high), 14));
            }
        }

        static string Whole(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
        Result("Health", "Maximum health: the expected creature health for the level, scaled by the content tuning and class, times the health multiplier.", l => Whole(l.Health));
        Result("Mana", "Base mana of the level and class (creature_classlevelstats) times the mana multiplier.", l => Whole(l.Mana));
        Result("Armor", "Expected creature armor for the level, scaled, times the armor multiplier.", l => Whole(l.Armor));
        static string Range(double min, double max) => $"{min.ToString("0.#", CultureInfo.InvariantCulture)} – {max.ToString("0.#", CultureInfo.InvariantCulture)}";
        Result("Weapon damage", "Minimum to maximum weapon damage before attack power and attack speed are added: expected damage per second for the level, scaled, times the damage multiplier (maximum is 1.5 times the minimum).",
            l => Range(l.DamageMin, l.DamageMax));
        Result("Melee hit", $"One melee swing: (weapon damage + attack power / 3.5 x melee variance {template.BaseVariance.ToString("0.##", CultureInfo.InvariantCulture)}) x damage multiplier x swing time ({template.BaseAttackTime} ms).",
            l => Range(l.Melee.Min, l.Melee.Max));
        if (template.RangeAttackTime > 0)
        {
            Result("Ranged hit", $"One ranged shot: the same with the ranged attack power, ranged variance {template.RangeVariance.ToString("0.##", CultureInfo.InvariantCulture)} and the ranged attack time ({template.RangeAttackTime} ms).",
                l => Range(l.Ranged.Min, l.Ranged.Max));
        }

        Result("Attack power", "Melee attack power from creature_classlevelstats. The multipliers do not change it.", l => Whole(l.AttackPower));
        Result("Ranged attack power", "Ranged attack power from creature_classlevelstats. The multipliers do not change it.", l => Whole(l.RangedAttackPower));
        HealthRate(template.Classification, rates);
    }

    /// <summary>Where the creature stands, for a template with no tuning: its map, this difficulty and the area cached on its spawn row.</summary>
    TcStatSite? Site(Row difficulty)
    {
        if (Ctx.Target is not { } spawn)
        {
            return null;
        }

        var row = Ctx.Effective.Get(TcTables.SpawnCreature, spawn.Guid.ToString(CultureInfo.InvariantCulture));
        uint area = row == null ? 0 : row.U("areaId") != 0 ? row.U("areaId") : row.U("zoneId");
        return new(spawn.Pose.Map, difficulty.I("DifficultyID"), area);
    }

    /// <summary>The server's Rate.Creature.*.HP for this classification: not stored in any database, so it is typed in once and kept per user.</summary>
    void HealthRate(uint classification, TcStatRates rates)
    {
        var row = new HBoxContainer();
        AddChild(row);
        row.AddChild(ClientStyle.Caption("Server health rate", "Rate.Creature.Normal/Elite/RareElite/Obsolete/Rare/Trivial/MinusMob.HP from worldserver.conf for this creature's classification. Health above is multiplied by it. 1 changes nothing."));
        var edit = new LineEdit { Name = "HealthRate", Text = rates.HealthFor(classification).ToString("0.###", CultureInfo.InvariantCulture), CustomMinimumSize = new(80, 0) };
        edit.TextSubmitted += text =>
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float rate) && rate > 0)
            {
                UserSettings.SaveCreatureHealthRate(classification, rate);
                Ctx.Redraw();
            }
            else
            {
                edit.Text = rates.HealthFor(classification).ToString("0.###", CultureInfo.InvariantCulture);
                Ctx.Status = "The health rate is a positive number.";
            }
        };
        row.AddChild(edit);
    }
}
