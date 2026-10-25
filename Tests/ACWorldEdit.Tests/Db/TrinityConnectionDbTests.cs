using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// Live, read-only checks of the TrinityCore connection plumbing. They skip unless ACWORLD_TC_DB_INFO
/// names a TrinityCore world database (and optionally its hotfixes database as a sixth part).
/// </summary>
[Trait("Category", "Db")]
public class TrinityConnectionDbTests
{
    [TcDbFact]
    public async Task ATrinityCoreWorldAndItsHotfixesDatabaseConnect() =>
        Assert.Null(await DatabaseDiagnostics.TestAsync(Environments.TrinityConnection()!));

    [TcDbFact]
    public async Task TheSchemaReadFindsTheTrinityCoreTables()
    {
        var connection = Environments.TrinityConnection()!;
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        Assert.True(session.Has("world_safe_locs.LocX"));
        Assert.True(session.Has("pool_members.spawnId"));
        Assert.True(session.Has("creature.spawnDifficulties"));
        Assert.False(session.Has("game_graveyard.ID"));
    }

    [TcDbFact]
    public async Task AWrongHotfixesDatabaseIsReportedByName()
    {
        var connection = Environments.TrinityConnection()! with { Hotfixes = "no_such_hotfixes_database" };
        string? error = await DatabaseDiagnostics.TestAsync(connection);
        Assert.NotNull(error);
        Assert.StartsWith("Hotfixes database:", error);
        Assert.Contains("no_such_hotfixes_database", error);
    }

    [TcDbFact]
    public async Task ABlankHotfixesDatabaseIsOptional() =>
        Assert.Null(await DatabaseDiagnostics.TestAsync(Environments.TrinityConnection()! with { Hotfixes = "" }));

    [DbFact]
    public async Task AnAzerothCoreDatabaseIsRejectedInTrinityCoreMode()
    {
        // The Wrath server's world database has no TrinityCore-only tables, so the schema is not accepted.
        var wrath = new WorldConnection(Test.Config, null, WorldDialect.TrinityCore);
        string? error = await DatabaseDiagnostics.TestAsync(wrath);
        Assert.NotNull(error);
    }
}
