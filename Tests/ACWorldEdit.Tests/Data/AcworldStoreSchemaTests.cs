using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Data;

/// <summary>
/// Pins the change_log DDL so it stays runnable on hosted servers: no TEXT/BLOB DEFAULT
/// (MySQL before 8.0.13 and MariaDB reject those with error 1101), every column the store
/// reads and writes present, and only genuinely missing columns migrated.
/// </summary>
public class AcworldStoreSchemaTests
{
    static DatabaseSettings Acworld() => new("db.example", 3306, "editor", "pw", "acworld");

    [Fact]
    public void CreateTableHasEveryColumnTheStoreReadsAndWrites()
    {
        string ddl = AcworldStore.CreateTableStatement(Acworld());
        foreach (string column in new[]
                 {
                     "`fingerprint`", "`change_key`", "`source_key`", "`dialect`", "`label`", "`apply_sql`",
                     "`revert_sql`", "`scripts_hash`", "`status`", "`notes`", "`created_at`", "`applied_at`", "`reverted_at`",
                 })
        {
            Assert.Contains(column, ddl);
        }

        Assert.Contains("`acworld`.`change_log`", ddl);
        Assert.Contains("UNIQUE KEY `uq_source_change` (`source_key`, `change_key`)", ddl);
        Assert.Contains("ENGINE=InnoDB", ddl);
    }

    [Fact]
    public void NoTextOrBlobColumnCarriesADefault()
    {
        foreach (string line in AcworldStore.CreateTableStatement(Acworld()).Split('\n'))
        {
            string trimmed = line.Trim();
            bool isText = trimmed.Contains("TEXT", StringComparison.OrdinalIgnoreCase);
            Assert.False(isText && trimmed.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase),
                $"TEXT/BLOB columns must not declare DEFAULT (error 1101 on older servers): {trimmed}");
        }
    }

    [Fact]
    public void CompleteTableNeedsNoMigrationStatements()
    {
        var columns = AcworldStore.RequiredColumns.Select(c => c.Column).ToList();
        Assert.Empty(AcworldStore.AddColumnStatements(Acworld(), columns));
    }

    [Fact]
    public void OlderTableGetsOnlyTheMissingColumns()
    {
        var migrations = AcworldStore.AddColumnStatements(Acworld(), ["notes", "applied_at", "reverted_at"]);
        Assert.Equal(2, migrations.Count);
        Assert.Contains(migrations, s => s.Contains("`change_key`"));
        Assert.Contains(migrations, s => s.Contains("`scripts_hash`"));
        Assert.DoesNotContain(migrations, s => s.Contains("`notes`"));
        Assert.All(migrations, statement => Assert.Contains("`acworld`.`change_log`", statement));
        Assert.All(migrations, statement => Assert.DoesNotContain("IF NOT EXISTS", statement));
    }

    [Fact]
    public void QualifiedTableEscapesBackticks()
    {
        Assert.Equal("`a``b`.`change_log`", AcworldStore.QualifiedTable(Acworld() with { Database = "a`b" }));
    }
}
