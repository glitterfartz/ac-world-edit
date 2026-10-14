using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;
/// <summary>
/// Opt-in graphical regression using real SQL/MPQ sources and actual sidebar signals. Exercises
/// bounded admission, nearest entity picking, unloading layers and the known event-bound mannequin.
/// </summary>
public partial class TerrainViewer
{
    bool visibilitySmoke;
    int visibilityStage;
    double visibilityTime, visibilityChanged;
    readonly List<double> visibilityFrames = [];
    T VisibilityControl<T>(string name)
        where T : Node => (T)sidebar.FindChild(name, true, false);
    void VisibilityPreset(int index)
    {
        var control = VisibilityControl<OptionButton>("VisibilityPreset");
        control.Select(index);
        control.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        visibilityChanged = visibilityTime;
    }

    void VisibilityLayers(bool enabled)
    {
        foreach (string key in new[]
        {
            "doodads",
            "wmos",
            "creatures",
            "gameobjects"
        }

        )
        {
            VisibilityControl<CheckButton>(key).ButtonPressed = enabled;
        }

        visibilityChanged = visibilityTime;
    }

    void VisibilityCapture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://artifacts/{name}.png"));
        }
    }

    void VisibilitySmoke(double delta)
    {
        visibilityTime += delta;
        try
        {
            if (visibilityTime > 180)
            {
                throw new Exception($"Timeout at stage {visibilityStage}: {creatures.QueryStatus}");
            }

            if (creatures.Records.Count > creatures.Preview.QueryLimit || gameObjects.Records.Count > gameObjects.Preview.QueryLimit || creatures.ModelCreatureCount > creatures.Preview.ModelLimit || gameObjects.ModelObjectCount > gameObjects.Preview.ModelLimit)
            {
                throw new Exception("Spawn/query admission budget exceeded");
            }

            if (creatures.AdmittedCreatureCount > creatures.Preview.ModelLimit || gameObjects.AdmittedObjectCount > gameObjects.Preview.ModelLimit)
            {
                throw new Exception("Fallback or queued native model admission exceeded the cap");
            }

            if (busy || world == null || creatures.IsLoading || gameObjects.IsLoading || visibilityTime - visibilityChanged < 0.7)
            {
                return;
            }

            switch (visibilityStage)
            {
                case 0:
                    if (!TerrainSettled || models.Busy || gameObjects.ModelObjectCount == 0)
                    {
                        return;
                    }

                    visibilityFrames.Add(delta * 1000);
                    if (visibilityFrames.Count < 240)
                    {
                        return;
                    }

                    var times = visibilityFrames.Order().ToArray();
                    GD.Print($"VISIBILITY_WARM frames={times.Length} p50Ms={times[times.Length / 2]:F3} p95Ms={times[(int)(times.Length * .95)]:F3} creatureRows={creatures.Records.Count} creatureModels={creatures.ModelCreatureCount} gameObjectRows={gameObjects.Records.Count} gameObjectModels={gameObjects.ModelObjectCount}");
                    sidebar.ShowSceneTab();
                    visibilityChanged = visibilityTime;
                    visibilityStage = 1;
                    break;
                case 1:
                    VisibilityCapture("visibility-scene");
                    var list = VisibilityControl<ItemList>("GameObjectList");
                    list.Select(0);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, 0);
                    uint guid = uint.Parse(list.GetItemMetadata(0).AsString());
                    if (gameObjects.SelectedGuid != guid || creatures.SelectedGuid != null)
                    {
                        throw new Exception("GameObject list selection failed");
                    }

                    var spawn = gameObjects.Records.Single(s => s.Guid == guid);
                    FocusPose(spawn.Pose);
                    visibilityChanged = visibilityTime;
                    visibilityStage = 2;
                    break;
                case 2:
                    if (models.Busy || gameObjects.ModelObjectCount == 0)
                    {
                        return;
                    }

                    var objectSpawn = gameObjects.Records.OrderBy(s => GameObjectOverlay.ViewPosition(s).DistanceSquaredTo(camera.Position)).First();
                    var screen = camera.UnprojectPosition(GameObjectOverlay.ViewPosition(objectSpawn) + Vector3.Up * .5f);
                    var hit = gameObjects.FindHit(camera, screen);
                    if (!hit.HasValue)
                    {
                        throw new Exception("Native GameObject ray picking failed");
                    }

                    // Hide creatures so the actual input route must select the nearest GameObject.
                    VisibilityControl<CheckButton>("creatures").ButtonPressed = false;
                    _UnhandledInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = screen });
                    if (gameObjects.SelectedGuid != hit.Value.Guid)
                    {
                        throw new Exception("Viewport/list identity mismatch");
                    }
                    // Isolate the selected prop for graphical review; nearby trees can hide
                    // small ground-level GameObjects from this deterministic test camera.
                    VisibilityControl<CheckButton>("doodads").ButtonPressed = false;

                    visibilityChanged = visibilityTime;
                    visibilityStage = 3;
                    break;
                case 3:
                    VisibilityCapture("visibility-gameobject");
                    VisibilityLayers(false);
                    visibilityStage = 4;
                    break;
                case 4:
                    if (objects.ObjectCount != 0 || creatures.Records.Count != 0 || gameObjects.Records.Count != 0 || creatures.ModelCreatureCount != 0 || gameObjects.ModelObjectCount != 0 || models.Busy)
                    {
                        throw new Exception("Hidden layers retained active requests or meshes");
                    }

                    GD.Print("VISIBILITY_LAYERS_PASSED all-hidden/no-spawn-queries/no-model-queue");
                    VisibilityLayers(true);
                    FocusPose(new(0, -9492.72, 75.6383, 56.1841, 0));
                    VisibilityControl<LineEdit>("CreatureSearch").Text = "96098";
                    VisibilityPreset(0);
                    visibilityStage = 5;
                    break;
                case 5:
                    if (creatures.Records.Count != 0)
                    {
                        throw new Exception("Inactive mannequin leaked into defaults");
                    }

                    VisibilityPreset(1);
                    visibilityStage = 6;
                    break;
                case 6:
                    if (creatures.Records.Count != 1 || creatures.Records[0].Guid != 96098)
                    {
                        throw new Exception("All stored preview lost the mannequin");
                    }

                    var phase = VisibilityControl<LineEdit>("PhaseMask");
                    VisibilityPreset(0);
                    var events = VisibilityControl<OptionButton>("EventPreview");
                    events.Select((int)EventPreviewMode.Selected);
                    events.EmitSignal(OptionButton.SignalName.ItemSelected, (int)EventPreviewMode.Selected);
                    VisibilityControl<MenuButton>("EventPicker").GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 31);
                    phase.Text = "2";
                    phase.EmitSignal(LineEdit.SignalName.TextSubmitted, "2");
                    visibilityChanged = visibilityTime;
                    visibilityStage = 7;
                    break;
                case 7:
                    if (creatures.Records.Count != 0)
                    {
                        throw new Exception("Phase mismatch leaked through");
                    }

                    var phaseOne = VisibilityControl<LineEdit>("PhaseMask");
                    phaseOne.Text = "0x1";
                    phaseOne.EmitSignal(LineEdit.SignalName.TextSubmitted, "0x1");
                    visibilityChanged = visibilityTime;
                    visibilityStage = 8;
                    break;
                case 8:
                    if (creatures.Records.Count != 1 || creatures.Records[0].Guid != 96098)
                    {
                        throw new Exception("Selected event override failed");
                    }

                    if (models.Busy || creatures.ModelCreatureCount != 1)
                    {
                        return;
                    }

                    creatures.Select(96098);
                    visibilityChanged = visibilityTime;
                    visibilityStage = 9;
                    break;
                case 9:
                    VisibilityCapture("visibility-event-creature");
                    VisibilityPreset(1);
                    VisibilityPreset(0);
                    VisibilityControl<LineEdit>("CreatureSearch").Text = "";
                    visibilityStage = 10;
                    break;
                case 10:
                    if (creatures.Records.Any(s => s.Guid == 96098) || gameObjects.Records.Any(s => s.Pose.Map != currentMapId))
                    {
                        throw new Exception("Canceled preview generation leaked into the scene");
                    }

                    VisibilityControl<HSlider>("SpawnDistance").Value = 600;
                    var budget = VisibilityControl<OptionButton>("SpawnBudget");
                    budget.Select(0);
                    budget.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                    VisibilityPreset(1);
                    visibilityStage = 11;
                    break;
                case 11:
                    if (models.Busy)
                    {
                        return;
                    }

                    if (creatures.Preview.ModelLimit != 64 || creatures.Preview.ViewDistance != 600)
                    {
                        throw new Exception("Display budget controls failed");
                    }

                    GD.Print($"VISIBILITY_BROAD_BUDGET creatureRows={creatures.Records.Count} admittedCreatures={creatures.AdmittedCreatureCount} gameObjectRows={gameObjects.Records.Count} admittedGameObjects={gameObjects.AdmittedObjectCount} rowsCap={creatures.Preview.QueryLimit} modelsCap={creatures.Preview.ModelLimit}");
                    GD.Print("VISIBILITY_SMOKE_PASSED native-GameObjects/list/viewport/layers/default-event/all-stored/manual-event/phase/stale-generations/broad-preview-admission");
                    GetTree().Quit();
                    break;
            }
        }
        catch (Exception error)
        {
            SmokeFail($"VISIBILITY_SMOKE_FAILED stage={visibilityStage}: {error.Message}");
        }
    }
}
