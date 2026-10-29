using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --spawn-authoring-smoke (windowed or headless) on an isolated content draft: the creature
/// authoring workflow in Goldshire. A spawn placed in the draft is drawn, listed and selected with
/// the template's name, exports as an INSERT and takes a fresh GUID after an undo; undo removes it
/// and redo brings it back. Kept apart from --interaction-smoke so that one stays focused on the
/// editors.
/// </summary>
public partial class TerrainViewer
{
    bool spawnAuthoringSmoke;
    readonly SmokeSteps spawnAuthoringRun = new("SPAWN_AUTHORING", 60, 240, 3);
    List<(string Name, Func<bool> Step)>? spawnAuthoringSteps;
    CreatureSpawn? authoringTemplate;
    Task<ulong?>? authoringPlace;
    GameObjectSpawn? authoringObject;
    Task<ulong?>? authoringObjectPlace;
    ulong authoringObjectGuid;
    ulong authoringFirst;
    WorldPose authoringPose;

    void SpawnAuthoringSmoke(double delta)
    {
        shotPrefix = "spawn-authoring";
        spawnAuthoringSteps ??= SpawnAuthoringSteps();
        TakeScreenshot();
        if (shot != null)
        {
            return;
        }

        string Where() => $"map={currentMapId} records={creatures.Records.Count} selected={creatures.SelectedGuid} net={drafts.Draft?.NetEdits().Count} {creatures.QueryStatus}";
        spawnAuthoringRun.Advance(delta, spawnAuthoringSteps, !busy && world != null, Where, SmokeFail, () => GetTree().Quit(), waiting: Where);
    }

    string AuthoringScript() => AuthoringScript(creatures.SmokeSourceKey);

    string AuthoringScript(string key)
    {
        return SqlExport.Build(key, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, ContentDraftFiles.Sources.SchemaFor(key)).Apply;
    }

    /// <summary>Place the template at the pose and keep the task so a step can wait for it.</summary>
    bool StartAuthoringPlace()
    {
        authoringPlace = creatures.PlaceAsync(authoringTemplate!.TemplateId, authoringPose);
        return true;
    }

    List<(string, Func<bool>)> SpawnAuthoringSteps() =>
    [
        ("goldshire", () =>
        {
            if (currentMapId != 0)
            {
                return false;
            }

            tileX.Value = 31;
            tileY.Value = 49;
            Teleport(new TileKey(31, 49));
            FocusPose(new WorldPose(0, -9460, 30, 58, 0));
            return true;
        }),
        ("creatures-loaded", () =>
        {
            if (loaded.Count != wanted.Count || creatures.IsLoading || creatures.Records.Count == 0)
            {
                return false;
            }

            authoringTemplate = creatures.Records[0];
            authoringPose = authoringTemplate.Pose with { X = authoringTemplate.Pose.X + 3 };
            Require(drafts.Draft?.NetEdits().Count == 0, "the smoke draft should start empty");
            return true;
        }),
        ("place", () =>
        {
            if (authoringPlace == null)
            {
                return StartAuthoringPlace() && false;
            }

            if (!authoringPlace.IsCompleted)
            {
                return false;
            }

            Require(authoringPlace.Result is ulong, "placing the creature returned no GUID");
            authoringFirst = authoringPlace.Result!.Value;
            authoringPlace = null;
            return true;
        }),
        ("drawn-and-selected", () =>
        {
            if (!creatures.SmokeDrawn(authoringFirst) || creatures.SelectedGuid != authoringFirst)
            {
                return false;
            }

            var record = creatures.SmokeRecord(authoringFirst)!;
            Require(record.TemplateId == authoringTemplate!.TemplateId && record.Name == authoringTemplate.Name, $"drawn as '{record.Name}' ({record.TemplateId}) instead of the template");
            Require(record.DisplayId == authoringTemplate.DisplayId, "drawn with another model than the template's");
            Require(creatures.SmokeListed(authoringFirst), "the placed spawn is not in the creature list");
            Require(creatures.PositionOf(authoringFirst).DistanceTo(creatures.SmokeViewPosition(authoringPose)) < 0.01f, "the marker is not at the placed pose");
            return true;
        }),
        ("exports-an-insert", () =>
        {
            Require(drafts.Draft!.NetEdits().Count == 1, "placing should be one net change");
            string apply = AuthoringScript();
            Require(apply.Contains("INSERT INTO `creature`", StringComparison.Ordinal), "the export lacks the creature INSERT");
            Require(apply.Contains(authoringFirst.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), "the export lacks the new GUID");
            return true;
        }),
        ("undo-removes-it", () =>
        {
            creatures.SmokeHistory(false);
            Require(!creatures.SmokeDrawn(authoringFirst) && creatures.SmokeRecord(authoringFirst) == null, "undo left the spawn drawn");
            Require(creatures.SelectedGuid != authoringFirst, "undo left the removed spawn selected");
            Require(!creatures.SmokeListed(authoringFirst), "undo left the spawn in the list");
            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            return true;
        }),
        ("redo-restores-it", () =>
        {
            creatures.SmokeHistory(true);
            if (!creatures.SmokeDrawn(authoringFirst))
            {
                return false;
            }

            Require(creatures.SmokeRecord(authoringFirst)?.Name == authoringTemplate!.Name, "redo restored the wrong spawn");
            creatures.SmokeHistory(false);
            return true;
        }),
        ("new-guid-after-undo", () =>
        {
            if (authoringPlace == null)
            {
                return StartAuthoringPlace() && false;
            }

            if (!authoringPlace.IsCompleted)
            {
                return false;
            }

            ulong second = authoringPlace.Result ?? throw new InvalidOperationException("the second placement returned no GUID");
            Require(second != authoringFirst, "a new spawn reused the GUID of an undone one, so a redo would collide");
            creatures.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0, "the draft should be empty again");
            return true;
        }),
        ("gameobjects-loaded", () =>
        {
            if (gameObjects.IsLoading || gameObjects.Records.Count == 0)
            {
                return false;
            }

            authoringObject = gameObjects.Records[0];
            Require(drafts.Draft!.NetEdits().Count == 0, "the draft should be empty before placing a GameObject");
            return true;
        }),
        ("place-gameobject", () =>
        {
            if (authoringObjectPlace == null)
            {
                authoringObjectPlace = gameObjects.PlaceAsync(authoringObject!.TemplateId, authoringObject.Pose with { X = authoringObject.Pose.X + 3 });
                return false;
            }

            if (!authoringObjectPlace.IsCompleted)
            {
                return false;
            }

            authoringObjectGuid = authoringObjectPlace.Result ?? throw new InvalidOperationException("placing the GameObject returned no GUID");
            return true;
        }),
        ("gameobject-drawn-and-selected", () =>
        {
            var record = gameObjects.SmokeRecord(authoringObjectGuid);
            Require(record != null, "the placed GameObject is not among the records");
            Require(gameObjects.SmokeSelected == authoringObjectGuid, "the placed GameObject is not selected");
            Require(record!.TemplateId == authoringObject!.TemplateId && record.Name == authoringObject.Name && record.DisplayId == authoringObject.DisplayId, "drawn as another template");
            Require(gameObjects.SmokeListed(authoringObjectGuid), "the placed GameObject is not in the list");
            Require(GameObjectOverlay.ViewPosition(record).DistanceTo(ViewSpace.ToView(authoringObject.Pose.X + 3, authoringObject.Pose.Y, authoringObject.Pose.Z)) < 0.01f, "the GameObject is not at the placed pose");
            string apply = AuthoringScript(gameObjects.SmokeSourceKey);
            Require(apply.Contains("INSERT INTO `gameobject`", StringComparison.Ordinal), "the export lacks the gameobject INSERT");
            return true;
        }),
        ("gameobject-undo-and-redo", () =>
        {
            gameObjects.SmokeHistory(false);
            Require(gameObjects.SmokeRecord(authoringObjectGuid) == null && !gameObjects.SmokeListed(authoringObjectGuid), "undo left the GameObject drawn");
            Require(gameObjects.SmokeSelected != authoringObjectGuid, "undo left the removed GameObject selected");
            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            gameObjects.SmokeHistory(true);
            Require(gameObjects.SmokeRecord(authoringObjectGuid)?.Name == authoringObject!.Name, "redo did not bring the GameObject back");
            gameObjects.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0, "the draft should be empty again");
            return true;
        })
    ];
}
