namespace ACWorldEdit.Foundation;

public enum SpawnKind
{
    Creature,
    GameObject
}

/// <summary>A table that names a spawn by its GUID, listed so a delete can show what it leaves behind.</summary>
public sealed record SpawnReference(string Table, string Column, string Meaning, string? Where = null);

/// <summary>
/// Row edits for placing, duplicating, moving and deleting creature and GameObject spawns, as
/// ordinary <see cref="RowEdit"/>s of the `creature` / `gameobject` tables so a spawn shares one undo
/// history and one export with every other edit. New rows start from what the server writes itself
/// (<c>Creature::SaveToDB</c> reached by `.npc add`, <c>GameObject::SaveToDB</c> by `.gobject add`),
/// not from invented values.
/// </summary>
public static class SpawnEdits
{
    /// <summary>Seconds a freshly added spawn waits to respawn: the constructor default of the server's Creature and GameObject.</summary>
    public const uint DefaultRespawnSeconds = 300;

    /// <summary>The GameObject animation progress `.gobject add` passes (fully open/visible).</summary>
    public const uint DefaultAnimProgress = 255;

    /// <summary>GO_STATE_READY: a door or chest in its usual state.</summary>
    public const uint StateReady = 1;

    const uint IdleMovement = 0;
    const uint RandomMovement = 1;

    public static TableSchema SpawnTable(IWorldSchema schema, SpawnKind kind) => schema.Tables.Find(kind == SpawnKind.Creature ? "creature" : "gameobject");

    public static TableSchema AddonTable(IWorldSchema schema, SpawnKind kind) => schema.Tables.Find(kind == SpawnKind.Creature ? "creature_addon" : "gameobject_addon");

    public static WorldPose PoseOf(Row spawn) => new((ushort)spawn.U("map"), spawn.F("position_x"), spawn.F("position_y"), spawn.F("position_z"), spawn.F("orientation"));

    public static Row WithPose(Row spawn, WorldPose pose) => spawn
        .With("map", (uint)pose.Map)
        .With("position_x", (float)pose.X)
        .With("position_y", (float)pose.Y)
        .With("position_z", (float)pose.Z)
        .With("orientation", (float)pose.Heading);

    /// <summary>The edit that moves a spawn. A spawn stays on its map, as every pose draft before it did.</summary>
    public static RowEdit Move(IWorldSchema schema, SpawnKind kind, Row before, WorldPose after)
    {
        after.Validate();
        if (PoseOf(before).Map != after.Map)
        {
            throw new InvalidDataException("A spawn cannot move to another map.");
        }

        return InteractionEdits.Update(SpawnTable(schema, kind), before, WithPose(before, after));
    }

    /// <summary>
    /// A new creature spawn as `.npc add` writes it: the template's flags are inherited, the model is
    /// the template's, respawn is 300 s and the creature does not wander. <paramref name="equipmentId"/>
    /// has no blind default: the server uses equipment set 1 only when the template has one, and 0
    /// means "no equipment", so the caller reads the template. A Random movement type without a wander
    /// radius becomes Idle, as the server normalizes it. Visibility defaults to the open world.
    /// </summary>
    public static Row NewCreature(IWorldSchema schema, ulong guid, uint entry, WorldPose pose, int equipmentId, uint templateMovementType, string spawnDifficulties = "0", uint spawnMask = 1)
    {
        pose.Validate();
        uint movement = templateMovementType == RandomMovement ? IdleMovement : templateMovementType;
        var table = SpawnTable(schema, SpawnKind.Creature);
        var row = Defaults(table);
        row = WithPose(row, pose)
            .With("guid", guid).With("id", entry)
            .With("equipment_id", equipmentId)
            .With("spawntimesecs", DefaultRespawnSeconds)
            .With("MovementType", movement);
        return schema.Dialect == WorldDialect.TrinityCore
            // Full health, and NULL flags: the spawn equals its template until an override is set.
            ? row.With("spawnDifficulties", spawnDifficulties).With("terrainSwapMap", -1).With("curHealthPct", 100u).With("npcflag", null).With("unit_flags", null).With("unit_flags2", null).With("unit_flags3", null).With("PhaseId", 0).With("PhaseGroup", 0).With("StringId", null)
            // curhealth only matters for creatures that do not regenerate; 1 is the column default.
            : row.With("spawnMask", spawnMask).With("phaseMask", 1u).With("curhealth", 1u);
    }

    /// <summary>
    /// A new GameObject spawn as `.gobject add` writes it: facing turns the object about the vertical
    /// axis (the stored quaternion), animation progress 255, state ready, respawn 300 s.
    /// </summary>
    public static Row NewGameObject(IWorldSchema schema, ulong guid, uint entry, WorldPose pose, string spawnDifficulties = "0", uint spawnMask = 1)
    {
        pose.Validate();
        var table = SpawnTable(schema, SpawnKind.GameObject);
        var row = WithPose(Defaults(table), pose)
            .With("guid", guid).With("id", entry)
            .With("rotation2", (float)Math.Sin(pose.Heading / 2)).With("rotation3", (float)Math.Cos(pose.Heading / 2))
            .With("spawntimesecs", (int)DefaultRespawnSeconds)
            .With("animprogress", DefaultAnimProgress).With("state", StateReady);
        return schema.Dialect == WorldDialect.TrinityCore
            ? row.With("spawnDifficulties", spawnDifficulties).With("terrainSwapMap", -1).With("PhaseId", 0).With("PhaseGroup", 0).With("StringId", null)
            : row.With("spawnMask", spawnMask).With("phaseMask", 1u);
    }

    /// <summary>
    /// Place a new spawn, with its addon row when it has one to copy. A copy keeps the source's own
    /// equipment, flags, phasing and addon (a duplicate must not lose "no equipment" or an override).
    /// </summary>
    public static RowEdit[] Place(IWorldSchema schema, SpawnKind kind, Row spawn, Row? addon = null)
    {
        var edits = new List<RowEdit> { InteractionEdits.Insert(SpawnTable(schema, kind), spawn) };
        if (addon != null)
        {
            edits.Add(InteractionEdits.Insert(AddonTable(schema, kind), addon.With("guid", spawn["guid"])));
        }

        return edits.ToArray();
    }

    /// <summary>A copy of <paramref name="source"/> at <paramref name="pose"/> under a new GUID, with its addon copied along.</summary>
    public static RowEdit[] Duplicate(IWorldSchema schema, SpawnKind kind, RowSet effective, Row source, ulong newGuid, WorldPose pose)
    {
        if (PoseOf(source).Map != pose.Map)
        {
            throw new InvalidDataException("A copy stays on the map of its source.");
        }

        var addon = effective.Get(AddonTable(schema, kind), source["guid"]);
        var copy = WithPose(source, pose).With("guid", newGuid);
        return Place(schema, kind, copy, addon);
    }

    /// <summary>
    /// Delete a spawn and its addon row in one command. Other tables that name the GUID
    /// (<see cref="References"/>) are listed to the user, not removed here.
    /// </summary>
    public static RowEdit[] Delete(IWorldSchema schema, SpawnKind kind, RowSet effective, Row spawn)
    {
        var edits = new List<RowEdit> { InteractionEdits.Delete(SpawnTable(schema, kind), spawn) };
        if (effective.Get(AddonTable(schema, kind), spawn["guid"]) is { } addon)
        {
            edits.Insert(0, InteractionEdits.Delete(AddonTable(schema, kind), addon));
        }

        return edits.ToArray();
    }

    /// <summary>
    /// Tables outside the spawn editors that name a spawn by GUID, so a delete can say what it would
    /// orphan. The server's own delete (<c>Creature::DeleteFromDB</c>) removes only some of them.
    /// </summary>
    public static IReadOnlyList<SpawnReference> References(WorldDialect dialect, SpawnKind kind)
    {
        bool creature = kind == SpawnKind.Creature;
        string smartSource = creature ? "0" : "1";
        var shared = new List<SpawnReference>
        {
            new("smart_scripts", "entryorguid", "SmartAI rules written for this one spawn (a negative GUID)", $"source_type = {smartSource} AND entryorguid < 0"),
            new("linked_respawn", "guid", "respawn linked to a boss or leader"),
            new("linked_respawn", "linkedGuid", "spawns whose respawn follows this one")
        };
        if (dialect == WorldDialect.TrinityCore)
        {
            shared.Add(new("spawn_group", "spawnId", "spawn group membership", $"spawnType = {(creature ? 0 : 1)}"));
            shared.Add(new("pool_members", "spawnId", "pool membership", $"type = {(creature ? 0 : 1)}"));
        }
        else
        {
            shared.Add(new(creature ? "pool_creature" : "pool_gameobject", "guid", "pool membership"));
        }

        shared.Add(new(creature ? "game_event_creature" : "game_event_gameobject", "guid", "game event spawn"));
        if (creature)
        {
            shared.Add(new("game_event_model_equip", "guid", "model and equipment swaps by event"));
            shared.Add(new("creature_formations", "leaderGUID", "formation this creature leads"));
            shared.Add(new("creature_formations", "memberGUID", "formation this creature belongs to"));
        }

        return shared;
    }

    /// <summary>A row of the table with every column zero or empty; callers set the columns that matter.</summary>
    static Row Defaults(TableSchema table) => Row.Of(table.Columns.Select(c => (c.Name, c.Kind == ColumnKind.Text ? "" : c.Kind == ColumnKind.Float ? (object)0f : 0)).ToArray());
}
