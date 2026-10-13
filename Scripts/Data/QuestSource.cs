using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// A creature that starts or ends a quest, for the quest editor's links back to the world.
/// </summary>
public sealed record QuestGiverLink(uint Entry, string Name, bool Starts);
public sealed record QuestDetails(QuestInfo Info, int QuestType, uint Flags, QuestGiverLink[] Givers);

/// <summary>
/// Read-only quest reads for the quest editor skeleton: bounded search and one quest's summary with
/// its creature starters/enders. Quest editing is not implemented yet.
/// </summary>
public sealed class QuestSource(string configPath)
{
    readonly WorldDatabase database = new(configPath);

    public async Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return await InteractionSource.SearchQuests(session, term, token);
    }

    public async Task<QuestDetails?> ReadAsync(uint id, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        var info = (await InteractionSource.ReadQuests(session, [id], token)).FirstOrDefault();
        if (info == null)
        {
            return null;
        }

        int type = 0;
        uint flags = 0;
        await using (var command = session.Command("SELECT QuestType,Flags FROM quest_template WHERE ID=@id"))
        {
            command.Parameters.AddWithValue("@id", id);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                type = Convert.ToInt32(reader.GetValue(0));
                flags = Convert.ToUInt32(reader.GetValue(1));
            }
        }

        List<QuestGiverLink> givers = [];
        await using (var command = session.Command("""
            SELECT s.id,COALESCE(t.name,''),1 FROM creature_queststarter s LEFT JOIN creature_template t ON t.entry=s.id WHERE s.quest=@id
            UNION ALL
            SELECT e.id,COALESCE(t.name,''),0 FROM creature_questender e LEFT JOIN creature_template t ON t.entry=e.id WHERE e.quest=@id
            LIMIT 100
            """))
        {
            command.Parameters.AddWithValue("@id", id);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                givers.Add(new QuestGiverLink(reader.GetUInt32(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)) == 1));
            }
        }

        return new QuestDetails(info, type, flags, givers.ToArray());
    }
}
