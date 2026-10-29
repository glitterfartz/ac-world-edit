using System.Globalization;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only interaction reads (server ObjectMgr tables). Every query is parameterized, bounded and
/// runs in one read-only transaction. Draft-touched rows are loaded too, and references reachable in
/// the effective (database + draft) view are followed until stable, so new submenus, texts and
/// trainers created in the draft resolve against the database.
/// </summary>
public sealed class AcInteractionSource(WorldDatabase database)
{
    /// <summary>
    /// Hash of host/port/database (no credentials) identifying drafts made against this source.
    /// </summary>
    public string SourceKey => database.SourceKey;

    static readonly string[] RequiredColumns =
    [
        "creature_template.npcflag", "creature_template.gossip_menu_id", "creature_template.subname",
        "creature_template.minlevel", "creature_template.unit_class", "creature_template.HealthModifier", "creature_classlevelstats.basehp0",
        "creature_template.faction", "creature_template.speed_run", "creature_template.lootid", "creature_template.flags_extra", "creature_template.CreatureImmunitiesId",
        "gossip_menu.MenuID", "gossip_menu.TextID", "npc_text.text0_0", "npc_text.BroadcastTextID0",
        "gossip_menu_option.OptionType", "gossip_menu_option.ActionMenuID", "creature_queststarter.quest", "creature_questender.quest",
        "npc_vendor.ExtendedCost", "creature_default_trainer.TrainerId", "trainer_spell.SpellId", "npc_spellclick_spells.spell_id",
        "quest_template.LogTitle", "item_template.name"
    ];

    public async Task<InteractionSnapshot> ReadAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require(RequiredColumns);
        var rows = new RowSet();
        string name = "", scriptName = "", aiName = "";
        await using (var command = session.Command("SELECT entry,npcflag,gossip_menu_id,COALESCE(subname,''),name,COALESCE(ScriptName,''),COALESCE(AIName,'') FROM creature_template WHERE entry=@e"))
        {
            command.Parameters.AddWithValue("@e", entry);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Creature template {entry} does not exist.");
            }

            rows.Set(AcTables.CreatureTemplate, Row.Of(("entry", reader.GetUInt32(0)), ("npcflag", reader.GetUInt32(1)), ("gossip_menu_id", reader.GetUInt32(2)), ("subname", reader.GetString(3))));
            name = reader.GetString(4);
            scriptName = reader.GetString(5);
            aiName = reader.GetString(6);
        }

        await Load(session, rows, AcTables.CreatureStats, "entry=@k0", [entry], token);
        await Load(session, rows, AcTables.CreatureDetails, "entry=@k0", [entry], token);
        await Load(session, rows, AcTables.QuestStarter, "id=@k0", [entry], token);
        await Load(session, rows, AcTables.QuestEnder, "id=@k0", [entry], token);
        await Load(session, rows, AcTables.Vendor, "entry=@k0", [entry], token);
        await Load(session, rows, AcTables.DefaultTrainer, "CreatureId=@k0", [entry], token);
        await Load(session, rows, AcTables.SpellClick, "npc_entry=@k0", [entry], token);
        foreach (var (table, key) in draftKeys)
        {
            var parts = key.Split('|');
            await Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }

        // Follow menus, texts and trainers reachable in the effective view until nothing new loads.
        var loadedMenus = new HashSet<uint>();
        var loadedTexts = new HashSet<uint>();
        var loadedTrainers = new HashSet<uint>();
        for (int pass = 0; pass < 8; pass++)
        {
            var view = effective(rows);
            var menus = ReachableMenus(view).Where(loadedMenus.Add).ToArray();
            foreach (uint menu in menus)
            {
                await Load(session, rows, AcTables.GossipMenu, "MenuID=@k0", [menu], token);
                await Load(session, rows, AcTables.GossipOption, "MenuID=@k0", [menu], token);
            }

            view = effective(rows);
            var texts = view.All(AcTables.GossipMenu).Select(r => r.U("TextID")).Distinct().Where(loadedTexts.Add).ToArray();
            foreach (uint text in texts)
            {
                await Load(session, rows, AcTables.NpcText, "ID=@k0", [text], token);
            }

            var trainers = view.All(AcTables.DefaultTrainer).Select(r => r.U("TrainerId")).Where(loadedTrainers.Add).ToArray();
            foreach (uint trainer in trainers)
            {
                await Load(session, rows, AcTables.Trainer, "Id=@k0", [trainer], token);
                await Load(session, rows, AcTables.TrainerSpell, "TrainerId=@k0", [trainer], token);
            }

            if (menus.Length == 0 && texts.Length == 0 && trainers.Length == 0)
            {
                break;
            }
        }

        var lookup = new InteractionLookupData(catalog);
        var final = effective(rows);
        await ReadLookups(session, final, lookup, token);
        var scripts = await ReadScripts(session, entry, guid, token);
        var conditions = await ReadConditionCounts(session, entry, final, token);
        int spawns = Convert.ToInt32(await Scalar(session, session.Has("creature.id1") ? "SELECT COUNT(*) FROM creature WHERE id1=@k0" : "SELECT COUNT(*) FROM creature WHERE id=@k0", [entry], token));
        return new InteractionSnapshot(entry, guid, name, scriptName, aiName, spawns, rows, scripts, conditions, lookup, database.SourceKey);
    }

    /// <summary>
    /// Menus reachable from the template's root through ActionMenuID, bounded like the view.
    /// </summary>
    static IEnumerable<uint> ReachableMenus(RowSet view)
    {
        var template = view.All(AcTables.CreatureTemplate).First();
        var seen = new HashSet<uint>();
        var queue = new Queue<uint>();
        if (template.U("gossip_menu_id") != 0)
        {
            queue.Enqueue(template.U("gossip_menu_id"));
        }

        foreach (var option in view.All(AcTables.GossipOption).Where(o => o.U("ActionMenuID") != 0))
        {
            queue.Enqueue(option.U("ActionMenuID"));
        }

        while (queue.Count > 0 && seen.Count < AcCreatureView.MaxMenus)
        {
            uint menu = queue.Dequeue();
            if (seen.Add(menu))
            {
                yield return menu;
            }
        }
    }

    /// <summary>
    /// Load rows of one table matching a parameterized predicate (identifiers come from the schema
    /// registry, never from input) into the snapshot, bounded by DbLimits.MaxRows.
    /// </summary>
    internal static async Task Load(WorldReadSession session, RowSet rows, TableSchema table, string predicate, object[] values, CancellationToken token)
    {
        // Text reads NULL as '' unless the column keeps NULL apart (a nullable override).
        string columns = string.Join(',', table.Columns.Select(c => c.Kind == ColumnKind.Text && !table.IsNullable(c.Name) ? $"COALESCE(`{c.Name}`,'')" : $"`{c.Name}`"));
        await using var command = session.Command($"SELECT {columns} FROM `{table.SqlName}` WHERE {predicate} LIMIT {DbLimits.MaxRows}");
        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue($"@k{i}", values[i]);
        }

        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var row = new Row();
            for (int i = 0; i < table.Columns.Length; i++)
            {
                object value = reader.GetValue(i);
                if (value is DBNull)
                {
                    // A nullable column keeps NULL; any other NULL number reads as 0, as the server loads it.
                    row.Values[table.Columns[i].Name] = table.IsNullable(table.Columns[i].Name) ? Row.NullValue : "0";
                    continue;
                }

                row.Values[table.Columns[i].Name] = table.Columns[i].Kind == ColumnKind.Float ? Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }

            rows.Set(table, row);
        }
    }

    internal static async Task<object?> Scalar(WorldReadSession session, string sql, object[] values, CancellationToken token)
    {
        await using var command = session.Command(sql);
        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue($"@k{i}", values[i]);
        }

        return await command.ExecuteScalarAsync(token).ConfigureAwait(false);
    }

    internal static string InList(MySqlCommand command, IEnumerable<uint> ids, string prefix) => WorldReadSession.InList(command, ids, prefix, DbLimits.MaxRows);

    async Task ReadLookups(WorldReadSession session, RowSet view, InteractionLookupData lookup, CancellationToken token)
    {
        // The base values are tiny (4 classes x ~83 levels), so read them all; the edited level or
        // class then resolves without another query.
        await using (var command = session.Command("SELECT level,class,basehp0,basehp1,basehp2,basemana,basearmor,attackpower,rangedattackpower,damage_base,damage_exp1,damage_exp2,Strength,Agility,Stamina,Intellect,Spirit FROM creature_classlevelstats LIMIT 2000"))
        {
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var s = new ClassLevelStats(
                    Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    [Convert.ToUInt32(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(3), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(4), CultureInfo.InvariantCulture)],
                    Convert.ToUInt32(reader.GetValue(5), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                    Convert.ToUInt32(reader.GetValue(7), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
                    [Convert.ToSingle(reader.GetValue(9), CultureInfo.InvariantCulture), Convert.ToSingle(reader.GetValue(10), CultureInfo.InvariantCulture), Convert.ToSingle(reader.GetValue(11), CultureInfo.InvariantCulture)],
                    Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(13), CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader.GetValue(14), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(15), CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader.GetValue(16), CultureInfo.InvariantCulture));
                lookup.ClassLevelStats[(s.Class, s.Level)] = s;
            }
        }

        // Reference vendor lists (negative items), one level deep like the view.
        var references = view.All(AcTables.Vendor).Where(r => r.I("item") < 0).Select(r => (uint)-r.I("item")).Distinct().ToArray();
        foreach (uint reference in references)
        {
            var referenced = new RowSet();
            await Load(session, referenced, AcTables.Vendor, "entry=@k0", [reference], token);
            lookup.ReferenceVendors[reference] = referenced.All(AcTables.Vendor).ToList();
        }

        var items = view.All(AcTables.Vendor).Where(r => r.I("item") > 0).Select(r => (uint)r.I("item"))
            .Concat(lookup.ReferenceVendors.Values.SelectMany(l => l).Where(r => r.I("item") > 0).Select(r => (uint)r.I("item")));
        await using (var command = session.Command(""))
        {
            command.CommandText = $"SELECT {ItemColumns} FROM item_template WHERE entry IN ({InList(command, items, "i")})";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var details = ReadItem(reader);
                lookup.Items[details.Info.Id] = details.Info;
                lookup.ItemDetails[details.Info.Id] = details;
            }
        }

        var quests = view.All(AcTables.QuestStarter).Concat(view.All(AcTables.QuestEnder)).Select(r => r.U("quest"));
        foreach (var quest in await ReadQuests(session, quests, token))
        {
            lookup.Quests[quest.Id] = quest;
        }

        var broadcasts = view.All(AcTables.NpcText).Select(r => r.I("BroadcastTextID0")).Concat(view.All(AcTables.GossipOption).Select(r => r.I("OptionBroadcastTextID"))).Where(id => id > 0).Select(id => (uint)id);
        await using (var command = session.Command(""))
        {
            command.CommandText = $"SELECT ID,COALESCE(MaleText,''),COALESCE(FemaleText,'') FROM broadcast_text WHERE ID IN ({InList(command, broadcasts, "b")})";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                string male = reader.GetString(1), female = reader.GetString(2);
                lookup.Broadcasts[reader.GetUInt32(0)] = (male.Length > 0 ? male : female, female.Length > 0 ? female : male);
            }
        }

        var pois = view.All(AcTables.GossipOption).Select(r => r.U("ActionPoiID")).Where(id => id != 0);
        await ReadIds(session, "SELECT ID FROM points_of_interest WHERE ID IN ({0})", pois, lookup.Pois, token);
        var menuRefs = view.All(AcTables.GossipOption).Select(r => r.U("ActionMenuID")).Concat(view.All(AcTables.GossipMenu).Select(r => r.U("MenuID"))).Where(id => id != 0).ToArray();
        await ReadIds(session, "SELECT DISTINCT MenuID FROM gossip_menu WHERE MenuID IN ({0})", menuRefs, lookup.Menus, token);
        var textRefs = view.All(AcTables.GossipMenu).Select(r => r.U("TextID")).ToArray();
        await ReadIds(session, "SELECT ID FROM npc_text WHERE ID IN ({0})", textRefs, lookup.Texts, token);
        var trainerRefs = view.All(AcTables.DefaultTrainer).Select(r => r.U("TrainerId")).ToArray();
        await ReadIds(session, "SELECT Id FROM trainer WHERE Id IN ({0})", trainerRefs, lookup.Trainers, token);
        await ReadCounts(session, "SELECT gossip_menu_id,COUNT(*) FROM creature_template WHERE gossip_menu_id IN ({0}) GROUP BY gossip_menu_id", menuRefs, lookup.MenuUserCounts, token);
        await ReadCounts(session, "SELECT TextID,COUNT(DISTINCT MenuID) FROM gossip_menu WHERE TextID IN ({0}) GROUP BY TextID", textRefs, lookup.TextUserCounts, token);
        await ReadCounts(session, "SELECT TrainerId,COUNT(*) FROM creature_default_trainer WHERE TrainerId IN ({0}) GROUP BY TrainerId", trainerRefs, lookup.TrainerUserCounts, token);
        if (session.Has("conditions.SourceTypeOrReferenceId"))
        {
            await ReadCounts(session, "SELECT SourceEntry,COUNT(*) FROM conditions WHERE SourceTypeOrReferenceId=19 AND SourceEntry IN ({0}) GROUP BY SourceEntry", quests.Distinct().ToArray(), lookup.QuestConditionCounts, token);
        }

        lookup.MaxMenuId = Convert.ToUInt32(await Scalar(session, "SELECT COALESCE(MAX(MenuID),0) FROM gossip_menu", [], token));
        lookup.MaxTextId = Convert.ToUInt32(await Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM npc_text", [], token));
        lookup.MaxTrainerId = Convert.ToUInt32(await Scalar(session, "SELECT COALESCE(MAX(Id),0) FROM trainer", [], token));
    }

    internal static async Task ReadIds(WorldReadSession session, string sql, IEnumerable<uint> ids, HashSet<uint> target, CancellationToken token)
    {
        await using var command = session.Command("");
        command.CommandText = string.Format(CultureInfo.InvariantCulture, sql, InList(command, ids, "x"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            target.Add(Convert.ToUInt32(reader.GetValue(0)));
        }
    }

    internal static async Task ReadCounts(WorldReadSession session, string sql, IEnumerable<uint> ids, Dictionary<uint, int> target, CancellationToken token)
    {
        await using var command = session.Command("");
        command.CommandText = string.Format(CultureInfo.InvariantCulture, sql, InList(command, ids, "c"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            target[Convert.ToUInt32(reader.GetValue(0))] = Convert.ToInt32(reader.GetValue(1));
        }
    }

    internal static async Task<List<QuestInfo>> ReadQuests(WorldReadSession session, IEnumerable<uint> ids, CancellationToken token)
    {
        await using var command = session.Command("");
        command.CommandText = $"""
            SELECT q.ID,COALESCE(q.LogTitle,''),q.QuestLevel,q.MinLevel,COALESCE(q.QuestDescription,''),COALESCE(q.LogDescription,''),q.RewardMoney,
                   q.RewardItem1,q.RewardAmount1,q.RewardItem2,q.RewardAmount2,q.RewardItem3,q.RewardAmount3,q.RewardItem4,q.RewardAmount4
            FROM quest_template q WHERE q.ID IN ({InList(command, ids, "q")})
            """;
        List<QuestInfo> quests = [];
        var rewardIds = new List<(uint Quest, uint Item, int Count)>();
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                uint id = reader.GetUInt32(0);
                for (int i = 0; i < 4; i++)
                {
                    uint item = Convert.ToUInt32(reader.GetValue(7 + i * 2));
                    if (item != 0)
                    {
                        rewardIds.Add((id, item, Convert.ToInt32(reader.GetValue(8 + i * 2))));
                    }
                }

                quests.Add(new QuestInfo(id, reader.GetString(1), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), reader.GetString(4), reader.GetString(5), Convert.ToInt32(reader.GetValue(6)), []));
            }
        }

        var names = new Dictionary<uint, string>();
        await using (var items = session.Command(""))
        {
            items.CommandText = $"SELECT entry,name FROM item_template WHERE entry IN ({InList(items, rewardIds.Select(r => r.Item), "r")})";
            await using var reader = await items.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                names[reader.GetUInt32(0)] = reader.GetString(1);
            }
        }

        return quests.Select(q => q with { RewardItems = rewardIds.Where(r => r.Quest == q.Id).Select(r => $"{r.Count}× {names.GetValueOrDefault(r.Item, $"item {r.Item}")}").ToArray() }).ToList();
    }

    static async Task<ScriptHook[]> ReadScripts(WorldReadSession session, uint entry, ulong guid, CancellationToken token)
    {
        await using var command = session.Command("""
            SELECT event_type,action_type,event_param1,event_param2,COALESCE(comment,'') FROM smart_scripts
            WHERE source_type=0 AND (entryorguid=@entry OR entryorguid=@guid) AND event_type IN (19,20,62,64)
            ORDER BY event_type,id LIMIT 100
            """);
        command.Parameters.AddWithValue("@entry", (int)entry);
        command.Parameters.AddWithValue("@guid", -(long)guid);
        List<ScriptHook> hooks = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            hooks.Add(new ScriptHook(Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), reader.GetString(4)));
        }

        return hooks.ToArray();
    }

    /// <summary>
    /// Count, never evaluate, conditions on this NPC's gossip menus (14), options (15), quests (19)
    /// and vendor items (23).
    /// </summary>
    static async Task<Dictionary<int, int>> ReadConditionCounts(WorldReadSession session, uint entry, RowSet view, CancellationToken token)
    {
        var counts = new Dictionary<int, int>();
        if (!session.Has("conditions.SourceTypeOrReferenceId"))
        {
            return counts;
        }

        await using var command = session.Command("");
        string menus = InList(command, view.All(AcTables.GossipMenu).Select(r => r.U("MenuID")), "m");
        string quests = InList(command, view.All(AcTables.QuestStarter).Select(r => r.U("quest")), "q");
        command.Parameters.AddWithValue("@entry", entry);
        command.CommandText = $"""
            SELECT SourceTypeOrReferenceId,COUNT(*) FROM conditions
            WHERE (SourceTypeOrReferenceId IN (14,15) AND SourceGroup IN ({menus}))
               OR (SourceTypeOrReferenceId=19 AND SourceEntry IN ({quests}))
               OR (SourceTypeOrReferenceId=23 AND SourceGroup=@entry)
            GROUP BY SourceTypeOrReferenceId
            """;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            counts[Convert.ToInt32(reader.GetValue(0))] = Convert.ToInt32(reader.GetValue(1));
        }

        return counts;
    }

    /// <summary>
    /// Existence checks for IDs typed in the editor (submenu, trainer) that are outside this NPC's
    /// loaded rows.
    /// </summary>
    public async Task<bool> MenuExistsAsync(uint menuId, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return Convert.ToInt64(await Scalar(session, "SELECT COUNT(*) FROM gossip_menu WHERE MenuID=@k0", [menuId], token)) > 0;
    }

    /// <summary>
    /// Bounded picker search over gossip menus by ID or by the (escaped LIKE) text of their npc_text; the name is
    /// that text.
    /// </summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchMenusAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("gossip_menu.MenuID", "gossip_menu.TextID", "npc_text.ID", "npc_text.text0_0", "npc_text.text0_1");
        await using var command = session.Command($"SELECT g.MenuID,MIN(COALESCE(NULLIF(n.text0_0,''),NULLIF(n.text0_1,''),'')) FROM gossip_menu g LEFT JOIN npc_text n ON n.ID=g.TextID WHERE n.text0_0 LIKE @s ESCAPE '=' OR n.text0_1 LIKE @s ESCAPE '=' OR g.MenuID=@id GROUP BY g.MenuID ORDER BY g.MenuID=@id DESC, g.MenuID LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string)> menus = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            menus.Add((Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture), reader.GetString(1)));
        }

        return menus;
    }

    public async Task<bool> PoiExistsAsync(uint poi, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return Convert.ToInt64(await Scalar(session, "SELECT COUNT(*) FROM points_of_interest WHERE ID=@k0", [poi], token)) > 0;
    }

    public async Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return Convert.ToInt64(await Scalar(session, "SELECT COUNT(*) FROM trainer WHERE Id=@k0", [trainerId], token)) > 0;
    }

    /// <summary>Columns of <see cref="ReadItem"/>, in order.</summary>
    static readonly string ItemColumns = "entry,name,Quality,BuyPrice,displayid,class,subclass,InventoryType,bonding,Flags,ItemLevel,RequiredLevel,maxcount,AllowableClass,AllowableRace,ContainerSlots,"
        + string.Join(",", Enumerable.Range(1, 10).Select(i => $"stat_type{i},stat_value{i}"))
        + ",dmg_min1,dmg_max1,dmg_type1,dmg_min2,dmg_max2,dmg_type2,delay,armor,block,holy_res,fire_res,nature_res,frost_res,shadow_res,arcane_res,MaxDurability,socketColor_1,socketColor_2,socketColor_3,COALESCE(description,''),SellPrice";

    static ItemDetails ReadItem(System.Data.Common.DbDataReader reader)
    {
        uint U(int i) => Convert.ToUInt32(reader.GetValue(i));
        int I(int i) => Convert.ToInt32(reader.GetValue(i));
        float F(int i) => Convert.ToSingle(reader.GetValue(i));
        var info = new ItemInfo(U(0), reader.GetString(1), I(2), Convert.ToInt64(reader.GetValue(3)), U(4));
        var stats = Enumerable.Range(0, 10).Select(i => (U(16 + i * 2), I(17 + i * 2))).ToArray();
        return new ItemDetails(info, U(5), U(6), U(7), U(8), U(9), U(10), U(11), U(12), I(13), I(14), U(15), stats,
            [(F(36), F(37), U(38)), (F(39), F(40), U(41))], U(42), U(43), U(44),
            Enumerable.Range(45, 6).Select(I).ToArray(), U(51), [U(52), U(53), U(54)], reader.GetString(55), Convert.ToInt64(reader.GetValue(56)));
    }

    /// <summary>
    /// Bounded picker searches (escaped LIKE, parameterized).
    /// </summary>
    /// <summary>Items whose name contains the term, or whose entry it is, with the template fields a tooltip shows.</summary>
    public async Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command($"SELECT {ItemColumns} FROM item_template WHERE name LIKE @s ESCAPE '=' OR entry=@id ORDER BY entry=@id DESC, CHAR_LENGTH(name), entry LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<ItemDetails> items = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    public async Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return await SearchQuests(session, term, token);
    }

    internal static async Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuests(WorldReadSession session, string term, CancellationToken token)
    {
        await using var command = session.Command($"SELECT ID,COALESCE(LogTitle,''),QuestLevel FROM quest_template WHERE LogTitle LIKE @s ESCAPE '=' OR ID=@id ORDER BY ID=@id DESC, CHAR_LENGTH(LogTitle), ID LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string, int)> quests = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            quests.Add((reader.GetUInt32(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2))));
        }

        return quests;
    }
}
