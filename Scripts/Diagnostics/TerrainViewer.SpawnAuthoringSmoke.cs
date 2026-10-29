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
}
