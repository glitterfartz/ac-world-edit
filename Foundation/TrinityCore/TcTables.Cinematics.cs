namespace ACWorldEdit.Foundation;

// The cinematics tables of TcTables. TrinityCore master only: AzerothCore has no conversations,
// scenes or player choices. Column order, types and NULLs follow TDB 1210.26091.
public sealed partial class TcTables
{
    /// <summary>
    /// playerchoice: the question card and the flags that shape it. Duration is milliseconds (NULL
    /// leaves the timing to the client); Question, MaxResponses and ScriptName are NULL-able, so the
    /// editors can tell "not set" from an empty or zero value.
    /// </summary>
    public static readonly TableSchema PlayerChoice = new("playerchoice", ["ChoiceId"],
    [
        ("ChoiceId", ColumnKind.Int, 0),
        ("UiTextureKitId", ColumnKind.Int, 0),
        ("SoundKitId", ColumnKind.UInt, 0),
        ("CloseSoundKitId", ColumnKind.UInt, 0),
        ("Duration", ColumnKind.Long, 0),
        ("PendingChoiceText", ColumnKind.Text, 255),
        ("Question", ColumnKind.Text, 255),
        ("HideWarboardHeader", ColumnKind.UInt, 0),
        ("KeepOpenAfterChoice", ColumnKind.UInt, 0),
        ("ShowChoicesAsList", ColumnKind.UInt, 0),
        ("RequiresSelection", ColumnKind.UInt, 0),
        ("ShowChoicesAsGrid", ColumnKind.UInt, 0),
        ("HideAnswerArt", ColumnKind.UInt, 0),
        ("ShowChoicesAsColumns", ColumnKind.UInt, 0),
        ("MaxResponses", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("InfiniteRange", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.Int, 0)
    ], Nullable: ["Duration", "Question", "MaxResponses", "ScriptName"]);

    /// <summary>
    /// playerchoice_response: one answer card. Header, Answer, Description and Confirmation have no
    /// database default, so a new row writes the empty string explicitly; RewardQuestID is NULL when
    /// the answer does not send the player to a quest.
    /// </summary>
    public static readonly TableSchema PlayerChoiceResponse = new("playerchoice_response", ["ChoiceId", "ResponseId"],
    [
        ("ChoiceId", ColumnKind.Int, 0),
        ("ResponseId", ColumnKind.Int, 0),
        ("Index", ColumnKind.UInt, 0),
        ("ChoiceArtFileId", ColumnKind.Int, 0),
        ("Flags", ColumnKind.Int, 0),
        ("WidgetSetID", ColumnKind.UInt, 0),
        ("UiTextureAtlasElementID", ColumnKind.UInt, 0),
        ("SoundKitID", ColumnKind.UInt, 0),
        ("GroupID", ColumnKind.UInt, 0),
        ("UiTextureKitID", ColumnKind.Int, 0),
        ("Header", ColumnKind.Text, 511),
        ("SubHeader", ColumnKind.Text, 100),
        ("ButtonTooltip", ColumnKind.Text, 400),
        ("Answer", ColumnKind.Text, 511),
        ("Description", ColumnKind.Text, 2047),
        ("Confirmation", ColumnKind.Text, 127),
        ("RewardQuestID", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.Int, 0)
    ], Nullable: ["RewardQuestID"]);

    /// <summary>
    /// playerchoice_response_reward: the scalar reward of one response. Its key is (ChoiceId,
    /// ResponseId) with no Index, exactly as TrinityCore publishes it, so a response has at most one
    /// reward row even though the server's loader orders by Index.
    /// </summary>
    public static readonly TableSchema PlayerChoiceResponseReward = new("playerchoice_response_reward", ["ChoiceId", "ResponseId"],
    [
        ("ChoiceId", ColumnKind.Int, 0),
        ("ResponseId", ColumnKind.Int, 0),
        ("TitleId", ColumnKind.Int, 0),
        ("PackageId", ColumnKind.Int, 0),
        ("SkillLineId", ColumnKind.UInt, 0),
        ("SkillPointCount", ColumnKind.UInt, 0),
        ("ArenaPointCount", ColumnKind.UInt, 0),
        ("HonorPointCount", ColumnKind.UInt, 0),
        ("Money", ColumnKind.ULong, 0),
        ("Xp", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.Int, 0)
    ]);

    /// <summary>
    /// playerchoice_response_reward_item: an item granted by one response's reward row. Index orders
    /// the items; BonusListIDs is a NULL-able comma-separated list (mediumtext, so unbounded here).
    /// </summary>
    public static readonly TableSchema PlayerChoiceResponseRewardItem = new("playerchoice_response_reward_item", ["ChoiceId", "ResponseId"],
    [
        ("ChoiceId", ColumnKind.Int, 0),
        ("ResponseId", ColumnKind.Int, 0),
        ("Index", ColumnKind.UInt, 0),
        ("ItemId", ColumnKind.UInt, 0),
        ("BonusListIDs", ColumnKind.Text, 0),
        ("Quantity", ColumnKind.Int, 0),
        ("VerifiedBuild", ColumnKind.Int, 0)
    ], Nullable: ["BonusListIDs"]);

    /// <summary>
    /// conversation_template: which client line chain to play (FirstLineId), its texture kit and the
    /// server flags. The spoken words and timing live in the client ConversationLine table.
    /// </summary>
    public static readonly TableSchema ConversationTemplate = new("conversation_template", ["Id"],
    [
        ("Id", ColumnKind.UInt, 0),
        ("FirstLineId", ColumnKind.UInt, 0),
        ("TextureKitId", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.Int, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("VerifiedBuild", ColumnKind.UInt, 0)
    ]);

    /// <summary>
    /// conversation_actors: who stands on screen at each index. NoActorObject and ActivePlayerObject
    /// are NULL-able; a non-zero value names a talking head or the player instead of a creature or spawn.
    /// </summary>
    public static readonly TableSchema ConversationActors = new("conversation_actors", ["ConversationId", "Idx"],
    [
        ("ConversationId", ColumnKind.UInt, 0),
        ("ConversationActorId", ColumnKind.UInt, 0),
        ("ConversationActorGuid", ColumnKind.ULong, 0),
        ("Idx", ColumnKind.UInt, 0),
        ("CreatureId", ColumnKind.UInt, 0),
        ("CreatureDisplayInfoId", ColumnKind.UInt, 0),
        ("NoActorObject", ColumnKind.UInt, 0),
        ("ActivePlayerObject", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.UInt, 0)
    ], Nullable: ["NoActorObject", "ActivePlayerObject"]);

    /// <summary>
    /// conversation_line_template: the per-line server overrides of one client line. The key is the
    /// client line Id, not the conversation, so several conversations that share a chain share them.
    /// </summary>
    public static readonly TableSchema ConversationLineTemplate = new("conversation_line_template", ["Id"],
    [
        ("Id", ColumnKind.UInt, 0),
        ("UiCameraID", ColumnKind.UInt, 0),
        ("ActorIdx", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.UInt, 0),
        ("ChatType", ColumnKind.UInt, 0),
        ("VerifiedBuild", ColumnKind.UInt, 0)
    ]);
}
