using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

namespace ACWorldEdit.Tests.Db;

/// <summary>
/// Live, read-only checks of the movement reads: path users (creature addons plus SmartAI), single
/// path rows for the edit baseline, formation rows and path ID maxima. Each test finds real rows
/// first, so it does not depend on particular data beyond well-known Goldshire paths.
/// </summary>
[Trait("Category", "Db")]
public class MovementDbTests
{
    static async Task<ulong> Find(WorldConnection connection, string sql)
    {
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var command = session.ReadOnly(sql);
        return Convert.ToUInt64(await command.ExecuteScalarAsync());
    }

    static async Task<uint> FindPath(WorldConnection connection, string sql)
    {
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var command = session.ReadOnly(sql);
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToUInt32(value);
    }

    static async Task CheckPathUsers(WorldConnection connection, uint pathId)
    {
        var source = DBSource.Open(connection);
        var users = Assert.Single(await source.ReadPathUsersAsync([pathId], default));
        Assert.Equal(pathId, users.PathId);
        Assert.NotEmpty(users.Creatures);
        Assert.All(users.Creatures, u => Assert.True(u.Guid > 0 && u.Entry > 0 && u.Name.Length > 0));
    }

    [DbFact]
    public async Task AzerothCoreReportsPathUsersAndRows()
    {
        var connection = new WorldConnection(Config, null, WorldDialect.AzerothCore);
        var source = DBSource.Open(connection);
        uint path = await FindPath(connection, "SELECT `path_id` FROM `creature_addon` WHERE `path_id`<>0 LIMIT 1");
        Assert.True(path > 0);
        await CheckPathUsers(connection, path);

        var rows = await source.ReadWaypointPathRowsAsync(path, default);
        var nodes = rows.All(AcTables.WaypointData).OrderBy(r => r.U("point")).ToArray();
        Assert.True(nodes.Length > 0);
        foreach (var row in nodes)
        {
            row.Validate(AcTables.WaypointData);
        }

        Assert.True(await source.MaxIdAsync(AcTables.WaypointData, "id", default) >= path);

        var formations = await source.ReadFormationsAsync([3453], default);
        Assert.NotNull(formations);
    }

    [TcDbFact]
    public async Task TrinityCoreReportsPathUsersRowsAndSmartRules()
    {
        var connection = Environments.TrinityConnection()!;
        var source = DBSource.Open(connection);
        // Goldshire path from the waypoint smoke: walked, with a header comment.
        await CheckPathUsers(connection, 22453760);

        var rows = await source.ReadWaypointPathRowsAsync(22453760, default);
        var nodes = rows.All(TcTables.WaypointPathNode).OrderBy(r => r.U("NodeId")).ToArray();
        Assert.Equal(92, nodes.Length);
        Assert.Equal(1u, nodes[0].U("NodeId"));
        foreach (var row in nodes)
        {
            row.Validate(TcTables.WaypointPathNode);
        }

        var header = Assert.Single(rows.All(TcTables.WaypointPath));
        header.Validate(TcTables.WaypointPath);
        Assert.Equal("Migrated from refactor", header["Comment"]);
        Assert.True(await source.MaxIdAsync(TcTables.WaypointPath, "PathId", default) >= 22453760);

        // A path started by SmartAI action 53 names its rule as a user.
        uint smartPath = await FindPath(connection, "SELECT `action_param2` FROM `smart_scripts` WHERE `action_type`=53 LIMIT 1");
        Assert.True(smartPath > 0);
        var smartUsers = Assert.Single(await source.ReadPathUsersAsync([smartPath], default));
        Assert.Contains(smartUsers.Smart, u => u.Action == 53);
    }

    [TcDbFact]
    public async Task TrinityCoreReadsFormationRows()
    {
        var source = DBSource.Open(Environments.TrinityConnection()!);
        var rows = await source.ReadFormationsAsync([3453], default);
        var members = rows.All(TcTables.CreatureFormations).ToArray();
        Assert.True(members.Length >= 3);
        foreach (var row in members)
        {
            row.Validate(TcTables.CreatureFormations);
        }

        Assert.Contains(members, r => r.UL("leaderGUID") == 3453 && r.UL("memberGUID") == 3453);
        Assert.Empty((await source.ReadFormationsAsync([], default)).All(TcTables.CreatureFormations));
        Assert.Empty(await source.ReadPathUsersAsync([], default));
        Assert.Empty((await source.ReadWaypointPathRowsAsync(uint.MaxValue, default)).All(TcTables.WaypointPathNode));
    }
}
