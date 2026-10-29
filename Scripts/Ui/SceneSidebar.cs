using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;
/// <summary>
/// Scene visibility, shared SQL preview and focused entity pages. Layer toggles tear down work;
/// broad inspection never bypasses query or model budgets. Controls never alter source rows.
/// </summary>
public partial class SceneSidebar : PanelContainer
{
    readonly CreatureOverlay creatures;
    readonly GameObjectOverlay gameObjects;
    readonly GraveyardOverlay graveyards;
    readonly WaypointOverlay waypoints;
    readonly AreaTriggerOverlay areaTriggers;
    readonly ConnectionForm connectionPath;
    readonly WorldObjectScene scenery;
    TabContainer tabs = null!;
    OptionButton budget = null!;
    VisibilityPreviewPanel visibility = null!;
    CheckBox names = null!;
    HSlider distance = null!;
    Label distanceText = null!, limits = null!;
    readonly Dictionary<string, Label> counts = [];
    long previewChanged;
    bool previewPending;
    public bool DoodadsEnabled { get; private set; } = true;
    public bool WmosEnabled { get; private set; } = true;

    public SceneSidebar(CreatureOverlay creatures, GameObjectOverlay gameObjects, GraveyardOverlay graveyards, WaypointOverlay waypoints, AreaTriggerOverlay areaTriggers, WorldObjectScene scenery, ConnectionForm connectionPath)
    {
        this.connectionPath = connectionPath;
        connectionPath.Applied += () =>
        {
            creatures.Refresh();
            gameObjects.Refresh();
            graveyards.Refresh();
            waypoints.Refresh();
            areaTriggers.Refresh();
        };
        this.creatures = creatures;
        this.gameObjects = gameObjects;
        this.graveyards = graveyards;
        this.waypoints = waypoints;
        this.areaTriggers = areaTriggers;
        this.scenery = scenery;
        AnchorLeft = AnchorRight = AnchorBottom = 1;
        OffsetLeft = -424;
        OffsetRight = -20;
        OffsetTop = 20;
        OffsetBottom = -82;
        Name = "SceneExplorer";
        Theme = InspectorUi.ExplorerTheme();
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        AddChild(root);
        root.AddChild(InspectorUi.Text("Scene explorer", 23));
        root.AddChild(InspectorUi.Text("VISIBILITY  /  SELECTION", 10, true));
        tabs = new TabContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(tabs);
        unsaved = new Button
        {
            Name = "UnsavedChanges",
            Text = "Unsaved changes!  Review",
            Flat = true,
            Visible = false,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Open the Pending Changes window."
        };
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            unsaved.AddThemeColorOverride(state, new Color("ff4040"));
        }

        unsaved.Pressed += () => ReviewPendingRequested?.Invoke();
        var creaturePage = creatures.BuildPanel();
        var objectPage = gameObjects.BuildPanel();
        var graveyardPage = graveyards.BuildPanel();
        var triggerPage = areaTriggers.BuildPanel();
        var waypointPage = waypoints.BuildPanel();
        tabs.AddChild(BuildScenePage());
        tabs.AddChild(creaturePage);
        tabs.AddChild(objectPage);
        tabs.AddChild(graveyardPage);
        tabs.AddChild(triggerPage);
        tabs.AddChild(waypointPage);
        // One selection at a time across kinds, so menus and the gizmo act on a single target.
        Action[] deselect = [creatures.Deselect, gameObjects.Deselect, graveyards.Deselect, areaTriggers.Deselect, waypoints.Deselect];
        void Exclusive(int kind)
        {
            for (int i = 0; i < deselect.Length; i++)
            {
                if (i != kind)
                {
                    deselect[i]();
                }
            }

            tabs.CurrentTab = kind + 1;
        }

        root.AddChild(unsaved);
        creatures.SelectionChanged += () => Exclusive(0);
        gameObjects.SelectionChanged += () => Exclusive(1);
        graveyards.SelectionChanged += () => Exclusive(2);
        areaTriggers.SelectionChanged += () => Exclusive(3);
        waypoints.SelectionChanged += () => Exclusive(4);
    }

    Button unsaved = null!;
    /// <summary>
    /// Raised when the red "Unsaved changes!" indicator is clicked.
    /// </summary>
    public event Action? ReviewPendingRequested;
    public bool UnsavedVisible => unsaved.Visible;
    public void SetUnsaved(int count)
    {
        unsaved.Visible = count > 0;
        unsaved.Text = count > 0 ? $"Unsaved changes! ({count})  Review" : "";
    }

    public void ShowSceneTab() => tabs.CurrentTab = 0;
    Label databaseNote = null!;

    /// <summary>
    /// Disable the layers that read the world database (creatures, GameObjects, graveyards,
    /// AreaTriggers) while the open client has no matching database, and say why. Scenery and
    /// building layers are unaffected.
    /// </summary>
    public void SetDatabaseAvailable(bool available)
    {
        foreach (string key in new[] { "creatures", "gameobjects", "graveyards", "areatriggers", "waypoints" })
        {
            if (FindChild(key, true, false) is CheckButton toggle)
            {
                toggle.Disabled = !available;
            }
        }

        databaseNote.Visible = !available;
    }

    VBoxContainer connectionDetails = null!;
    ScrollContainer sceneScroll = null!;
    Control BuildScenePage()
    {
        var page = InspectorUi.Page("Scene", out var scroll);
        sceneScroll = scroll;
        var layers = InspectorUi.Card(page, "Scene layers");
        Layer(layers, "Scenery props", "doodads", value =>
        {
            DoodadsEnabled = value;
            ApplyScenery();
        });
        Layer(layers, "Buildings & interiors", "wmos", value =>
        {
            WmosEnabled = value;
            ApplyScenery();
        });
        Layer(layers, "Creatures", "creatures", creatures.SetLayerEnabled);
        Layer(layers, "GameObjects", "gameobjects", gameObjects.SetLayerEnabled);
        Layer(layers, "Graveyards", "graveyards", graveyards.SetLayerEnabled);
        Layer(layers, "AreaTriggers", "areatriggers", areaTriggers.SetLayerEnabled, areaTriggers.LayerEnabled);
        Layer(layers, "Waypoints", "waypoints", waypoints.SetLayerEnabled);
        layers.AddChild(InspectorUi.Text("Hidden layers stop queuing models and spawn queries.", 11, true));
        databaseNote = InspectorUi.Text("Database layers are off for this client; the world database does not describe it.", 11, true);
        databaseNote.Visible = false;
        layers.AddChild(databaseNote);
        var preview = InspectorUi.Card(page, "Visibility preview");
        visibility = new VisibilityPreviewPanel();
        visibility.Changed += markCustom => ApplyFilters(markCustom);
        preview.AddChild(visibility);
        var performance = InspectorUi.Card(page, "Display budget");
        distanceText = InspectorUi.Text("View distance  ·  200 yards", 13);
        performance.AddChild(distanceText);
        distance = new HSlider
        {
            Name = "SpawnDistance",
            MinValue = 60,
            MaxValue = 600,
            Step = 20,
            Value = 200
        };
        performance.AddChild(distance);
        distance.ValueChanged += value =>
        {
            distanceText.Text = $"View distance  ·  {value:0} yards";
            DebounceFilters();
        };
        distance.DragEnded += _ => ApplyFilters();
        budget = new OptionButton
        {
            Name = "SpawnBudget",
            FitToLongestItem = false
        };
        budget.AddItem("Low  ·  64 models per layer");
        budget.AddItem("Balanced  ·  128 models per layer");
        budget.AddItem("High  ·  256 models per layer");
        budget.Select(1);
        performance.AddChild(budget);
        budget.ItemSelected += _ => ApplyFilters();
        names = Toggle(performance, "Nearby name labels", false);
        limits = InspectorUi.Text("", 11, true);
        performance.AddChild(limits);
        var connection = InspectorUi.Card(page, "Data source");
        var advanced = connectionDetails = new VBoxContainer
        {
            Visible = false
        };
        connection.AddChild(InspectorUi.Button("Database connection…", () => advanced.Visible = !advanced.Visible));
        connection.AddChild(advanced);
        advanced.AddChild(InspectorUi.Text("World database (read-only)", 12, true));
        advanced.AddChild(connectionPath);
        advanced.AddChild(InspectorUi.Button("Apply & refresh", () => _ = connectionPath.ApplyAsync()));
        connection.AddChild(InspectorUi.Text("Read-only connection. Saved in your user profile, not the repo.", 11, true));
        UpdateControlState();
        return scroll;
    }

    /// <summary>Switch the visibility controls to the open client's server (see <see cref="VisibilityPreviewPanel.SetDialect"/>) and restart the preview from its defaults.</summary>
    public void SetDialect(WorldDialect value)
    {
        visibility.SetDialect(value);
        ApplyFilters(false);
    }

    void Layer(VBoxContainer parent, string name, string key, Action<bool> action, bool initial = true)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        var toggle = new CheckButton
        {
            Name = key,
            Text = name,
            ButtonPressed = initial,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddChild(toggle);
        toggle.Toggled += value => action(value);
        var count = InspectorUi.Text("0", 12, true);
        // Wrapped labels otherwise collapse to one character beside an expanding toggle.
        count.AutowrapMode = TextServer.AutowrapMode.Off;
        count.CustomMinimumSize = new(48, 0);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(count);
        counts[key] = count;
    }

    CheckBox Toggle(VBoxContainer parent, string text, bool initial)
    {
        var check = new CheckBox
        {
            Text = text,
            ButtonPressed = initial
        };
        parent.AddChild(check);
        check.Toggled += _ => ApplyFilters();
        return check;
    }

    void ApplyScenery() => scenery.SetLayers(DoodadsEnabled, WmosEnabled);
    void DebounceFilters()
    {
        previewPending = true;
        previewChanged = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public override void _Process(double delta)
    {
        if (previewPending && System.Diagnostics.Stopwatch.GetElapsedTime(previewChanged).TotalSeconds >= 0.3)
        {
            ApplyFilters();
        }
    }

    /// <summary>Merge the visibility rules with the display budget into the immutable policy; invalid input leaves the previous preview intact.</summary>
    void ApplyFilters(bool markCustom = true)
    {
        previewPending = false;
        if (visibility.Build() is not { } rules)
        {
            return;
        }

        int modelLimit = new[]
        {
            64,
            128,
            256
        }[budget.Selected];
        var policy = rules with
        {
            ViewDistance = (float)distance.Value,
            ModelLimit = modelLimit,
            QueryLimit = modelLimit == 256 ? 768 : 384,
            ShowNames = names.ButtonPressed
        };
        try
        {
            policy.Validate();
        }
        catch (ArgumentException)
        {
            visibility.ShowError("These preview settings exceed the supported limits.");
            return;
        }

        creatures.SetPreview(policy);
        gameObjects.SetPreview(policy);
        if (markCustom)
        {
            visibility.MarkCustom();
        }

        UpdateControlState();
    }

    void UpdateControlState() => limits.Text = $"Up to {creatures.Preview.QueryLimit} nearest rows / {creatures.Preview.ModelLimit} models per spawn layer. Limits also apply to All stored spawns.";

    public void UpdateCounts()
    {
        counts["doodads"].Text = scenery.DoodadCount.ToString("N0");
        counts["wmos"].Text = scenery.WmoCount.ToString("N0");
        counts["creatures"].Text = creatures.ModelCreatureCount.ToString("N0");
        counts["gameobjects"].Text = gameObjects.ModelObjectCount.ToString("N0");
        counts["graveyards"].Text = graveyards.Sites.Count.ToString("N0");
        counts["graveyards"].TooltipText = graveyards.QueryStatus;
        counts["areatriggers"].Text = areaTriggers.Triggers.Count.ToString("N0");
        counts["areatriggers"].TooltipText = areaTriggers.QueryStatus;
        counts["waypoints"].Text = waypoints.Paths.Count.ToString("N0");
        counts["waypoints"].TooltipText = waypoints.QueryStatus;
        counts["creatures"].TooltipText = $"{creatures.Records.Count} SQL matches · {creatures.AdmittedCreatureCount} admitted · {creatures.ModelCreatureCount} native models ready";
        counts["gameobjects"].TooltipText = $"{gameObjects.Records.Count} SQL matches · {gameObjects.AdmittedObjectCount} admitted · {gameObjects.ModelObjectCount} native models ready";
        visibility.SetPhaseCatalog(PhaseCatalog.Merge(creatures.PhaseCatalog, gameObjects.PhaseCatalog));
        visibility.SetEventCatalog(creatures.EventCatalog.Length > 0 ? creatures.EventCatalog : gameObjects.EventCatalog);
    }
}
