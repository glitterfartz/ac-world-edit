using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// Row value semantics, schema validation, composite keys and RowSet layering.
/// </summary>
public class InteractionRowsTests
{
    static Row Template(uint entry = 42, uint npcflag = 0, uint menu = 0, string subname = "") =>
        Row.Of(("entry", entry), ("npcflag", npcflag), ("gossip_menu_id", menu), ("subname", subname));

    [Fact]
    public void RowsCompareByValueRegardlessOfInsertionOrder()
    {
        var a = Row.Of(("entry", 1u), ("npcflag", 2u));
        var b = Row.Of(("npcflag", 2u), ("entry", 1u));
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.With("npcflag", 3u));
        Assert.True(a != a.With("npcflag", 3u));
        Assert.False(a.Equals(null));
        Assert.True((Row?)null == null);
    }

    [Fact]
    public void WithReturnsACopyAndLeavesTheOriginalAlone()
    {
        var a = Row.Of(("entry", 1u));
        var b = a.With("entry", 2u);
        Assert.Equal("1", a["entry"]);
        Assert.Equal("2", b["entry"]);
    }

    [Fact]
    public void NumbersAreStoredInInvariantForm()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var row = Row.Of(("x", 1.5f), ("d", 2.25), ("n", -7), ("t", "hi"));
            Assert.Equal("1.5", row["x"]);
            Assert.Equal("2.25", row["d"]);
            Assert.Equal("-7", row["n"]);
            Assert.Equal(1.5f, row.F("x"));
            Assert.Equal(-7, row.I("n"));
            Assert.Equal("hi", row.T("t"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FloatsRoundTripExactly()
    {
        float value = 0.1f + 0.2f;
        Assert.Equal(value, Row.Of(("x", value)).F("x"));
    }

    [Fact]
    public void MissingColumnThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => Row.Of(("entry", 1u))["nope"]);
    }

    [Fact]
    public void ValidateAcceptsExactlyTheSchemaColumns()
    {
        Template().Validate(AcTables.CreatureTemplate);
        Assert.Throws<InvalidDataException>(() => Row.Of(("entry", 1u)).Validate(AcTables.CreatureTemplate));
        Assert.Throws<InvalidDataException>(() => Template().With("extra", 1u).Validate(AcTables.CreatureTemplate));
        Assert.Throws<InvalidDataException>(() => Row.Of(("entry", 1u), ("npcflag", 0u), ("gossip_menu_id", 0u), ("other", "")).Validate(AcTables.CreatureTemplate));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("4294967296")]
    [InlineData("+1")]
    public void UIntColumnsRejectBadNumbers(string bad)
    {
        var row = Template();
        row.Values["npcflag"] = bad;
        var error = Assert.Throws<InvalidDataException>(() => row.Validate(AcTables.CreatureTemplate));
        Assert.Contains("npcflag", error.Message);
    }

    [Theory]
    [InlineData("-5", true)]
    [InlineData("2147483647", true)]
    [InlineData("2147483648", false)]
    [InlineData("x", false)]
    public void IntColumnsParseSignedValues(string value, bool ok)
    {
        var row = Row.Of(("entry", 1u), ("item", 0), ("ExtendedCost", 0u), ("slot", 0), ("maxcount", 0u), ("incrtime", 0u));
        row.Values["item"] = value;
        if (ok)
        {
            row.Validate(AcTables.Vendor);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => row.Validate(AcTables.Vendor));
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public void FloatColumnsRejectNonFinite(string bad)
    {
        var row = SmartCatalog.Ac.NewRow(new SmartScope(1, 0), 0, 4, 11, 2);
        row.Values["target_x"] = bad;
        Assert.Throws<InvalidDataException>(() => row.Validate(AcTables.SmartScripts));
    }

    [Fact]
    public void TextLimitAppliesOnlyToBoundedColumns()
    {
        Template(subname: new string('x', 100)).Validate(AcTables.CreatureTemplate);
        var error = Assert.Throws<InvalidDataException>(() => Template(subname: new string('x', 101)).Validate(AcTables.CreatureTemplate));
        Assert.Contains("at most 100", error.Message);
        // npc_text columns are unbounded (limit 0).
        var text = Row.Of(("ID", 1u), ("text0_0", new string('y', 100_000)), ("text0_1", ""), ("BroadcastTextID0", 0), ("Probability0", 1f));
        text.Validate(AcTables.NpcText);
    }

    [Fact]
    public void CompositeKeysJoinKeyColumnsInSchemaOrder()
    {
        var vendor = Row.Of(("entry", 5u), ("item", 2589), ("ExtendedCost", 7u), ("slot", 0), ("maxcount", 0u), ("incrtime", 0u));
        Assert.Equal("5|2589|7", AcTables.Vendor.KeyOf(vendor));
        Assert.Equal("42", AcTables.CreatureTemplate.KeyOf(Template()));
    }

    [Fact]
    public void SchemaLookupFindsEveryTableAndRejectsUnknown()
    {
        foreach (var table in AcTables.Tables)
        {
            Assert.Same(table, ((ITableCatalog)AcTables.Instance).Find(table.Name));
            Assert.All(table.Key, k => Assert.True(table.Has(k), $"{table.Name} key {k} is not a column"));
            Assert.Equal(table.Columns.Length, table.Columns.Select(c => c.Name).Distinct().Count());
        }

        Assert.Equal(AcTables.Tables.Length, AcTables.Tables.Select(t => t.Name).Distinct().Count());
        Assert.Throws<InvalidDataException>(() => ((ITableCatalog)AcTables.Instance).Find("characters"));
    }

    [Fact]
    public void CreatureAiIsALogicalSplitOfCreatureTemplate()
    {
        Assert.Equal("creature_template", AcTables.CreatureAI.SqlName);
        Assert.Equal("creature_ai", AcTables.CreatureAI.Name);
        Assert.Equal("quest_template", AcTables.QuestTemplate.SqlName);
    }

    [Fact]
    public void CreatureTemplateColumnGroupsDoNotOverlap()
    {
        var groups = AcTables.Tables.Where(t => t.SqlName == "creature_template").ToArray();
        Assert.Contains(AcTables.CreatureDetails, groups);

        var columns = groups.SelectMany(t => t.Columns.Select(c => c.Name).Where(n => n != "entry")).ToArray();
        Assert.Equal(columns.Length, columns.Distinct().Count());
        Assert.DoesNotContain("VerifiedBuild", columns);
    }

    [Fact]
    public void RowSetSetGetRemoveAndAll()
    {
        var rows = new RowSet();
        Assert.Null(rows.Get(AcTables.CreatureTemplate, "42"));
        Assert.Empty(rows.All(AcTables.CreatureTemplate));
        rows.Set(AcTables.CreatureTemplate, Template());
        Assert.Equal(Template(), rows.Get(AcTables.CreatureTemplate, "42"));
        rows.Set(AcTables.CreatureTemplate, Template(npcflag: 3));
        Assert.Single(rows.All(AcTables.CreatureTemplate));
        Assert.Equal(3u, rows.Get(AcTables.CreatureTemplate, "42")!.U("npcflag"));
        rows.Remove(AcTables.CreatureTemplate, "42");
        rows.Remove(AcTables.CreatureTemplate, "42");
        rows.Remove(AcTables.Vendor, "nothing");
        Assert.Empty(rows.All(AcTables.CreatureTemplate));
    }

    [Fact]
    public void ApplyInsertsReplacesAndDeletes()
    {
        var rows = new RowSet();
        var table = AcTables.CreatureTemplate.Name;
        rows.Apply(new RowEdit(table, "42", null, Template()));
        Assert.NotNull(rows.Get(AcTables.CreatureTemplate, "42"));
        rows.Apply(new RowEdit(table, "42", Template(), Template(npcflag: 9)));
        Assert.Equal(9u, rows.Get(AcTables.CreatureTemplate, "42")!.U("npcflag"));
        rows.Apply(new RowEdit(table, "42", Template(npcflag: 9), null));
        Assert.Null(rows.Get(AcTables.CreatureTemplate, "42"));
        Assert.Throws<InvalidDataException>(() => rows.Apply(new RowEdit("characters", "1", null, Template())));
    }

    [Fact]
    public void CloneIsIndependentOfTheOriginal()
    {
        var original = new RowSet();
        original.Set(AcTables.CreatureTemplate, Template());
        var copy = original.Clone();
        copy.Set(AcTables.CreatureTemplate, Template(npcflag: 5));
        copy.Set(AcTables.CreatureTemplate, Template(entry: 43));
        copy.Remove(AcTables.CreatureTemplate, "42");
        Assert.Equal(0u, original.Get(AcTables.CreatureTemplate, "42")!.U("npcflag"));
        Assert.Single(original.All(AcTables.CreatureTemplate));
        Assert.Single(copy.All(AcTables.CreatureTemplate));
    }

    [Fact]
    public void RowSetKeepsTablesSeparate()
    {
        var rows = new RowSet();
        rows.Set(AcTables.QuestStarter, Row.Of(("id", 1u), ("quest", 2u)));
        Assert.Empty(rows.All(AcTables.QuestEnder));
        Assert.Null(rows.Get(AcTables.QuestEnder, "1|2"));
        Assert.NotNull(rows.Get(AcTables.QuestStarter, "1|2"));
    }
}
