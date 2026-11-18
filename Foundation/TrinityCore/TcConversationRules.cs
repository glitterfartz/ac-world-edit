namespace ACWorldEdit.Foundation;

/// <summary>
/// TrinityCore conversation rules: the three tables, complete rows for a new conversation, actor or
/// line override, how the server derives an actor's kind from its four columns, and the loader's
/// checks. The client line chain itself is supplied by the caller, which can read the client.
/// </summary>
public static class TcConversationRules
{
    /// <summary>What an actor is, derived from the columns the server reads.</summary>
    public enum ActorKind
    {
        /// <summary>A creature template stands on screen (the usual actor).</summary>
        Creature,
        /// <summary>An existing creature spawn stands on screen.</summary>
        Spawn,
        /// <summary>A talking head: a creature id and display, with no world object.</summary>
        NoObject,
        /// <summary>The player is the actor.</summary>
        ActivePlayer
    }

    public static readonly TableSchema[] Tables = [TcTables.ConversationTemplate, TcTables.ConversationActors, TcTables.ConversationLineTemplate];

    /// <summary>A conversation playing the client chain that starts at <paramref name="firstLineId"/>.</summary>
    public static Row NewConversation(uint id, uint firstLineId) =>
        Defaults(TcTables.ConversationTemplate)
            .With("Id", id)
            .With("FirstLineId", firstLineId);

    /// <summary>A new actor at <paramref name="idx"/>, a creature with no picks yet.</summary>
    public static Row NewActor(uint conversationId, uint idx) =>
        Defaults(TcTables.ConversationActors)
            .With("ConversationId", conversationId)
            .With("Idx", idx);

    /// <summary>An override row for one client line, using the client's camera, actor and flags.</summary>
    public static Row NewLineOverride(uint lineId) =>
        Defaults(TcTables.ConversationLineTemplate)
            .With("Id", lineId);

    /// <summary>The next free actor index (0 for a conversation with none, otherwise highest + 1, so a removed index is not reused).</summary>
    public static uint NextActorIdx(RowSet rows, uint conversation)
    {
        var indices = rows.All(TcTables.ConversationActors).Where(a => a.U("ConversationId") == conversation).Select(a => a.U("Idx")).ToList();
        return indices.Count == 0 ? 0u : indices.Max() + 1;
    }

    /// <summary>
    /// An actor and every override that names its index: a line override cannot point at an actor
    /// the conversation no longer has, so they leave together.
    /// </summary>
    public static RowEdit[] RemoveActor(Row actor, RowSet rows)
    {
        uint idx = actor.U("Idx");
        return
        [
            InteractionEdits.Delete(TcTables.ConversationActors, actor),
            .. rows.All(TcTables.ConversationLineTemplate)
                .Where(line => line.U("ActorIdx") == idx)
                .Select(line => InteractionEdits.Delete(TcTables.ConversationLineTemplate, line))
        ];
    }

    /// <summary>Actor and override rows of one conversation, actors first in index order.</summary>
    public static IEnumerable<RowEdit> RemoveConversation(Row conversation, uint id, RowSet rows)
    {
        yield return InteractionEdits.Delete(TcTables.ConversationTemplate, conversation);
        foreach (var actor in rows.All(TcTables.ConversationActors).Where(a => a.U("ConversationId") == id))
        {
            yield return InteractionEdits.Delete(TcTables.ConversationActors, actor);
        }
    }

    /// <summary>
    /// The actor kind the server derives: a talking head, then the player, then a spawn by GUID,
    /// otherwise a creature template.
    /// </summary>
    public static ActorKind Kind(Row actor) =>
        actor.U("NoActorObject") != 0 ? ActorKind.NoObject
        : actor.U("ActivePlayerObject") != 0 ? ActorKind.ActivePlayer
        : actor.UL("ConversationActorGuid") != 0 ? ActorKind.Spawn
        : ActorKind.Creature;

    /// <summary>
    /// Loader checks for one conversation's effective rows. <paramref name="chainLines"/> is the
    /// client chain's line IDs when the client is available; null skips the chain checks.
    /// <paramref name="names"/> resolves creatures and spawns; null skips those lookups.
    /// </summary>
    public static IEnumerable<InteractionIssue> Check(uint conversation, RowSet rows, IContentNames? names, IReadOnlyCollection<uint>? chainLines = null)
    {
        if (rows.Get(TcTables.ConversationTemplate, conversation.ToString()) is not { } template)
        {
            yield break;
        }

        if (template.U("FirstLineId") == 0)
        {
            yield return new(true, "The conversation has no first line, so nothing plays.");
        }

        var actors = rows.All(TcTables.ConversationActors)
            .Where(a => a.U("ConversationId") == conversation)
            .OrderBy(a => a.U("Idx"))
            .ToList();
        if (actors.Count == 0)
        {
            yield return new(false, "The conversation has no actors; its lines play without a cast.");
        }

        // The primary key is (ConversationId, Idx), so the snapshot cannot hold two actors at one
        // index; the database and the draft reject that before it reaches a check.
        var indices = actors.Select(a => a.U("Idx")).ToHashSet();
        foreach (var actor in actors)
        {
            uint idx = actor.U("Idx");
            if (actor.U("NoActorObject") != 0 && actor.U("ActivePlayerObject") != 0)
            {
                yield return new(true, $"Actor {idx} cannot be both a talking head and the player.");
            }

            switch (Kind(actor))
            {
                case ActorKind.Spawn when names != null && !names.CreatureSpawnExists(actor.UL("ConversationActorGuid")):
                    yield return new(true, $"Actor {idx}: spawn {actor.UL("ConversationActorGuid")} does not exist.");
                    break;
                case ActorKind.NoObject when actor.U("CreatureId") == 0:
                    yield return new(false, $"Actor {idx}: no creature chosen yet; a talking head needs one.");
                    break;
                case ActorKind.NoObject when actor.U("CreatureDisplayInfoId") == 0:
                    yield return new(false, $"Actor {idx}: no display chosen; the creature template's model is used.");
                    break;
                case ActorKind.Creature when actor.U("CreatureId") == 0:
                    yield return new(false, $"Actor {idx}: no creature chosen yet.");
                    break;
                case ActorKind.Creature when names?.Creature(actor.U("CreatureId")) == null:
                    yield return new(true, $"Actor {idx}: creature {actor.U("CreatureId")} does not exist.");
                    break;
            }
        }

        foreach (var line in rows.All(TcTables.ConversationLineTemplate))
        {
            if (chainLines != null && !chainLines.Contains(line.U("Id")))
            {
                yield return new(false, $"Override for line {line.U("Id")} is outside this conversation's chain; the server ignores it.");
            }

            if (actors.Count > 0 && !indices.Contains(line.U("ActorIdx")))
            {
                yield return new(true, $"Line {line.U("Id")} names actor {line.U("ActorIdx")}, but the conversation has no such actor.");
            }
        }
    }

    static Row Defaults(TableSchema table) =>
        new(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Kind == ColumnKind.Text ? "" : "0")));
}
