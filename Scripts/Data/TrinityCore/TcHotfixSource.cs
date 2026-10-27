using ACWorldEdit.Assets;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only access to a TrinityCore `hotfixes` database. Its tables hold sparse overrides of client
/// DB2 rows, keyed by the DB2 row ID: a row here replaces the client's row with the same ID, which is
/// how a live client applies it. Nothing is written; each read opens its own read-only snapshot.
/// </summary>
public sealed class TcHotfixSource(DatabaseSettings? settings)
{
    public bool Available => settings is { IsComplete: true };

    async Task<MySqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new MySqlConnection(settings!.BuildConnectionString());
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The server's creature_display_info rows (the first three skins and the humanoid extra) and the file IDs of its creature_model_data rows.</summary>
    public async Task<(IReadOnlyList<CreatureDisplayHotfix> Displays, IReadOnlyDictionary<uint, uint> ModelFiles)> ReadCreatureDisplaysAsync(CancellationToken token)
    {
        if (!Available)
        {
            return ([], new Dictionary<uint, uint>());
        }

        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        List<CreatureDisplayHotfix> displays = [];
        await using (var command = new MySqlCommand("SELECT ID,ModelID,CreatureModelScale,TextureVariationFileDataID1,TextureVariationFileDataID2,TextureVariationFileDataID3,ExtendedDisplayInfoID FROM creature_display_info", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                displays.Add(new(Convert.ToUInt32(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)), reader.GetFloat(2),
                    [(uint)Math.Max(0, Convert.ToInt32(reader.GetValue(3))), (uint)Math.Max(0, Convert.ToInt32(reader.GetValue(4))), (uint)Math.Max(0, Convert.ToInt32(reader.GetValue(5)))], Convert.ToInt32(reader.GetValue(6))));
            }
        }

        var files = new Dictionary<uint, uint>();
        await using (var command = new MySqlCommand("SELECT ID,FileDataID FROM creature_model_data", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                files[Convert.ToUInt32(reader.GetValue(0))] = (uint)Math.Max(0, Convert.ToInt32(reader.GetValue(1)));
            }
        }

        return (displays, files);
    }

    /// <summary>
    /// broadcast_text rows for these IDs as (male, female) text. The table is keyed by (ID,
    /// VerifiedBuild), so an ID can have several versions; the highest build wins.
    /// </summary>
    public async Task<IReadOnlyDictionary<uint, (string Male, string Female)>> ReadBroadcastTextsAsync(IReadOnlyCollection<uint> ids, CancellationToken token)
    {
        var texts = new Dictionary<uint, (string, string)>();
        if (!Available || ids.Count == 0)
        {
            return texts;
        }

        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        await using var command = new MySqlCommand("", connection, transaction);
        string list = string.Join(',', ids.Distinct().Take(DbLimits.MaxRows).Select((id, i) =>
        {
            command.Parameters.AddWithValue($"@id{i}", id);
            return $"@id{i}";
        }));
        command.CommandText = $"SELECT ID,COALESCE(Text,''),COALESCE(Text1,'') FROM broadcast_text WHERE ID IN ({list}) ORDER BY ID,VerifiedBuild";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            string male = reader.GetString(1), female = reader.GetString(2);
            texts[Convert.ToUInt32(reader.GetValue(0))] = (male.Length > 0 ? male : female, female.Length > 0 ? female : male);
        }

        return texts;
    }

    /// <summary>Broadcast texts whose male or female text contains the term, newest build of an ID first, bounded.</summary>
    public async Task<IReadOnlyList<(uint Id, string Text)>> SearchBroadcastTextsAsync(string term, int limit, CancellationToken token)
    {
        if (!Available)
        {
            return [];
        }

        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT ID,CASE WHEN COALESCE(Text,'')<>'' THEN Text ELSE COALESCE(Text1,'') END FROM broadcast_text WHERE Text LIKE @s ESCAPE '=' OR Text1 LIKE @s ESCAPE '=' ORDER BY CHAR_LENGTH(COALESCE(Text,'')),ID,VerifiedBuild DESC LIMIT @n", connection, transaction);
        command.Parameters.AddWithValue("@s", "%" + term.Replace("=", "==", StringComparison.Ordinal).Replace("%", "=%", StringComparison.Ordinal).Replace("_", "=_", StringComparison.Ordinal) + "%");
        command.Parameters.AddWithValue("@n", limit);
        List<(uint, string)> results = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            // An ID can have several builds; the newest comes first and wins.
            uint id = Convert.ToUInt32(reader.GetValue(0));
            if (results.All(r => r.Item1 != id))
            {
                results.Add((id, reader.GetString(1)));
            }
        }

        return results;
    }

    /// <summary>
    /// The server's item_sparse overrides for these item IDs as (name, quality); the highest build of an
    /// ID wins. An item the table does not list keeps the client's data.
    /// </summary>
    public async Task<IReadOnlyDictionary<uint, (string Name, int Quality)>> ReadItemOverridesAsync(IReadOnlyCollection<uint> ids, CancellationToken token)
    {
        var items = new Dictionary<uint, (string, int)>();
        if (!Available || ids.Count == 0)
        {
            return items;
        }

        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        await using var command = new MySqlCommand("", connection, transaction);
        string list = string.Join(',', ids.Distinct().Take(DbLimits.MaxRows).Select((id, i) =>
        {
            command.Parameters.AddWithValue($"@id{i}", id);
            return $"@id{i}";
        }));
        command.CommandText = $"SELECT ID,COALESCE(Display,''),OverallQualityID FROM item_sparse WHERE ID IN ({list}) ORDER BY ID,VerifiedBuild";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            items[Convert.ToUInt32(reader.GetValue(0))] = (reader.GetString(1), Convert.ToInt32(reader.GetValue(2)));
        }

        return items;
    }

    /// <summary>The server's area_trigger overrides, in the shape the client lookup returns.</summary>
    public async Task<IReadOnlyList<ClientAreaTrigger>> ReadAreaTriggersAsync(CancellationToken token)
    {
        if (!Available)
        {
            return [];
        }

        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT ID,ContinentID,PosX,PosY,PosZ,Radius,BoxLength,BoxWidth,BoxHeight,BoxYaw,ShapeType,PhaseID,PhaseGroupID,PhaseUseFlags FROM area_trigger", connection, transaction);
        List<ClientAreaTrigger> rows = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            rows.Add(new(Convert.ToUInt32(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)), reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6), reader.GetFloat(7), reader.GetFloat(8), reader.GetFloat(9),
                (byte)Math.Max(0, Convert.ToInt32(reader.GetValue(10))), (uint)Math.Max(0, Convert.ToInt32(reader.GetValue(11))), (uint)Math.Max(0, Convert.ToInt32(reader.GetValue(12))), (uint)Math.Max(0, Convert.ToInt32(reader.GetValue(13)))));
        }

        return rows;
    }
}
