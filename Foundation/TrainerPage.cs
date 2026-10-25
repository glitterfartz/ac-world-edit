namespace ACWorldEdit.Foundation;

/// <summary>Trainer assignment, template storage and the spells taught by one trainer.</summary>
public sealed record TrainerPageSpec(TableSchema Trainer, ItemListSpec Bindings, ItemListSpec Spells, uint TrainerFlag);

/// <summary>Schema references: the official AC/TC trainer, trainer_spell and creature trainer pages.</summary>
public static class TrainerPage
{
    public static readonly TrainerPageSpec AzerothCore = Build(false);
    public static readonly TrainerPageSpec TrinityCore = Build(true);

    static TrainerPageSpec Build(bool retail)
    {
        var table = retail ? TcTables.TrainerSpell : AcTables.TrainerSpell;
        FieldSpec Whole(string column, string label, string tip, ulong max = 0) =>
            new("spell", table, column, label, tip, FieldKind.Whole, RowBinding.Item, Max: max);
        var fields = new List<FieldSpec>
        {
            Whole("MoneyCost", "Cost in copper", "Cost to learn this spell."),
            Whole("ReqLevel", "Required level", "Minimum player level to learn this spell.", byte.MaxValue),
            Whole("ReqSkillLine", "Required skill", "SkillLine ID required to learn this spell. 0 means none."),
            Whole("ReqSkillRank", "Required skill rank", "Minimum rank in the required skill.")
        };
        if (retail)
        {
            for (int i = 1; i <= 3; i++)
                fields.Add(Whole($"ReqAbility{i}", $"Required ability {i}", "Spell ID the player must already know. 0 means none."));
        }
        Row NewSpell(uint trainer, RowSet rows)
        {
            var row = Row.Of(("TrainerId", trainer), ("SpellId", 0u), ("MoneyCost", 0u), ("ReqLevel", 1u), ("ReqSkillLine", 0u), ("ReqSkillRank", 0u));
            return retail ? row.With("ReqAbility1", 0u).With("ReqAbility2", 0u).With("ReqAbility3", 0u) : row;
        }
        var binding = retail ? TcTables.CreatureTrainer : AcTables.DefaultTrainer;
        var bindings = new ItemListSpec(binding, retail ? "CreatureID" : "CreatureId", retail ? "MenuID" : "TrainerId", "trainer binding", [],
            (entry, _) => retail
                ? Row.Of(("CreatureID", entry), ("TrainerID", 0u), ("MenuID", 0u), ("OptionID", 0u))
                : Row.Of(("CreatureId", entry), ("TrainerId", 0u)), ThenByColumn: retail ? "OptionID" : null);
        return new(retail ? TcTables.Trainer : AcTables.Trainer, bindings,
            new(table, "TrainerId", "ReqLevel", "trainer spell", fields.ToArray(), NewSpell, ThenByColumn: "SpellId"), 0x10);
    }
}
