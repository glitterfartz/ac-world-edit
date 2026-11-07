using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Data;

/// <summary>
/// Pins the change_log DDL so it stays runnable on hosted servers: no TEXT/BLOB DEFAULT
/// (MySQL before 8.0.13 and MariaDB reject those with error 1101), every column the store
/// reads and writes present, writes always binding their own values.
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
                     "`fingerprint`", "`source_key`", "`dialect`", "`label`", "`apply_sql`",
                     "`revert_sql`", "`status`", "`notes`", "`created_at`", "`applied_at`", "`reverted_at`",
                 })
        {
            Assert.Contains(column, ddl);
        }

        Assert.Contains("`acworld`.`change_log`", ddl);
        Assert.Contains("ENGINE=InnoDB", ddl);
    }

    [Fact]
    public void NoTextOrBlobColumnCarriesADefault()
    {
        foreach (string statement in new[] { AcworldStore.CreateTableStatement(Acworld()) }.Concat(AcworldStore.MigrationStatements(Acworld())))
        {
            foreach (string line in statement.Split('\n'))
            {
                string trimmed = line.Trim();
                bool isText = trimmed.Contains("TEXT", StringComparison.OrdinalIgnoreCase);
                Assert.False(isText && trimmed.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase),
                    $"TEXT/BLOB columns must not declare DEFAULT (error 1101 on older servers): {trimmed}");
            }
        }
    }

    [Fact]
    public void MigrationsCoverEarlierShapes()
    {
        var migrations = AcworldStore.MigrationStatements(Acworld());
        Assert.Equal(3, migrations.Count);
        Assert.All(migrations, statement => Assert.Contains("`acworld`.`change_log`", statement));
    }

    [Fact]
    public void QualifiedTableEscapesBackticks()
    {
        Assert.Equal("`a``b`.`change_log`", AcworldStore.QualifiedTable(Acworld() with { Database = "a`b" }));
    }
}
