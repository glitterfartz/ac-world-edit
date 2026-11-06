using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

namespace ACWorldEdit.Tests.Db;

/// <summary>
/// The GameObject template read against both world databases: the Mailbox (32349) exists in
/// both and is a type 19 template. Read-only; the tests skip when no database is configured.
/// </summary>
[Trait("Category", "Db")]
public class GameObjectTemplateDbTests
{
    [DbFact]
    public Task AzerothCore() => Check(AcSource(Config));

    [TcDbFact]
    public Task TrinityCore() => Check(DBSource.Open(Environments.TrinityConnection()!));

    static async Task Check(IDBSource source)
    {
        var token = CancellationToken.None;
        var snapshot = await source.ReadGameObjectTemplateAsync(32349, [], rows => rows, token);
        Assert.Equal(32349u, snapshot.Entry);
        Assert.Equal("Mailbox", snapshot.Name);
        Assert.Equal(19, snapshot.Type);
        Assert.NotNull(snapshot.Rows.Get(source.Schema.Tables.Find("gameobject_template"), "32349"));
        Assert.InRange(snapshot.SpawnCount, 0, int.MaxValue);
        Assert.True(snapshot.Spawns.Count <= 20);
        Assert.True(snapshot.Spawns.Count <= snapshot.SpawnCount);

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadGameObjectTemplateAsync(uint.MaxValue, [], rows => rows, token));
    }
}
