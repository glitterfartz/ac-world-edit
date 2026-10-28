using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Sql;

/// <summary>
/// Text-level checks of the generated apply/revert scripts: identifier quoting, FLOAT and text
/// guards, symmetry and the empty diff. The scripts are never executed here.
/// </summary>
public class SqlExportScriptTests
{
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    static SqlScripts Rows(params RowEdit[] edits) => SqlExport.Build("src", [], [], edits, Stamp);

    static RowEdit Update(TableSchema table, Row before, Row after) => new(table.Name, table.KeyOf(before), before, after);

    static IEnumerable<string> Statements(string script, string verb) =>
        script.Split('\n').Where(l => l.StartsWith(verb + " ", StringComparison.Ordinal));

    [Fact]
    public void EmptyDiffProducesAHeaderAndNoStatements()
    {
        var scripts = SqlExport.Build("src", [], [], [], Stamp);
        Assert.Equal(0, scripts.Changes);
        Assert.Empty(scripts.Summary);
        foreach (string script in new[] { scripts.Apply, scripts.Revert })
        {
            Assert.Contains("Nothing to export", script);
            Assert.DoesNotContain("START TRANSACTION", script);
            Assert.Empty(Statements(script, "UPDATE"));
            Assert.Empty(Statements(script, "INSERT"));
            Assert.Empty(Statements(script, "DELETE"));
        }
    }

    [Fact]
    public void IdentifiersAreBacktickQuotedIncludingKeywordLikeNames()
    {
        // creature_text has Text/Type/Language-style names; npc_text and creature_template cover the rest.
        var before = Row.Of(("entry", 5u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", "a"));
        var after = before.With("npcflag", 3u);
        string apply = Rows(Update(AcTables.CreatureTemplate, before, after)).Apply;
        string update = Statements(apply, "UPDATE").Single();
        Assert.Contains("`creature_template` SET `npcflag` = 3 WHERE `entry` = 5 AND `npcflag` = 1;", update);
        var text = Row.Of(AcTables.CreatureText.Columns.Select(c => (c.Name, c.Name == "Text" ? "hi" : c.Kind == ColumnKind.Text ? "" : c.Kind == ColumnKind.Float ? (object)0f : 0u)).ToArray());
        string insert = Statements(Rows(new RowEdit(AcTables.CreatureText.Name, AcTables.CreatureText.KeyOf(text), null, text)).Apply, "INSERT").Single();
        Assert.Contains("(`CreatureID`, `GroupID`, `ID`, `Text`", insert);
    }

    [Fact]
    public void BacktickInAnIdentifierIsDoubled()
    {
        // Identifiers come from code, but the quoting must still be safe: exercise through a text value instead.
        Assert.Equal("'a`b'", SqlExport.Quote("a`b"));
    }

    [Fact]
    public void FloatGuardsUseToleranceNotEquality()
    {
        var before = Row.Of(("ID", 1u), ("text0_0", ""), ("text0_1", ""), ("BroadcastTextID0", 0), ("Probability0", "0.1"));
        var after = before.With("Probability0", "0.7");
        var scripts = Rows(Update(AcTables.NpcText, before, after));
        string update = Statements(scripts.Apply, "UPDATE").Single();
        Assert.Contains("ABS(`Probability0` - (0.1)) < 0.0001", update);
        Assert.DoesNotContain("`Probability0` = 0.1", update);
        Assert.Contains("SET `Probability0` = 0.7", update);
    }

    [Fact]
    public void CreaturePositionUsesRoundTripNumbersAndToleranceGuards()
    {
        var before = new WorldPose(0, 1e-5, -2.5, 3, 0.25);
        var after = before with { X = 1234.5678901234567 };
        var scripts = SqlExport.Build("src", [new("db", 9, before, after)], [], [], Stamp);
        string apply = Statements(scripts.Apply, "UPDATE").Single();
        Assert.Contains("ABS(`position_x` - (1E-05)) < 0.0001", apply);
        Assert.Contains("`position_x` = 1234.5678901234567", apply);
        Assert.DoesNotContain("NaN", apply);
    }

    [Fact]
    public void TextGuardsTreatNullAsEmptyAndCompareBinary()
    {
        var before = Row.Of(("entry", 5u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", ""));
        var after = before.With("subname", "Innkeeper");
        string update = Statements(Rows(Update(AcTables.CreatureTemplate, before, after)).Apply, "UPDATE").Single();
        Assert.Contains("BINARY COALESCE(`subname`, '') = BINARY ''", update);
        Assert.Contains("SET `subname` = 'Innkeeper'", update);
    }

    [Fact]
    public void RevertSwapsSetAndGuardValuesAndRunsInReverseOrder()
    {
        var a = Row.Of(("entry", 1u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", ""));
        var b = Row.Of(("entry", 2u), ("npcflag", 5u), ("gossip_menu_id", 0u), ("subname", ""));
        var scripts = Rows(Update(AcTables.CreatureTemplate, a, a.With("npcflag", 2u)), Update(AcTables.CreatureTemplate, b, b.With("npcflag", 6u)));
        var apply = Statements(scripts.Apply, "UPDATE").ToList();
        var revert = Statements(scripts.Revert, "UPDATE").ToList();
        Assert.Equal(2, apply.Count);
        Assert.Contains("`entry` = 1", apply[0]);
        Assert.Contains("SET `npcflag` = 2 WHERE `entry` = 1 AND `npcflag` = 1", apply[0]);
        Assert.Contains("`entry` = 2", revert[0]);
        Assert.Contains("SET `npcflag` = 5 WHERE `entry` = 2 AND `npcflag` = 6", revert[0]);
        Assert.Contains("SET `npcflag` = 1 WHERE `entry` = 1 AND `npcflag` = 2", revert[1]);
    }

    [Fact]
    public void InsertIsUnguardedAndRevertDeletesTheFullRow()
    {
        var row = Row.Of(("MenuID", 9u), ("TextID", 1u));
        var scripts = Rows(new RowEdit(AcTables.GossipMenu.Name, AcTables.GossipMenu.KeyOf(row), null, row));
        string insert = Statements(scripts.Apply, "INSERT").Single();
        Assert.DoesNotContain("WHERE", insert);
        Assert.Contains("DELETE FROM `gossip_menu` WHERE `MenuID` = 9 AND `TextID` = 1;", scripts.Revert);
        Assert.Contains("'ok'", scripts.Apply);
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it\\'s'")]
    [InlineData("back\\slash", "'back\\\\slash'")]
    [InlineData("line\nbreak\r", "'line\\nbreak\\r'")]
    [InlineData("nul\0", "'nul\\0'")]
    [InlineData("ctrl\u001aZ", "'ctrl\\ZZ'")]
    [InlineData("'; DROP TABLE creature; --", "'\\'; DROP TABLE creature; --'")]
    public void QuoteEscapesForTheDefaultSqlMode(string text, string expected) => Assert.Equal(expected, SqlExport.Quote(text));

    [Fact]
    public void ScriptsStayOneStatementPerLineWhateverTheText()
    {
        var before = Row.Of(("entry", 5u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", ""));
        var after = before.With("subname", "evil\n'; DELETE FROM creature; --\r\nmore");
        string apply = Rows(Update(AcTables.CreatureTemplate, before, after)).Apply;
        Assert.DoesNotContain(apply.Split('\n'), l => l.StartsWith("DELETE FROM `creature`", StringComparison.Ordinal));
        Assert.Single(Statements(apply, "UPDATE"));
    }
}
