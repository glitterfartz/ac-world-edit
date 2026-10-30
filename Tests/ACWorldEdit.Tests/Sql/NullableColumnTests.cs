using System.Text.Json;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Sql;

/// <summary>
/// TrinityCore spawn overrides use NULL to mean "inherit from the template", so a row has to tell
/// NULL from 0 end to end: validation, the stored form and the exported guards.
/// </summary>
public class NullableColumnTests
{
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static readonly TableSchema Spawn = TcTables.SpawnCreature;

    static Row Inheriting(ulong guid = 7)
    {
        var row = Row.Of(Spawn.Columns.Select(c => (c.Name, c.Kind == ColumnKind.Text ? "" : c.Kind == ColumnKind.Float ? (object)0f : 0u)).ToArray());
        return row.With("guid", guid).With("npcflag", null).With("unit_flags", null).With("StringId", null).With("PhaseId", null);
    }

    static string Apply(RowEdit edit) => SqlExport.Build("src", [], [edit], Stamp, null, TcSchema.Instance).Apply;

    static string Revert(RowEdit edit) => SqlExport.Build("src", [], [edit], Stamp, null, TcSchema.Instance).Revert;

    static RowEdit Update(Row before, Row after) => new(Spawn.Name, Spawn.KeyOf(before), before, after);

    [Fact]
    public void NullIsStoredAsOneReservedValueAndNeverEqualsZeroOrEmpty()
    {
        var row = Inheriting();
        Assert.True(row.IsNull("npcflag"));
        Assert.False(row.With("npcflag", 0u).IsNull("npcflag"));
        Assert.NotEqual(row, row.With("npcflag", 0u));
        Assert.NotEqual(row.With("StringId", ""), row);
    }

    [Fact]
    public void OnlyDeclaredColumnsMayHoldNull()
    {
        Inheriting().Validate(Spawn);
        var error = Assert.Throws<InvalidDataException>(() => Inheriting().With("modelid", null).Validate(Spawn));
        Assert.Contains("modelid cannot be empty", error.Message);
    }

    [Fact]
    public void ARowWithNullSurvivesTheDraftFile()
    {
        var row = Inheriting();
        var copy = JsonSerializer.Deserialize<Row>(JsonSerializer.Serialize(row))!;
        Assert.Equal(row, copy);
        Assert.True(copy.IsNull("npcflag"));
    }

    [Fact]
    public void OverridingAnInheritedFlagGuardsWithIsNullAndRevertsToNull()
    {
        var before = Inheriting();
        var edit = Update(before, before.With("npcflag", 3UL));
        string apply = Apply(edit);
        Assert.Contains("UPDATE `creature` SET `npcflag` = 3 WHERE `guid` = 7 AND `npcflag` IS NULL;", apply);
        Assert.DoesNotContain("= NULL", apply);
        Assert.Contains("UPDATE `creature` SET `npcflag` = NULL WHERE `guid` = 7 AND `npcflag` = 3;", Revert(edit));
    }

    [Fact]
    public void AnExplicitZeroIsNotTheSameChangeAsInheriting()
    {
        var before = Inheriting();
        var edit = Update(before, before.With("unit_flags", 0u));
        Assert.Contains("SET `unit_flags` = 0 WHERE `guid` = 7 AND `unit_flags` IS NULL;", Apply(edit));
    }

    [Fact]
    public void InsertWritesNullLiteralsAndDeleteGuardsThem()
    {
        var row = Inheriting();
        var insert = new RowEdit(Spawn.Name, Spawn.KeyOf(row), null, row);
        string apply = Apply(insert);
        Assert.Contains("NULL", apply.Split('\n').Single(l => l.StartsWith("INSERT ", StringComparison.Ordinal)));
        Assert.Contains("`npcflag` IS NULL", Revert(insert));
    }

    [Fact]
    public void TheAzerothCoreSpawnHasNoNullableColumns()
    {
        Assert.Null(AcTables.SpawnCreature.Nullable);
        Assert.Throws<InvalidDataException>(() => Row.Of(AcTables.SpawnCreature.Columns.Select(c => (c.Name, c.Kind == ColumnKind.Text ? "" : c.Kind == ColumnKind.Float ? (object)0f : 0u)).ToArray()).With("npcflag", null).Validate(AcTables.SpawnCreature));
    }

    [Fact]
    public void AZeroInAColumnTheCatalogDoesNotModelAsNullableAlsoGuardsNullButAKeyStaysPlain()
    {
        // Real rows have NULL in columns such as gossip_menu_option.SpellID; the loader reads them as 0.
        var row = Row.Of(TcTables.GossipOption.Columns.Select(c => (c.Name, (object)(c.Kind == ColumnKind.Text ? "" : 0))).ToArray()).With("MenuID", 5u);
        var sql = SqlExport.Build("tc", [], [InteractionEdits.Delete(TcTables.GossipOption, row)], DateTime.UnixEpoch, schema: TcSchema.Instance);
        Assert.Contains("(`SpellID` = 0 OR `SpellID` IS NULL)", sql.Apply);
        Assert.Contains("`MenuID` = 5 AND", sql.Apply);
        Assert.DoesNotContain("(`MenuID` = 5", sql.Apply);
        Assert.DoesNotContain("(`OptionID` = 0 OR", sql.Apply);
    }
}
