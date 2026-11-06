using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>A creature spawn walking a path: Walks is false when its MovementType is not 2, and
/// FromTemplate when the path comes from its template's addon (the spawn has no addon row of its own).</summary>
public sealed record PathCreatureUser(ulong Guid, uint Entry, string Name, bool Walks, bool FromTemplate);

/// <summary>A SmartAI rule starting a path: Owner is the entryorguid (negative for one spawn, an
/// entry or action-list id otherwise), Action the action type that names the path.</summary>
public sealed record PathSmartUser(long Owner, int SourceType, int Action, uint PathId);

/// <summary>Every database user of some waypoint paths: creature spawns/templates plus SmartAI rules.</summary>
public sealed record PathUsers(uint PathId, PathCreatureUser[] Creatures, PathSmartUser[] Smart);

/// <summary>
/// All users of some waypoint paths: the spawns whose own addon names the path, the spawns that
/// inherit it from their template's addon, and the SmartAI rules that start it (TrinityCore action
/// 53 in action_param2; AzerothCore action 53 in param2, 232 in param1, 233 in param1/param2).
/// A table the database lacks is skipped, so a trimmed schema reports what it can.
/// </summary>
internal static class DbPathUsers
{
    const int MaxPaths = 200;

    public static async Task<IReadOnlyList<PathUsers>> ReadAsync(WorldDatabase database, WorldDialect dialect, IReadOnlyList<uint> pathIds, CancellationToken token)
    {
        var wanted = pathIds.Distinct().Take(MaxPaths).ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        bool tc = dialect == WorldDialect.TrinityCore;
        string column = tc ? "PathId" : "path_id";
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        if (!session.Has($"creature_addon.{column}"))
        {
            return [];
        }

        var creatures = await ReadCreatures(session, tc, column, wanted, token).ConfigureAwait(false);
        var smart = await ReadSmart(session, tc, wanted, token).ConfigureAwait(false);
        return wanted.Select(id => new PathUsers(id,
            creatures.TryGetValue(id, out var users) ? users.ToArray() : [],
            smart.TryGetValue(id, out var rules) ? rules.ToArray() : [])).ToArray();
    }

    static async Task<Dictionary<uint, List<PathCreatureUser>>> ReadCreatures(WorldReadSession session, bool tc, string column, uint[] wanted, CancellationToken token)
    {
        var users = new Dictionary<uint, List<PathCreatureUser>>();
        void Add(uint path, PathCreatureUser user)
        {
            if (!users.TryGetValue(path, out var list))
            {
                users[path] = list = [];
            }

            list.Add(user);
        }

        string id = !tc && session.Has("creature.id1") ? "c.id1" : "c.id";
        // Own addon rows name the path directly.
        await using (var command = session.Command(""))
        {
            string list = WorldReadSession.InList(command, wanted, "path");
            command.CommandText = $"""
                SELECT a.guid,{id},COALESCE(t.name,''),c.MovementType,a.`{column}` FROM creature_addon a
                JOIN creature c ON c.guid=a.guid
                JOIN creature_template t ON t.entry={id}
                WHERE a.`{column}` IN ({list})
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                Add(Convert.ToUInt32(reader.GetValue(4)), new(Convert.ToUInt64(reader.GetValue(0)),
                    Convert.ToUInt32(reader.GetValue(1)), reader.GetString(2), Convert.ToInt32(reader.GetValue(3)) == 2, false));
            }
        }

        // Spawns without an addon row inherit their template's path (a spawn with an addon row
        // ignores the template's entirely, even when its own path is 0).
        if (session.Has($"creature_template_addon.{column}"))
        {
            await using var command = session.Command("");
            string templates = WorldReadSession.InList(command, wanted, "template");
            command.CommandText = $"""
                SELECT c.guid,{id},COALESCE(t.name,''),c.MovementType,cta.`{column}` FROM creature c
                JOIN creature_template t ON t.entry={id}
                JOIN creature_template_addon cta ON cta.entry={id}
                LEFT JOIN creature_addon ca ON ca.guid=c.guid
                WHERE cta.`{column}` IN ({templates}) AND ca.guid IS NULL
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                Add(Convert.ToUInt32(reader.GetValue(4)), new(Convert.ToUInt64(reader.GetValue(0)),
                    Convert.ToUInt32(reader.GetValue(1)), reader.GetString(2), Convert.ToInt32(reader.GetValue(3)) == 2, true));
            }
        }

        return users;
    }

    static async Task<Dictionary<uint, List<PathSmartUser>>> ReadSmart(WorldReadSession session, bool tc, uint[] wanted, CancellationToken token)
    {
        var users = new Dictionary<uint, List<PathSmartUser>>();
        if (!session.Has("smart_scripts.action_type") || !session.Has("smart_scripts.action_param1") || !session.Has("smart_scripts.action_param2"))
        {
            return users;
        }

        // (action, param column): every rule action that names a waypoint path.
        (int Action, int Param)[] uses = tc
            ? [(53, 2)]
            : [(53, 2), (232, 1), (233, 1), (233, 2)];
        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, wanted, "path");
        string match = string.Join(" OR ", uses.Select(u => $"(action_type={u.Action} AND action_param{u.Param} IN ({list}))"));
        command.CommandText = $"SELECT entryorguid,source_type,action_type,action_param1,action_param2 FROM smart_scripts WHERE {match}";
        var wantedSet = wanted.ToHashSet();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            long owner = Convert.ToInt64(reader.GetValue(0));
            int source = Convert.ToInt32(reader.GetValue(1)), action = Convert.ToInt32(reader.GetValue(2));
            uint param1 = Convert.ToUInt32(reader.GetValue(3)), param2 = Convert.ToUInt32(reader.GetValue(4));
            foreach (var use in uses)
            {
                if (use.Action != action)
                {
                    continue;
                }

                uint path = use.Param == 1 ? param1 : param2;
                if (!wantedSet.Contains(path))
                {
                    continue;
                }

                if (!users.TryGetValue(path, out var list2))
                {
                    users[path] = list2 = [];
                }

                list2.Add(new(owner, source, action, path));
            }
        }

        return users;
    }
}
