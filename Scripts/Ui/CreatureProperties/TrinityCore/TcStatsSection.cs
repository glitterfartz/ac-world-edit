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
public sealed partial class TcStatsSection(CreatureContext ctx) : TcSection(ctx)
{
    static readonly (uint Id, string Name)[] Classes = [(1, "Warrior"), (2, "Paladin"), (4, "Rogue"), (8, "Mage")];

    readonly PageRenderer renderer = new(ctx);

    public override string Id => "stats";
    public override string Title => "Class / Level / Stats";

    public override void OnCreatureChanged(CreatureSpawn? creature) => renderer.Reset();

    protected override void Populate(TcCreatureView v)
    {
        if (Ctx.TcEditable)
        {
            Line($"These apply to every spawn of entry {v.Entry}.", ClientStyle.Warning, 11);
        }

        renderer.After["multipliers"] = () => Results(v);
        renderer.Render(this, TcStatsPage.Page, Ctx.Effective, v.Entry, Ctx.TcEditable);
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

        uint unitClass = Ctx.Effective.Get(TcTables.CreatureTemplate, v.Entry.ToString(CultureInfo.InvariantCulture))?.U("unit_class") ?? 0;
        TcCreatureStats stats;
        try
        {
            var classStats = Ctx.Snapshot.Lookup.TcClassStats;
            stats = TcStatCalculator.Compute(row, unitClass, data, (level, unitClass) => classStats.GetValueOrDefault((level, unitClass)));
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
        Result("Weapon damage", "Minimum to maximum weapon damage before attack power and attack speed are added: expected damage per second for the level, scaled, times the damage multiplier (maximum is 1.5 times the minimum).",
            l => $"{l.DamageMin.ToString("0.#", CultureInfo.InvariantCulture)} – {l.DamageMax.ToString("0.#", CultureInfo.InvariantCulture)}");
        Result("Attack power", "Melee attack power from creature_classlevelstats. The multipliers do not change it.", l => Whole(l.AttackPower));
        Result("Ranged attack power", "Ranged attack power from creature_classlevelstats. The multipliers do not change it.", l => Whole(l.RangedAttackPower));
        Line("The server's Rate.Creature.* settings also scale health and damage and are taken as 1.", ClientStyle.Faded, 11);
    }
}
