using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// The content browser's template search against both world databases: by name, by exact entry, with the
/// `type` filter, and bounded. Read-only; Hogger (448, humanoid) and the Mailbox (32349, type 19) exist in both.
/// </summary>
[Trait("Category", "Db")]
public class TemplateSearchDbTests
{
    [DbFact]
    public Task AzerothCore() => Check(AcSource(Config));

    [TcDbFact]
    public Task TrinityCore() => Check(DBSource.Open(Environments.TrinityConnection()!));

    static async Task Check(IDBSource source)
    {
        var token = CancellationToken.None;
        var hogger = await source.SearchTemplatesAsync(SpawnKind.Creature, "Hogger", null, token);
        Assert.Contains(hogger, h => h is { Entry: 448, Name: "Hogger", Type: 7 });

        var byEntry = await source.SearchTemplatesAsync(SpawnKind.Creature, "448", null, token);
        Assert.Equal(448u, byEntry[0].Entry);

        Assert.Empty(await source.SearchTemplatesAsync(SpawnKind.Creature, "Hogger", 1, token));

        var mailboxes = await source.SearchTemplatesAsync(SpawnKind.GameObject, "Mailbox", 19, token);
        Assert.Contains(mailboxes, h => h.Entry == 32349);
        Assert.All(mailboxes, h => Assert.Equal(19, h.Type));

        var everything = await source.SearchTemplatesAsync(SpawnKind.GameObject, "", null, token);
        Assert.Equal(DbLimits.MaxSearch, everything.Count);
        Assert.All(everything, h => Assert.NotEqual("", h.Name));
    }
}
