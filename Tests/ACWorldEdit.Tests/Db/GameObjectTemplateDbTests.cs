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
        Assert.True(snapshot.Spawns.Count <= DbLimits.MaxRows);
        Assert.True(snapshot.Spawns.Count <= snapshot.SpawnCount);

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadGameObjectTemplateAsync(uint.MaxValue, [], rows => rows, token));
    }

    /// <summary>
    /// The editor reads through the shared content draft, which on TrinityCore can hold
    /// hotfixes.broadcast_text/hotfix_data rows from a gossip-text edit. Those live in another
    /// database, so the template read must skip them instead of failing the whole window.
    /// </summary>
    [TcDbFact]
    public Task TrinityCoreWithHotfixDraftRows() => CheckWithHotfixDraft(DBSource.Open(Environments.TrinityConnection()!));

    static async Task CheckWithHotfixDraft(IDBSource source)
    {
        using var drafts = new ContentDrafts(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json"), _ => TcTables.Instance);
        drafts.Draft!.Execute(source.SourceKey, new RowSet(), "Add broadcast text", TcBroadcastText.Create(910001, "Smoke greeting, $N.", 910001));
        var keys = drafts.Draft.Keys(source.SourceKey);
        Assert.Contains(keys, k => k.Table == TcTables.BroadcastText);
        var apply = drafts.Draft.Frozen(source.SourceKey);

        var snapshot = await source.ReadGameObjectTemplateAsync(32349, keys, apply, CancellationToken.None);
        Assert.Equal(32349u, snapshot.Entry);
        Assert.Equal("Mailbox", snapshot.Name);
        Assert.Equal(19, snapshot.Type);
        var effective = drafts.Draft.Effective(source.SourceKey, snapshot.Rows);
        Assert.NotNull(effective.Get(source.Schema.Tables.Find("gameobject_template"), "32349"));
        Assert.DoesNotContain(GameObjectRules.Check(WorldDialect.TrinityCore, effective, 32349), i => i.Error);
    }
}
