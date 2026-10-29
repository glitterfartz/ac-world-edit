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
    readonly SmokeSteps spawnAuthoringRun = new("SPAWN_AUTHORING", 60, 420, 3);
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

        string Where() => $"map={currentMapId} records={creatures.Records.Count} selected={creatures.SelectedGuid} net={drafts.Draft?.NetEdits().Count} objectsSelected={gameObjects.SmokeSelected} objectGroup={gameObjects.Group.Count} creatureGroup={creatures.Group.Count} {creatures.QueryStatus}";
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
        }),
        ("copy-select-creature", () =>
        {
            creatures.Select(authoringTemplate!.Guid);
            if (creatures.SelectedGuid != authoringTemplate.Guid || !creatures.CanDuplicate)
            {
                return false;
            }

            Require(creatures.SmokeCopyButtons == (true, true), "the Duplicate and Delete buttons should be enabled for a loaded creature");
            authoringCount = creatures.Records.Count;
            return true;
        }),
        ("copy-creature-ctrl-d", () =>
        {
            SmokeKey(Key.D, ctrl: true);
            return true;
        }),
        ("copy-creature-drawn", () =>
        {
            if (creatures.Records.Count != authoringCount + 1 || creatures.SelectedGuid == authoringTemplate!.Guid || creatures.SelectedGuid is not ulong copy)
            {
                return false;
            }

            authoringCopy = copy;
            var record = creatures.SmokeRecord(copy)!;
            Require(record.TemplateId == authoringTemplate.TemplateId && record.Name == authoringTemplate.Name, "the copy is another creature");
            double distance = Math.Sqrt(Math.Pow(record.Pose.X - authoringTemplate.Pose.X, 2) + Math.Pow(record.Pose.Y - authoringTemplate.Pose.Y, 2));
            Require(Math.Abs(distance - 2) < 0.1, $"the copy is {distance:F2} yd from its source, expected 2");
            Require(creatures.SmokeListed(copy), "the copy is not in the list");
            string apply = AuthoringScript(creatures.SmokeSourceKey);
            Require(apply.Contains("INSERT INTO `creature`", StringComparison.Ordinal) && apply.Contains(copy.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), "the export lacks the copy's INSERT");
            return true;
        }),
        ("delete-copy-with-the-key", () =>
        {
            SmokeKey(Key.Delete);
            return true;
        }),
        ("copy-gone-and-nothing-to-export", () =>
        {
            if (creatures.SmokeRecord(authoringCopy) != null)
            {
                return false;
            }

            Require(!creatures.SmokeDrawn(authoringCopy) && !creatures.SmokeListed(authoringCopy), "the deleted copy is still drawn or listed");
            Require(creatures.SelectedGuid == null, "a deleted spawn must not stay selected");
            Require(drafts.Draft!.NetEdits().Count == 0, "placing then deleting a copy should leave nothing to export");
            return true;
        }),
        ("delete-source-with-the-button", () =>
        {
            creatures.Select(authoringTemplate!.Guid);
            if (!creatures.CanDelete)
            {
                return false;
            }

            creatures.SmokePressDelete();
            return true;
        }),
        ("source-gone-and-exports-a-delete", () =>
        {
            creatures.SmokeAnswerDelete(true);
            if (creatures.SmokeRecord(authoringTemplate!.Guid) != null)
            {
                return false;
            }

            Require(!creatures.SmokeDrawn(authoringTemplate.Guid), "the deleted creature is still drawn");
            string apply = AuthoringScript(creatures.SmokeSourceKey);
            Require(apply.Contains("DELETE FROM `creature` WHERE", StringComparison.Ordinal) && apply.Contains(authoringTemplate.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), "the export lacks the DELETE");
            creatures.SmokeHistory(false);
            return true;
        }),
        ("delete-undone", () =>
        {
            if (!creatures.SmokeDrawn(authoringTemplate!.Guid))
            {
                return false;
            }

            Require(creatures.SmokeRecord(authoringTemplate.Guid) != null && drafts.Draft!.NetEdits().Count == 0, "undo did not restore the deleted creature");
            return true;
        }),
        ("fields-show-the-row", () =>
        {
            creatures.Select(authoringTemplate!.Guid);
            if (creatures.SelectedGuid != authoringTemplate.Guid || !creatures.CanDelete || creatures.SmokeRow() is not { } row)
            {
                return false;
            }

            var fields = creatures.SmokeFields;
            Require(fields.Visible && fields.SmokeOpen, "the spawn fields are not shown for a selected creature");
            var respawn = fields.Field("spawntimesecs");
            Require(respawn?.Text == row["spawntimesecs"], $"respawn shows '{respawn?.Text}' but the row holds {row["spawntimesecs"]}");
            var flags = fields.Field("npcflag")!.Value;
            bool tc = ContentDraftFiles.Sources.DialectOf(creatures.SmokeSourceKey) == WorldDialect.TrinityCore;
            if (tc)
            {
                // Most TrinityCore spawns inherit their flags: the field is empty and the template's value shows through.
                Require(row.IsNull("npcflag") == (flags.Text == ""), "the npcflag field disagrees with the row's NULL");
                if (flags.Text == "" && !flags.Template.StartsWith("Template: ", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            authoringRespawn = row["spawntimesecs"];
            return true;
        }),
        ("fields-are-on-screen", () =>
        {
            creatures.SmokeRevealFields();
            Screenshot("fields");
            return true;
        }),
        ("fields-edit-respawn", () =>
        {
            creatures.SmokeFields.SmokeType("spawntimesecs", "777");
            return true;
        }),
        ("fields-respawn-saved-and-exported", () =>
        {
            Require(creatures.SmokeRow()?["spawntimesecs"] == "777", "the respawn was not saved");
            Require(creatures.LastMessage == "Saved.", "the user was told: " + creatures.LastMessage);
            Require(drafts.Draft!.NetEdits().Count == 1, "one field edit should be one net change");
            Require(AuthoringScript(creatures.SmokeSourceKey).Contains("UPDATE `creature` SET `spawntimesecs` = 777", StringComparison.Ordinal), "the export lacks the respawn update");
            creatures.SmokeFields.SmokeType("wander_distance", "6");
            return true;
        }),
        ("fields-radius-makes-it-wander", () =>
        {
            var row = creatures.SmokeRow()!;
            Require(row["MovementType"] == "1" && row.F("wander_distance") == 6, "a wander radius should switch the creature to wandering");
            Require(creatures.SmokeFields.Field("MovementType")!.Value.Text == "Wanders", "the movement field does not show the change");
            creatures.SmokeFields.SmokeType("spawntimesecs", "-5");
            return true;
        }),
        ("fields-refused-value-snaps-back", () =>
        {
            Require(creatures.SmokeRow()!["spawntimesecs"] == "777", "an invalid respawn changed the row");
            Require(creatures.SmokeFields.Field("spawntimesecs")!.Value.Text == "777", "the field kept the refused text");
            Require(creatures.LastMessage.Contains("Respawn after", StringComparison.Ordinal), "the user was not told why: " + creatures.LastMessage);
            creatures.SmokeFields.SmokeType("npcflag", "3");
            return true;
        }),
        ("fields-override-then-use-template", () =>
        {
            var flags = creatures.SmokeFields.Field("npcflag")!.Value;
            Require(creatures.SmokeRow()!["npcflag"] == "3" && flags.Text == "3" && flags.CanReset, "the override did not stick or no reset is offered");
            creatures.SmokeFields.SmokePressReset("npcflag");
            return true;
        }),
        ("fields-reset-and-undo-everything", () =>
        {
            var row = creatures.SmokeRow()!;
            bool tc = ContentDraftFiles.Sources.DialectOf(creatures.SmokeSourceKey) == WorldDialect.TrinityCore;
            Require(tc ? row.IsNull("npcflag") : row["npcflag"] == "0", "Use template did not clear the override");
            Require(!creatures.SmokeFields.Field("npcflag")!.Value.CanReset, "Use template is still offered");
            for (int i = 0; i < 10 && drafts.Draft!.NetEdits().Count > 0; i++)
            {
                creatures.SmokeHistory(false);
            }

            Require(drafts.Draft!.NetEdits().Count == 0 && creatures.SmokeRow()!["spawntimesecs"] == authoringRespawn, "undo did not restore the row");
            Require(creatures.SmokeFields.Field("spawntimesecs")!.Value.Text == authoringRespawn, "the field does not show the restored value");
            return true;
        }),
        ("place-search-and-arm", () =>
        {
            if (authoringSearch == null)
            {
                authoringSearch = creatures.PlaceCard.SmokeSearchAsync(authoringTemplate!.Name);
                return false;
            }

            if (!authoringSearch.IsCompleted)
            {
                return false;
            }

            Require(creatures.PlaceCard.SmokeCount > 0, $"searching for '{authoringTemplate!.Name}' found nothing");
            int index = Enumerable.Range(0, creatures.PlaceCard.SmokeCount).First(i => creatures.PlaceCard.SmokeItem(i).EndsWith($"·   {authoringTemplate.TemplateId}", StringComparison.Ordinal));
            authoringPickIndex = index;
            creatures.PlaceCard.SmokeChoose(index);
            Require(placement.Current is { Kind: SpawnKind.Creature } armed && armed.Entry == authoringTemplate.TemplateId, "choosing a result did not arm the placement tool");
            Require(creatures.PlaceCard.SmokeHint.StartsWith("Placing ", StringComparison.Ordinal), "the card does not say what is being placed: " + creatures.PlaceCard.SmokeHint);
            return true;
        }),
        ("place-pointer-shows-a-ghost-on-the-ground", () =>
        {
            authoringScreen = GetViewport().GetVisibleRect().Size * new Vector2(0.5f, 0.62f);
            SmokeMove(authoringScreen);
            return true;
        }),
        ("place-ghost-follows-the-cursor", () =>
        {
            if (placement.Target is not Vector3 target)
            {
                SmokeMove(authoringScreen + Vector2.One * (spawnAuthoringRun.StepTime > 1 ? 1 : 0));
                return false;
            }

            // The viewer lifts a camera that sits low over the ground, so wait until it has stopped; the ghost
            // is then recomputed for the final camera on the following frame.
            if (camera.GlobalTransform != authoringCamera)
            {
                authoringCamera = camera.GlobalTransform;
                authoringSettle = 0;
                return false;
            }

            if (++authoringSettle < 3)
            {
                return false;
            }

            Require(placement.Ghost.Visible, "the ghost is hidden although the cursor is over ground");
            authoringTarget = target;
            Screenshot("ghost");
            Require(Math.Abs((GroundAt(target + Vector3.Up * 0.5f).Height ?? target.Y) - target.Y) < 0.1f, "the ghost is not on the ground");
            var projected = camera.UnprojectPosition(target);
            Require(projected.DistanceTo(authoringScreen) < 8, $"the ghost is not under the cursor: cursor {authoringScreen}, ghost at {projected}, ground {target}, camera {camera.GlobalPosition}, viewport {GetViewport().GetVisibleRect().Size}, transform {GetViewport().GetFinalTransform()}, fresh ray hit {PlacementTool.RayGround(camera, authoringScreen, TerrainHeightAt)}, rotation {camera.Rotation}, fov {camera.Fov} keep {camera.KeepAspect}, rayDir {camera.ProjectRayNormal(authoringScreen)}, unproject(fresh) {camera.UnprojectPosition(PlacementTool.RayGround(camera, authoringScreen, TerrainHeightAt) ?? Vector3.Zero)}");
            return true;
        }),
        ("place-click-puts-it-there-facing-the-camera", () =>
        {
            authoringCount = creatures.Records.Count;
            SmokeClick(authoringScreen, shift: false);
            return true;
        }),
        ("place-clicked-spawn-exists", () =>
        {
            if (creatures.Records.Count != authoringCount + 1 || creatures.SelectedGuid == authoringTemplate!.Guid || creatures.SelectedGuid is not ulong guid)
            {
                return false;
            }

            var record = creatures.SmokeRecord(guid)!;
            var target = authoringTarget;
            var server = ViewSpace.ToServer(target);
            Require(record.TemplateId == authoringTemplate.TemplateId, "placed another template");
            Require(Math.Abs(record.Pose.X - server.X) < 0.3 && Math.Abs(record.Pose.Y - server.Y) < 0.3, $"placed at ({record.Pose.X:F1}, {record.Pose.Y:F1}), the cursor was at ({server.X:F1}, {server.Y:F1})");
            var cam = ViewSpace.ToServer(camera.GlobalPosition);
            double facing = SpawnPlacement.FacingHeading(record.Pose.X, record.Pose.Y, cam.X, cam.Y);
            Require(Math.Abs(WorldCoordinates.NormalizeHeading(record.Pose.Heading - facing)) < 0.05 || Math.Abs(WorldCoordinates.NormalizeHeading(record.Pose.Heading - facing) - 2 * Math.PI) < 0.05, "the spawn does not face the camera");
            Require(placement.Current == null, "a plain click should stop placing");
            Require(creatures.PlaceCard.SmokeHint.StartsWith("Search ", StringComparison.Ordinal), "the card still says it is placing");
            Require(AuthoringScript(creatures.SmokeSourceKey).Contains("INSERT INTO `creature`", StringComparison.Ordinal), "the export lacks the INSERT");
            authoringCopy = guid;
            Screenshot("placed");
            return true;
        }),
        ("place-shift-click-keeps-placing", () =>
        {
            creatures.PlaceCard.SmokeChoose(authoringPickIndex);
            Require(placement.Current != null, "re-arming failed");
            authoringCount = creatures.Records.Count;
            var other = authoringScreen + new Vector2(60, 20);
            SmokeMove(other);
            SmokeClick(authoringScreen, shift: true);
            SmokeClick(other, shift: true);
            return true;
        }),
        ("place-two-more-and-still-armed", () =>
        {
            if (creatures.Records.Count < authoringCount + 2)
            {
                return false;
            }

            Require(placement.Current != null, "Shift-click should keep the template armed");
            SmokeKey(Key.Escape);
            return true;
        }),
        ("place-escape-stops-and-undo-clears", () =>
        {
            if (placement.Current != null)
            {
                return false;
            }

            for (int i = 0; i < 10 && drafts.Draft!.NetEdits().Count > 0; i++)
            {
                creatures.SmokeHistory(false);
            }

            Require(drafts.Draft!.NetEdits().Count == 0, "undo left placed spawns behind");
            return true;
        }),
        ("place-gameobject-search-and-arm", () =>
        {
            if (authoringSearch == null || authoringSearchObject == false)
            {
                authoringSearchObject = true;
                authoringSearch = gameObjects.PlaceCard.SmokeSearchAsync(authoringObject!.TemplateId.ToString());
                return false;
            }

            if (!authoringSearch.IsCompleted)
            {
                return false;
            }

            Require(gameObjects.PlaceCard.SmokeCount > 0, $"searching for {authoringObject!.TemplateId} found nothing");
            int index = Enumerable.Range(0, gameObjects.PlaceCard.SmokeCount).First(i => gameObjects.PlaceCard.SmokeItem(i).EndsWith($"·   {authoringObject.TemplateId}", StringComparison.Ordinal));
            gameObjects.PlaceCard.SmokeChoose(index);
            Require(placement.Current is { Kind: SpawnKind.GameObject } armed && armed.Entry == authoringObject.TemplateId, "choosing a result did not arm the placement tool");
            Require(creatures.PlaceCard.SmokeHint.StartsWith("Search ", StringComparison.Ordinal) && gameObjects.PlaceCard.SmokeHint.StartsWith("Placing ", StringComparison.Ordinal), "only the GameObject card should say it is placing");
            authoringCount = gameObjects.Records.Count;
            SmokeMove(authoringScreen);
            return true;
        }),
        ("place-gameobject-click", () =>
        {
            if (placement.Target == null)
            {
                SmokeMove(authoringScreen);
                return false;
            }

            SmokeClick(authoringScreen, shift: false);
            return true;
        }),
        ("place-gameobject-exists-and-undone", () =>
        {
            if (gameObjects.Records.Count != authoringCount + 1 || gameObjects.SmokeSelected == authoringObject!.Guid || gameObjects.SmokeSelected is not ulong guid)
            {
                return false;
            }

            var record = gameObjects.SmokeRecord(guid)!;
            Require(record.TemplateId == authoringObject.TemplateId && record.Name == authoringObject.Name && placement.Current == null, "placed the wrong template or kept placing");
            Require(AuthoringScript(gameObjects.SmokeSourceKey).Contains("INSERT INTO `gameobject`", StringComparison.Ordinal), "the export lacks the INSERT");
            gameObjects.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0 && gameObjects.SmokeRecord(guid) == null, "undo left the GameObject");
            return true;
        }),
        ("group-ctrl-click-adds-creatures", () =>
        {
            creatures.Select(authoringTemplate!.Guid);
            FocusPose(authoringTemplate.Pose);
            if (creatures.SelectedGuid != authoringTemplate.Guid || !creatures.CanDelete)
            {
                return false;
            }

            // Two other creatures the cursor can really reach: a ray at their centre hits them.
            var reachable = creatures.Records
                .Where(s => s.Guid != authoringTemplate.Guid)
                .Select(s => (s.Guid, Screen: camera.UnprojectPosition(creatures.PickPointOf(s.Guid)), Behind: camera.IsPositionBehind(creatures.PickPointOf(s.Guid))))
                .Where(c => !c.Behind && GetViewport().GetVisibleRect().Grow(-120).HasPoint(c.Screen) && creatures.FindHit(camera, c.Screen)?.Guid == c.Guid)
                .Take(2).ToArray();
            if (reachable.Length < 2)
            {
                return false;
            }

            authoringGroup = [authoringTemplate.Guid, .. reachable.Select(c => c.Guid)];
            foreach (var (_, screen, _) in reachable)
            {
                SmokeClick(screen, shift: false, ctrl: true);
            }

            return true;
        }),
        ("group-has-three-and-says-so", () =>
        {
            if (creatures.Group.Count < 3)
            {
                return false;
            }

            Require(creatures.Group.SequenceEqual(authoringGroup), "the group is not the primary followed by the two Ctrl-clicked creatures");
            Require(creatures.SmokeMeta.Contains("(+2 selected)", StringComparison.Ordinal), "the inspector does not say two more are selected: " + creatures.SmokeMeta);
            Require(!creatures.SmokeFields.Visible, "the spawn fields should hide while several creatures are selected");
            Screenshot("group");
            // A second Ctrl-click on a member takes it out again.
            SmokeClick(camera.UnprojectPosition(creatures.PickPointOf(authoringGroup[2])), shift: false, ctrl: true);
            return true;
        }),
        ("group-ctrl-click-removes", () =>
        {
            if (creatures.Group.Count != 2)
            {
                return false;
            }

            Require(!creatures.InGroup(authoringGroup[2]), "the Ctrl-clicked member is still selected");
            SmokeClick(camera.UnprojectPosition(creatures.PickPointOf(authoringGroup[2])), shift: false, ctrl: true);
            return true;
        }),
        ("group-duplicate-copies-all", () =>
        {
            if (creatures.Group.Count != 3 || !creatures.CanDuplicate)
            {
                return false;
            }

            authoringCount = creatures.Records.Count;
            SmokeKey(Key.D, ctrl: true);
            return true;
        }),
        ("group-copies-are-the-selection", () =>
        {
            if (creatures.Records.Count != authoringCount + 3 || creatures.Group.Count != 3 || creatures.Group.Any(g => authoringGroup.Contains(g)))
            {
                return false;
            }

            Require(drafts.Draft!.NetEdits().Count(e => e.Edit.Table == "creature") == 3, "three copies should be three staged inserts");
            Require(AuthoringScript(creatures.SmokeSourceKey).Split("INSERT INTO `creature`").Length - 1 >= 3, "the export lacks the three INSERTs");
            authoringCopies = creatures.Group.ToArray();
            authoringBefore = authoringCopies.Select(g => creatures.SmokeRecord(g)!.Pose).ToArray();
            return true;
        }),
        ("group-move-drags-the-primary", () =>
        {
            Require(creatures.SmokeDrag(camera), "could not grab the primary creature's move handle");
            return true;
        }),
        ("group-move-moved-everyone-the-same", () =>
        {
            var after = authoringCopies.Select(g => creatures.SmokeRecord(g)!.Pose).ToArray();
            double dy = after[0].Y - authoringBefore[0].Y;
            if (Math.Abs(dy) < 0.5)
            {
                return false;
            }

            for (int i = 0; i < after.Length; i++)
            {
                Require(Math.Abs((after[i].Y - authoringBefore[i].Y) - dy) < 0.05 && Math.Abs(after[i].X - authoringBefore[i].X) < 0.05, $"creature {i} moved differently from the primary");
            }

            Require(creatures.Group.Count == 3, "the group was lost by the move");
            Require(drafts.Draft!.NetEdits().Count(e => e.Edit.Table == "creature") == 3, "a group move of staged copies should stay three inserts");
            return true;
        }),
        ("group-delete-removes-all-copies", () =>
        {
            SmokeKey(Key.Delete);
            return true;
        }),
        ("group-copies-gone-nothing-left", () =>
        {
            if (authoringCopies.Any(g => creatures.SmokeRecord(g) != null))
            {
                return false;
            }

            Require(creatures.Group.Count == 0 && drafts.Draft!.NetEdits().Count == 0, "deleting the staged copies should leave nothing selected or to export");
            return true;
        }),
        ("group-box-starts-dragging", () =>
        {
            creatures.Deselect();
            var points = authoringGroup.Select(g => camera.UnprojectPosition(creatures.PickPointOf(g))).ToArray();
            authoringBoxMin = new Vector2(points.Min(p => p.X), points.Min(p => p.Y)) - new Vector2(50, 50);
            authoringBoxMax = new Vector2(points.Max(p => p.X), points.Max(p => p.Y)) + new Vector2(50, 50);
            var window = GetViewport().GetFinalTransform() * authoringBoxMin;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window, GlobalPosition = window, ShiftPressed = true });
            SmokeMove(authoringBoxMin + (authoringBoxMax - authoringBoxMin) / 2);
            return true;
        }),
        ("group-box-is-drawn-then-released", () =>
        {
            if (!selectionBox.Visible)
            {
                SmokeMove(authoringBoxMin + (authoringBoxMax - authoringBoxMin) / 2 + Vector2.One * (spawnAuthoringRun.StepTime > 1 ? 1 : 0));
                return false;
            }

            SmokeMove(authoringBoxMax);
            var window = GetViewport().GetFinalTransform() * authoringBoxMax;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = window, GlobalPosition = window, ShiftPressed = true });
            return true;
        }),
        ("group-box-result", () =>
        {
            if (creatures.Group.Count < 3)
            {
                return false;
            }

            Require(!selectionBox.Visible, "the selection box stays on screen after the drag");
            Require(authoringGroup.All(g => creatures.Group.Contains(g)), "the box missed creatures inside it");
            creatures.Deselect();
            return true;
        }),
        ("appearance-set-emote-and-stance", () =>
        {
            creatures.Select(authoringTemplate!.Guid);
            if (creatures.SelectedGuid != authoringTemplate.Guid || !creatures.CanDelete || creatures.SmokeRow() == null)
            {
                return false;
            }

            Require(creatures.SmokeFields.Field("emote") != null && creatures.SmokeFields.Field("auras") != null, "the Appearance fields are not shown");
            authoringAddonBefore = creatures.SmokeAddon();
            creatures.SmokeFields.SmokeType("emote", "69");
            return true;
        }),
        ("appearance-emote-saved", () =>
        {
            Require(creatures.SmokeAddon()?.U("emote") == 69 && creatures.LastMessage == "Saved.", "the emote was not saved: " + creatures.LastMessage);
            Require(AuthoringScript(creatures.SmokeSourceKey).Contains("`creature_addon`", StringComparison.Ordinal), "the export lacks the addon change");
            creatures.SmokeRevealField("emote");
            Screenshot("appearance");
            creatures.SmokeFields.SmokeType("Stance".Length > 0 && ContentDraftFiles.Sources.DialectOf(creatures.SmokeSourceKey) == WorldDialect.TrinityCore ? "StandState" : "bytes1", "Sleeping");
            return true;
        }),
        ("appearance-stance-saved-keeping-the-other-bits", () =>
        {
            bool tc = ContentDraftFiles.Sources.DialectOf(creatures.SmokeSourceKey) == WorldDialect.TrinityCore;
            var addon = creatures.SmokeAddon()!;
            Require(tc ? addon.U("StandState") == 3 : (addon.U("bytes1") & 0xFF) == 3, "the stance was not saved");
            if (!tc && authoringAddonBefore != null)
            {
                Require((addon.U("bytes1") & ~0xFFu) == (authoringAddonBefore.U("bytes1") & ~0xFFu), "setting the stance changed the other bytes of bytes1");
            }

            creatures.SmokeFields.SmokeType("auras", "12345 12345");
            return true;
        }),
        ("appearance-bad-auras-refused-then-undone", () =>
        {
            Require(creatures.LastMessage.Contains("listed twice", StringComparison.Ordinal), "the user was not told why the auras were refused: " + creatures.LastMessage);
            Require(creatures.SmokeFields.Field("auras")!.Value.Text == "" || creatures.SmokeFields.Field("auras")!.Value.Text == (creatures.SmokeAddon()?.IsNull("auras") == false ? creatures.SmokeAddon()!["auras"] : ""), "the field kept the refused text");
            for (int i = 0; i < 10 && drafts.Draft!.NetEdits().Count > 0; i++)
            {
                creatures.SmokeHistory(false);
            }

            Require(drafts.Draft!.NetEdits().Count == 0 && creatures.SmokeAddon() == authoringAddonBefore, "undo did not restore the addon");
            return true;
        }),
        ("referenced-creature-select", () =>
        {
            if (authoringReferenced == 0)
            {
                var found = creatures.SmokeReferenced();
                Require(found != null, "no creature near Goldshire is referenced by a pool, event, group, link or SmartAI rule; the dialog cannot be tested");
                authoringReferenced = found!.Value.Guid;
                authoringItems = found.Value.Items;
                GD.Print($"SPAWN_AUTHORING_REFERENCED creature {authoringReferenced}: {string.Join("; ", SpawnImpact.Lines(authoringItems))}");
            }

            creatures.Select(authoringReferenced);
            if (creatures.SelectedGuid != authoringReferenced || !creatures.CanDelete)
            {
                return false;
            }

            creatures.SmokePressDelete();
            return true;
        }),
        ("referenced-creature-asks-first", () =>
        {
            if (creatures.SmokeDeleteDialog is not { } text)
            {
                return false;
            }

            Require(text.Contains("is still referenced", StringComparison.Ordinal), "the dialog does not say the spawn is referenced");
            foreach (var item in authoringItems)
            {
                Require(text.Contains(item.Reference.Table, StringComparison.Ordinal), $"the dialog lacks {item.Reference.Table}");
            }

            Screenshot("delete-dialog");
            Require(creatures.SmokeRecord(authoringReferenced) != null && drafts.Draft!.NetEdits().Count == 0, "the spawn was deleted before the answer");
            Require(creatures.SmokeAnswerDelete(false), "could not answer the dialog");
            return true;
        }),
        ("referenced-creature-kept", () =>
        {
            if (creatures.SmokeDeleteDialog != null)
            {
                return false;
            }

            Require(creatures.SmokeRecord(authoringReferenced) != null && creatures.SmokeDrawn(authoringReferenced), "Keep still removed the creature");
            Require(drafts.Draft!.NetEdits().Count == 0, "Keep still staged a change");
            creatures.SmokePressDelete();
            return true;
        }),
        ("referenced-creature-deleted-anyway", () =>
        {
            if (creatures.SmokeDeleteDialog == null)
            {
                return false;
            }

            creatures.SmokeAnswerDelete(true);
            return true;
        }),
        ("referenced-creature-gone-and-undone", () =>
        {
            if (creatures.SmokeRecord(authoringReferenced) != null)
            {
                return false;
            }

            Require(AuthoringScript(creatures.SmokeSourceKey).Contains("DELETE FROM `creature` WHERE", StringComparison.Ordinal), "the export lacks the DELETE");
            creatures.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            return true;
        }),
        ("pathed-creature-select", () =>
        {
            if (authoringPath == null && !authoringSkipPath)
            {
                authoringPath = creatures.SmokeOrphanPath();
                if (authoringPath == null)
                {
                    GD.Print("SPAWN_AUTHORING_NOTE no creature near Goldshire walks a path of its own; the path warning is covered by the unit and database tests");
                    authoringSkipPath = true;
                    return true;
                }

                GD.Print($"SPAWN_AUTHORING_PATHED creature {authoringPath.Guid}: path {authoringPath.PathId}, {authoringPath.Nodes} nodes");
            }

            if (authoringSkipPath)
            {
                return true;
            }

            creatures.Select(authoringPath!.Guid);
            if (creatures.SelectedGuid != authoringPath.Guid || !creatures.CanDelete)
            {
                return false;
            }

            creatures.SmokePressDelete();
            return true;
        }),
        ("pathed-creature-names-its-path", () =>
        {
            if (authoringSkipPath)
            {
                return true;
            }

            if (creatures.SmokeDeleteDialog is not { } text)
            {
                return false;
            }

            Require(text.Contains($"waypoint path {authoringPath!.PathId} ({authoringPath.Nodes} node", StringComparison.Ordinal), "the dialog does not name the path that would be left unused: " + text);
            Require(creatures.SmokeRecord(authoringPath.Guid) != null && drafts.Draft!.NetEdits().Count == 0, "the creature was deleted before the answer");
            creatures.SmokeAnswerDelete(true);
            return true;
        }),
        ("pathed-creature-gone-and-undone", () =>
        {
            if (authoringSkipPath)
            {
                return true;
            }

            if (creatures.SmokeRecord(authoringPath!.Guid) != null)
            {
                return false;
            }

            creatures.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            return true;
        }),
        ("copy-select-gameobject", () =>
        {
            gameObjects.SmokeSelect(authoringObject!.Guid);
            if (gameObjects.SmokeSelected != authoringObject.Guid || !gameObjects.CanDuplicate)
            {
                return false;
            }

            Require(gameObjects.SmokeCopyButtons == (true, true), "the Duplicate and Delete buttons should be enabled for a loaded GameObject");
            authoringCount = gameObjects.Records.Count;
            gameObjects.SmokePressDuplicate();
            return true;
        }),
        ("copy-gameobject-drawn", () =>
        {
            if (gameObjects.Records.Count != authoringCount + 1 || gameObjects.SmokeSelected == authoringObject!.Guid || gameObjects.SmokeSelected is not ulong copy)
            {
                return false;
            }

            authoringCopy = copy;
            var record = gameObjects.SmokeRecord(copy)!;
            Require(record.TemplateId == authoringObject.TemplateId && record.Name == authoringObject.Name && record.DisplayId == authoringObject.DisplayId, "the copy is another GameObject");
            Require(record.Rotation == authoringObject.Rotation, "the copy lost its facing");
            Require(gameObjects.SmokeListed(copy), "the copy is not in the list");
            Require(AuthoringScript(gameObjects.SmokeSourceKey).Contains("INSERT INTO `gameobject`", StringComparison.Ordinal), "the export lacks the copy's INSERT");
            return true;
        }),
        ("delete-gameobject-with-the-button", () =>
        {
            gameObjects.SmokePressDelete();
            return true;
        }),
        ("gameobject-copy-gone", () =>
        {
            if (gameObjects.SmokeRecord(authoringCopy) != null)
            {
                return false;
            }

            Require(gameObjects.SmokeSelected == null && !gameObjects.SmokeListed(authoringCopy), "the deleted GameObject is still selected or listed");
            Require(drafts.Draft!.NetEdits().Count == 0, "placing then deleting a copy should leave nothing to export");
            return true;
        }),
        ("object-fields", () =>
        {
            gameObjects.SmokeSelect(authoringObject!.Guid);
            if (gameObjects.SmokeSelected != authoringObject.Guid || !gameObjects.CanDelete || gameObjects.SmokeRow() is not { } row)
            {
                return false;
            }

            var fields = gameObjects.SmokeFields;
            Require(fields.Visible && fields.Field("state") != null && fields.Field("animprogress") != null, "the GameObject fields are not shown");
            Require(fields.Field("spawntimesecs")!.Value.Text == row["spawntimesecs"], "respawn does not show the row");
            fields.SmokeType("state", "Active (open)");
            return true;
        }),
        ("object-state-saved", () =>
        {
            Require(gameObjects.SmokeRow()!["state"] == "0" && gameObjects.SmokeFields.Field("state")!.Value.Text == "Active (open)", "the state was not saved");
            Require(AuthoringScript(gameObjects.SmokeSourceKey).Contains("`state` = 0", StringComparison.Ordinal), "the export lacks the state update");
            return true;
        }),
        ("object-invisibility-creates-an-addon", () =>
        {
            Require(gameObjects.SmokeAddon() == null || gameObjects.SmokeAddon()!.U("invisibilityType") == 0, "this GameObject already has an invisibility; pick another for the test");
            gameObjects.SmokeFields.SmokeType("invisibilityType", "3");
            return true;
        }),
        ("object-invisibility-saved", () =>
        {
            Require(gameObjects.SmokeAddon()?.U("invisibilityType") == 3 && gameObjects.LastMessage == "Saved.", "the invisibility was not saved: " + gameObjects.LastMessage);
            Require(AuthoringScript(gameObjects.SmokeSourceKey).Contains("`gameobject_addon`", StringComparison.Ordinal), "the export lacks the addon change");
            gameObjects.SmokeFields.SmokeType("animprogress", "300");
            return true;
        }),
        ("object-refused-and-undone", () =>
        {
            Require(gameObjects.SmokeRow()!["animprogress"] != "300" && gameObjects.SmokeFields.Field("animprogress")!.Value.Text != "300", "an out-of-range animation progress was accepted");
            Require(gameObjects.LastMessage.Contains("Animation progress", StringComparison.Ordinal), "the user was not told why: " + gameObjects.LastMessage);
            for (int i = 0; i < 10 && drafts.Draft!.NetEdits().Count > 0; i++)
            {
                gameObjects.SmokeHistory(false);
            }

            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            return true;
        }),
        ("object-group-focus", () =>
        {
            creatures.Deselect();
            gameObjects.SmokeSelect(authoringObject!.Guid);
            FocusPose(authoringObject.Pose);
            authoringSettle = 0;
            return true;
        }),
        ("object-group-ctrl-click", () =>
        {
            // Let the camera move settle: a region change reloads the GameObjects and clears the selection.
            if (gameObjects.IsLoading || ++authoringSettle < 30 || gameObjects.SmokeSelected != authoringObject!.Guid || !gameObjects.CanDelete)
            {
                if (!gameObjects.IsLoading && gameObjects.SmokeSelected != authoringObject!.Guid)
                {
                    gameObjects.SmokeSelect(authoringObject.Guid);
                }

                return false;
            }

            // Two other GameObjects a Ctrl-click really reaches: the viewer sends the click to the nearer of
            // creature and GameObject, and a click on the primary's gizmo starts its drag instead.
            var primaryScreen = camera.UnprojectPosition(GameObjectOverlay.ViewPosition(gameObjects.SmokeRecord(authoringObject.Guid)!));
            var reachable = gameObjects.Records
                .Where(s => s.Guid != authoringObject.Guid)
                .Select(s => (s.Guid, Point: GameObjectOverlay.ViewPosition(s) + Vector3.Up))
                .Select(c => (c.Guid, c.Point, Screen: camera.UnprojectPosition(c.Point)))
                .Where(c => !camera.IsPositionBehind(c.Point)
                    && GetViewport().GetVisibleRect().Grow(-120).HasPoint(c.Screen)
                    && c.Screen.DistanceTo(primaryScreen) > 150
                    && gameObjects.FindHit(camera, c.Screen) is { } hit && hit.Guid == c.Guid
                    && (creatures.FindHit(camera, c.Screen) is not { } nearer || nearer.Distance > hit.Distance))
                .Take(2).ToArray();
            if (reachable.Length < 2)
            {
                GD.Print("SPAWN_AUTHORING_NOTE fewer than two GameObjects can be clicked from here; their group commands are covered by the unit tests");
                authoringSkipObjectGroup = true;
                return true;
            }

            authoringObjectGroup = [authoringObject.Guid, .. reachable.Select(c => c.Guid)];
            foreach (var c in reachable)
            {
                SmokeClick(c.Screen, shift: false, ctrl: true);
            }

            return true;
        }),
        ("object-group-has-three", () =>
        {
            if (authoringSkipObjectGroup)
            {
                return true;
            }

            if (gameObjects.Group.Count < 3 || !gameObjects.CanDuplicate)
            {
                return false;
            }

            Require(gameObjects.Group.SequenceEqual(authoringObjectGroup), "the GameObject group is not the primary followed by the Ctrl-clicked ones");
            authoringCount = gameObjects.Records.Count;
            gameObjects.SmokePressDuplicate();
            return true;
        }),
        ("object-group-duplicate-copies-all", () =>
        {
            if (authoringSkipObjectGroup)
            {
                return true;
            }

            if (gameObjects.Records.Count != authoringCount + 3 || gameObjects.Group.Count != 3 || gameObjects.Group.Any(g => authoringObjectGroup.Contains(g)))
            {
                return false;
            }

            Require(drafts.Draft!.NetEdits().Count(e => e.Edit.Table == "gameobject") == 3, "three copies should be three staged inserts");
            authoringCopies = gameObjects.Group.ToArray();
            gameObjects.SmokePressDelete();
            return true;
        }),
        ("object-group-copies-gone", () =>
        {
            if (authoringSkipObjectGroup)
            {
                return true;
            }

            if (authoringCopies.Any(g => gameObjects.SmokeRecord(g) != null))
            {
                return false;
            }

            Require(gameObjects.Group.Count == 0 && drafts.Draft!.NetEdits().Count == 0, "deleting the staged copies should leave nothing selected or to export");
            return true;
        }),
        ("referenced-gameobject", () =>
        {
            if (authoringReferenced == 0 || authoringKind != SpawnKind.GameObject)
            {
                authoringKind = SpawnKind.GameObject;
                var found = gameObjects.SmokeReferenced();
                if (found == null)
                {
                    GD.Print("SPAWN_AUTHORING_NOTE no GameObject near Goldshire is referenced; the dialog is covered by the creature steps");
                    authoringSkipObject = true;
                    return true;
                }

                authoringReferenced = found.Value.Guid;
                authoringItems = found.Value.Items;
                GD.Print($"SPAWN_AUTHORING_REFERENCED gameobject {authoringReferenced}: {string.Join("; ", SpawnImpact.Lines(authoringItems))}");
            }

            gameObjects.SmokeSelect(authoringReferenced);
            if (gameObjects.SmokeSelected != authoringReferenced || !gameObjects.CanDelete)
            {
                return false;
            }

            gameObjects.SmokePressDelete();
            return true;
        }),
        ("referenced-gameobject-asks-then-deletes", () =>
        {
            if (authoringSkipObject)
            {
                return true;
            }

            if (gameObjects.SmokeDeleteDialog is not { } text)
            {
                return false;
            }

            Require(text.Contains("is still referenced", StringComparison.Ordinal) && authoringItems.All(i => text.Contains(i.Reference.Table, StringComparison.Ordinal)), "the dialog does not list the references");
            Require(gameObjects.SmokeRecord(authoringReferenced) != null, "the GameObject was deleted before the answer");
            gameObjects.SmokeAnswerDelete(true);
            return true;
        }),
        ("referenced-gameobject-gone-and-undone", () =>
        {
            if (authoringSkipObject)
            {
                return true;
            }

            if (gameObjects.SmokeRecord(authoringReferenced) != null)
            {
                return false;
            }

            gameObjects.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            return true;
        })
    ];

    ulong authoringReferenced;
    IReadOnlyList<SpawnImpactItem> authoringItems = [];
    SpawnKind authoringKind = SpawnKind.Creature;
    bool authoringSkipObject;
    ulong[] authoringGroup = [];
    ulong[] authoringObjectGroup = [];
    bool authoringSkipObjectGroup;
    int authoringSettle;
    Transform3D authoringCamera;
    Vector2 authoringBoxMin, authoringBoxMax;
    ulong[] authoringCopies = [];
    WorldPose[] authoringBefore = [];
    Task? authoringSearch;
    bool authoringSearchObject;
    int authoringPickIndex;
    Vector2 authoringScreen;
    Vector3 authoringTarget;
    string authoringRespawn = "";
    Row? authoringAddonBefore;
    SpawnPathUse? authoringPath;
    bool authoringSkipPath;

    int authoringCount;
    ulong authoringCopy;

    /// <summary>Send a key press and release through the viewport, the way the keyboard does, so menu accelerators run.</summary>
    void SmokeKey(Key key, bool ctrl = false)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = ctrl });
        }
    }

    /// <summary>A left click through the viewport, the way the mouse sends it.</summary>
    void SmokeClick(Vector2 screen, bool shift, bool ctrl = false)
    {
        foreach (bool pressed in new[] { true, false })
        {
            var window = GetViewport().GetFinalTransform() * screen;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = window, GlobalPosition = window, ShiftPressed = shift, CtrlPressed = ctrl });
        }
    }

    /// <summary>Move the pointer to a viewport point; events carry window pixels, which the content scale can change.</summary>
    void SmokeMove(Vector2 screen)
    {
        var window = GetViewport().GetFinalTransform() * screen;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = window, GlobalPosition = window });
    }
}
