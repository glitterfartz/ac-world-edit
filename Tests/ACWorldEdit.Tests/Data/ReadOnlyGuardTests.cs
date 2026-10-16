using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Data;

/// <summary>The guard is verified on text only; no write statement is ever sent to a database.</summary>
public class ReadOnlyGuardTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("  select * from creature where id = 1;  ")]
    [InlineData("SHOW COLUMNS FROM creature")]
    [InlineData("DESCRIBE creature")]
    [InlineData("-- note\nSELECT 'a;b' AS semi, 'DELETE FROM x' AS text")]
    [InlineData("/* DELETE */ SELECT `into`.id FROM t `into`")]
    public void AcceptsSingleReadStatements(string sql) => Assert.Equal(sql, ReadOnlyGuard.Require(sql));

    [Theory]
    [InlineData("INSERT INTO creature VALUES (1)")]
    [InlineData("UPDATE creature SET id = 1")]
    [InlineData("DELETE FROM creature")]
    [InlineData("REPLACE INTO creature VALUES (1)")]
    [InlineData("DROP TABLE creature")]
    [InlineData("TRUNCATE creature")]
    [InlineData("ALTER TABLE creature ADD x INT")]
    [InlineData("GRANT ALL ON *.* TO x")]
    [InlineData("CALL do_things()")]
    [InlineData("SET @a = 1")]
    [InlineData("WITH c AS (SELECT 1) DELETE FROM creature")]
    [InlineData("EXPLAIN ANALYZE SELECT 1")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("")]
    [InlineData("   ;  ")]
    public void RejectsNonReadStatements(string sql) => Assert.Throws<InvalidOperationException>(() => ReadOnlyGuard.Require(sql));

    [Theory]
    [InlineData("SELECT 1; DELETE FROM creature")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT * FROM creature FOR UPDATE")]
    [InlineData("SELECT * FROM creature LOCK IN SHARE MODE")]
    [InlineData("SELECT * FROM creature INTO OUTFILE 'x'")]
    [InlineData("SELECT SLEEP(10)")]
    [InlineData("SELECT 1 INTO @a")]
    public void RejectsStackedOrSideEffectingSelects(string sql) => Assert.Throws<InvalidOperationException>(() => ReadOnlyGuard.Require(sql));
}
