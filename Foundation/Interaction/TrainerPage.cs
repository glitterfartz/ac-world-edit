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
            Whole("MoneyCost", "Cost in copper", "How much gold the player pays to learn this spell, in copper (100 copper = 1 silver, 100 silver = 1 gold)."),
            Whole("ReqLevel", "Required level", "The minimum player level needed to learn this spell.", byte.MaxValue),
            Whole("ReqSkillLine", "Required skill", "The SkillLine ID the player must have to learn this spell, such as 197 for Tailoring. 0 means no skill requirement."),
            Whole("ReqSkillRank", "Required skill rank", "The minimum rank in that skill. 0 means any rank.")
        };
        for (int i = 1; i <= 3; i++)
            fields.Add(Whole($"ReqAbility{i}", $"Required ability {i}", "A spell ID the player must already know before this one can be learned. 0 means no requirement."));
        Row NewSpell(uint trainer, RowSet rows)
        {
            return Row.Of(("TrainerId", trainer), ("SpellId", 0u), ("MoneyCost", 0u), ("ReqLevel", 1u), ("ReqSkillLine", 0u), ("ReqSkillRank", 0u),
                ("ReqAbility1", 0u), ("ReqAbility2", 0u), ("ReqAbility3", 0u));
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
