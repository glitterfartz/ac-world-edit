using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>
/// The TrinityCore creature read model on synthetic rows: several root menus, submenus reached
/// through options, a trainer bound to a gossip option, missing texts and trainers, npcflag names.
/// </summary>
public class TcCreatureViewTests
{
    static Row Template(ulong npcflag) => Row.Of(TcTables.CreatureTemplate.Columns.Select(c => (c.Name, c.Name switch
    {
        "entry" => (object)7u,
        "name" => "Tester",
        "npcflag" => npcflag,
        _ => c.Kind == ColumnKind.Text ? "" : 0
    })).ToArray());

    static Row Option(uint menu, uint option, uint actionMenu, uint npc = 0) => Row.Of(TcTables.GossipOption.Columns.Select(c => (c.Name, c.Name switch
    {
        "MenuID" => (object)menu,
        "OptionID" => option,
        "ActionMenuID" => actionMenu,
        "OptionNpc" => npc,
        "OptionText" => $"Option {menu}/{option}",
        _ => c.Kind == ColumnKind.Text ? "" : 0
    })).ToArray());

    static RowSet Rows()
    {
        var rows = new RowSet();
        rows.Set(TcTables.CreatureTemplate, Template(0x10000ul | 0x80ul | (0x1ul << 32)));
        rows.Set(TcTables.CreatureGossip, Row.Of(("CreatureID", 7u), ("MenuID", 100u)));
        rows.Set(TcTables.CreatureGossip, Row.Of(("CreatureID", 7u), ("MenuID", 200u)));
        rows.Set(TcTables.GossipMenu, Row.Of(("MenuID", 100u), ("TextID", 5u)));
        rows.Set(TcTables.GossipMenu, Row.Of(("MenuID", 300u), ("TextID", 6u)));
        rows.Set(TcTables.NpcText, Row.Of([("ID", (object)5u), .. Enumerable.Range(0, 8).SelectMany(i => new (string, object)[] { ($"BroadcastTextID{i}", i == 0 ? 42u : 0u), ($"Probability{i}", i == 0 ? 1f : 0f) })]));
        rows.Set(TcTables.GossipOption, Option(100, 0, 300));
        rows.Set(TcTables.GossipOption, Option(100, 1, 0, npc: 3));
        rows.Set(TcTables.CreatureTrainer, Row.Of(("CreatureID", 7u), ("MenuID", 100u), ("OptionID", 1u), ("TrainerID", 9u)));
        rows.Set(TcTables.CreatureDifficulty, Row.Of(TcTables.CreatureDifficulty.Columns.Select(c => (c.Name, c.Name switch { "Entry" => (object)7u, "DifficultyID" => 1, _ => 0 })).ToArray()));
        rows.Set(TcTables.CreatureDifficulty, Row.Of(TcTables.CreatureDifficulty.Columns.Select(c => (c.Name, c.Name switch { "Entry" => (object)7u, _ => 0 })).ToArray()));
        return rows;
    }

    static TcCreatureView Build()
    {
        var lookup = new InteractionLookupData(null);
        lookup.Broadcasts[42] = ("Hello there.", "Hello there.");
        return TcCreatureView.Build(7, Rows(), lookup);
    }

    [Fact]
    public void EveryRootMenuAndTheSubmenusTheirOptionsReachAreListed()
    {
        var view = Build();
        Assert.Equal([100u, 200u, 300u], view.Menus.Select(m => m.MenuId));
        Assert.Equal([true, true, false], view.Menus.Select(m => m.Root));
        Assert.Equal("Hello there.", view.Menus[0].Texts.Single().Variants.Single().Text);
        Assert.Empty(view.Menus[1].Texts);
        Assert.True(view.Menus[2].Texts.Single().Missing);
    }

    [Fact]
    public void ATrainerIsAttachedToItsGossipOptionAndAMissingOneIsFlagged()
    {
        var view = Build();
        var trainer = Assert.Single(view.Trainers);
        Assert.Equal((9u, 100u, 1u), (trainer.TrainerId, trainer.MenuId, trainer.OptionId));
        Assert.True(trainer.Missing);
        var options = view.Menus[0].Options;
        Assert.Null(options[0].TrainerId);
        Assert.Equal(9u, options[1].TrainerId);
        Assert.Equal("Trainer", TcCreatureView.OptionNpcName(options[1].OptionNpc));
    }

    [Fact]
    public void DifficultiesAreOrderedAndNpcFlagsNameBothHalves()
    {
        var view = Build();
        Assert.Equal([0, 1], view.Difficulties.Select(d => d.DifficultyId));
        Assert.Equal(["Vendor", "Innkeeper", "Item upgrade master"], TcCreatureView.DescribeNpcFlags(view.NpcFlags));
        Assert.Equal(["0x100000000000"], TcCreatureView.DescribeNpcFlags(0x1000ul << 32));
        Assert.Equal("Option NPC 999", TcCreatureView.OptionNpcName(999));
    }

    [Fact]
    public void TheCatalogKeysMatchTheTrinityCoreSchema()
    {
        Assert.Equal(["CreatureID", "MenuID"], TcTables.CreatureGossip.Key);
        Assert.Equal(["CreatureID", "MenuID", "OptionID"], TcTables.CreatureTrainer.Key);
        Assert.Equal(["entry", "item", "ExtendedCost", "type"], TcTables.Vendor.Key);
        Assert.Equal(["Entry", "DifficultyID"], TcTables.CreatureDifficulty.Key);
        Assert.Equal(ColumnKind.ULong, TcTables.CreatureTemplate.Columns.Single(c => c.Name == "npcflag").Kind);
        Assert.Equal(17, TcTables.NpcText.Columns.Length);
    }

    [Fact]
    public void QuestLinksCarryTheQuestAndMarkRowsThatDifferFromTheDatabase()
    {
        var rows = Rows();
        var database = rows.Clone();
        rows.Set(TcTables.QuestStarter, Row.Of(("id", 7u), ("quest", 100u)));
        var lookup = new InteractionLookupData(null);
        lookup.Quests[100] = new QuestInfo(100, "Wolves", 5, 0, "", "", 0, []);

        var edited = TcCreatureView.Build(7, rows, lookup, database).Starts;
        Assert.Equal("Wolves", Assert.Single(edited).Info?.Title);
        Assert.True(edited[0].Edited);
        Assert.False(TcCreatureView.Build(7, rows, lookup, rows.Clone()).Starts[0].Edited);
        Assert.False(TcCreatureView.Build(7, rows, lookup).Starts[0].Edited);
    }
}
