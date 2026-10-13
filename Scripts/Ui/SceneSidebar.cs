using System.Globalization;
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
    readonly WorldObjectScene scenery;
    TabContainer tabs = null!;
    OptionButton preset = null!, eventMode = null!, difficulty = null!, budget = null!;
    LineEdit phase = null!;
    CheckBox masks = null!, helpers = null!, pools = null!, conditional = null!, scripted = null!, despawned = null!, names = null!;
    MenuButton eventPicker = null!;
    HSlider distance = null!;
    Label validation = null!, distanceText = null!, limits = null!;
    readonly Dictionary<string, Label> counts = [];
    readonly HashSet<int> selectedEvents = [];
    string eventSignature = "";
    bool syncing;
    long previewChanged;
    bool previewPending;
    public bool DoodadsEnabled { get; private set; } = true;
    public bool WmosEnabled { get; private set; } = true;

    public SceneSidebar(CreatureOverlay creatures, GameObjectOverlay gameObjects, WorldObjectScene scenery)
    {
        this.creatures = creatures;
        this.gameObjects = gameObjects;
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
        root.AddChild(InspectorUi.Text("VISIBILITY  /  SELECTION  /  LOCAL DRAFTS", 10, true));
        tabs = new TabContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(tabs);
        var creaturePage = creatures.BuildPanel();
        var objectPage = gameObjects.BuildPanel();
        tabs.AddChild(BuildScenePage());
        tabs.AddChild(creaturePage);
        tabs.AddChild(objectPage);
        creatures.SelectionChanged += () =>
        {
            gameObjects.Deselect();
            tabs.CurrentTab = 1;
        };
        gameObjects.SelectionChanged += () =>
        {
            creatures.Deselect();
            tabs.CurrentTab = 2;
        };
    }

    public void ShowSceneTab() => tabs.CurrentTab = 0;
    Control BuildScenePage()
    {
        var page = InspectorUi.Page("Scene", out var scroll);
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
        layers.AddChild(InspectorUi.Text("Hidden layers stop queuing models and spawn queries.", 11, true));
        var preview = InspectorUi.Card(page, "Visibility preview");
        preset = new OptionButton
        {
            Name = "VisibilityPreset",
            FitToLongestItem = false
        };
        preset.AddItem("Normal preview");
        preset.AddItem("All stored spawns");
        preset.AddItem("Custom preview");
        preview.AddChild(preset);
        preset.ItemSelected += index =>
        {
            if (index == 2)
            {
                return;
            }

            SyncPolicy(index == 1 ? SpawnPreview.AllStored : new SpawnPreview());
            ApplyFilters(false);
        };
        preview.AddChild(InspectorUi.Text("Database rules only. Core scripts, live pools and player state can change what appears in game.", 12, true));
        masks = Toggle(preview, "Match phase & difficulty", true);
        var phaseRow = new HBoxContainer();
        preview.AddChild(phaseRow);
        var phaseCaption = InspectorUi.Text("Phase mask", 13, true);
        phaseCaption.AutowrapMode = TextServer.AutowrapMode.Off;
        phaseCaption.CustomMinimumSize = new(90, 0);
        phaseRow.AddChild(phaseCaption);
        phase = new LineEdit
        {
            Name = "PhaseMask",
            Text = "1",
            PlaceholderText = "1 or 0x1",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 12
        };
        phaseRow.AddChild(phase);
        phase.TextSubmitted += _ => ApplyFilters();
        phase.TextChanged += _ => DebounceFilters();
        phase.TooltipText = "Bitmask: 3 includes phase 1 OR phase 2. Decimal and 0x hex are accepted. This does not change the database.";
        difficulty = new OptionButton
        {
            FitToLongestItem = false
        };
        foreach (string text in new[]
        {
            "Normal / 10-player normal",
            "Heroic / 25-player normal",
            "10-player heroic",
            "25-player heroic",
            "Any difficulty"
        }

        )
        {
            difficulty.AddItem(text);
        }

        preview.AddChild(difficulty);
        difficulty.ItemSelected += _ => ApplyFilters();
        preview.AddChild(InspectorUi.Text("Events", 12, true));
        eventMode = new OptionButton
        {
            Name = "EventPreview",
            FitToLongestItem = false
        };
        foreach (string text in new[]
        {
            "SQL schedule",
            "No events",
            "Selected events",
            "All event spawns"
        }

        )
        {
            eventMode.AddItem(text);
        }

        preview.AddChild(eventMode);
        eventMode.ItemSelected += _ => ApplyFilters();
        eventPicker = new MenuButton
        {
            Name = "EventPicker",
            Text = "Choose events…",
            CustomMinimumSize = new(0, 32)
        };
        preview.AddChild(eventPicker);
        var menu = eventPicker.GetPopup();
        menu.HideOnCheckableItemSelection = false;
        menu.MaxSize = new(380, 500);
        menu.IdPressed += id =>
        {
            int key = (int)id;
            if (!selectedEvents.Contains(key) && selectedEvents.Count >= 128)
            {
                validation.Text = "Select up to 128 events, or use All event spawns.";
                return;
            }

            if (!selectedEvents.Add(key))
            {
                selectedEvents.Remove(key);
            }

            int index = menu.GetItemIndex(key);
            menu.SetItemChecked(index, selectedEvents.Contains(key));
            ApplyFilters();
        };
        eventPicker.TooltipText = "Preview chosen event IDs independently of their SQL dates. No event is started on the server.";
        helpers = Toggle(preview, "Hidden helpers & ghost-only", false);
        pools = Toggle(preview, "Pool candidates", false);
        pools.TooltipText = "Includes all pool alternatives. The editor cannot know which random member the live core selected.";
        conditional = Toggle(preview, "SQL conditional spawns", false);
        conditional.TooltipText = "Includes respawn/visibility conditions without pretending to evaluate player quest, aura or world state.";
        scripted = Toggle(preview, "Script-bound spawns", true);
        scripted.TooltipText = "Filters SQL ScriptName / AIName bindings only. A binding does not mean invisible; scripts without a SQL binding cannot be detected.";
        despawned = Toggle(preview, "Initially despawned GameObjects", false);
        validation = InspectorUi.Text("", 12, true);
        preview.AddChild(validation);
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
        var advanced = new VBoxContainer
        {
            Visible = false
        };
        connection.AddChild(InspectorUi.Button("Database connection…", () => advanced.Visible = !advanced.Visible));
        connection.AddChild(advanced);
        advanced.AddChild(InspectorUi.Text("External worldserver.conf", 12, true));
        advanced.AddChild(creatures.ConnectionControl);
        advanced.AddChild(InspectorUi.Button("Apply & refresh", () =>
        {
            creatures.Refresh();
            gameObjects.Refresh();
        }));
        connection.AddChild(InspectorUi.Text("Read-only connection. Credentials stay in the external file.", 11, true));
        SyncPolicy(new SpawnPreview());
        UpdateControlState();
        return scroll;
    }

    void Layer(VBoxContainer parent, string name, string key, Action<bool> action)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        var toggle = new CheckButton
        {
            Name = key,
            Text = name,
            ButtonPressed = true,
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
        check.Toggled += _ =>
        {
            if (!syncing)
            {
                ApplyFilters();
            }
        };
        return check;
    }

    void ApplyScenery() => scenery.SetLayers(DoodadsEnabled, WmosEnabled);
    void DebounceFilters()
    {
        if (syncing)
        {
            return;
        }

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

    void SyncPolicy(SpawnPreview value)
    {
        syncing = true;
        masks.ButtonPressed = value.RespectMasks;
        phase.Text = value.PhaseMask.ToString();
        difficulty.Select(0);
        eventMode.Select((int)value.Events);
        helpers.ButtonPressed = value.IncludeHelpers;
        pools.ButtonPressed = value.IncludePools;
        conditional.ButtonPressed = value.IncludeConditional;
        scripted.ButtonPressed = value.IncludeScripted;
        despawned.ButtonPressed = value.IncludeDespawned;
        syncing = false;
    }

    /// <summary>Validate masks before replacing the immutable policy; invalid input leaves the previous preview intact.</summary>
    void ApplyFilters(bool markCustom = true)
    {
        if (syncing)
        {
            return;
        }

        previewPending = false;
        string text = phase.Text.Trim();
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!uint.TryParse(hex ? text[2..] : text, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out uint phaseMask))
        {
            validation.Text = "Enter a valid phase mask (for example 1, 3 or 0x3).";
            return;
        }

        validation.Text = "";
        int modelLimit = new[]
        {
            64,
            128,
            256
        }[budget.Selected];
        var policy = new SpawnPreview
        {
            RespectMasks = masks.ButtonPressed,
            PhaseMask = phaseMask,
            SpawnMask = new uint[]
            {
                1,
                2,
                4,
                8,
                uint.MaxValue
            }[difficulty.Selected],
            Events = (EventPreviewMode)eventMode.Selected,
            ActiveEvents = selectedEvents.Order().ToArray(),
            IncludeHelpers = helpers.ButtonPressed,
            IncludePools = pools.ButtonPressed,
            IncludeConditional = conditional.ButtonPressed,
            IncludeScripted = scripted.ButtonPressed,
            IncludeDespawned = despawned.ButtonPressed,
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
            validation.Text = "These preview settings exceed the supported limits.";
            return;
        }

        creatures.SetPreview(policy);
        gameObjects.SetPreview(policy);
        if (markCustom)
        {
            preset.Select(2);
        }

        UpdateControlState();
    }

    void UpdateControlState()
    {
        phase.Editable = masks.ButtonPressed;
        difficulty.Disabled = !masks.ButtonPressed;
        eventPicker.Disabled = eventMode.Selected != (int)EventPreviewMode.Selected || eventSignature.Length == 0;
        eventPicker.Text = selectedEvents.Count == 0 ? "Choose events…" : $"{selectedEvents.Count} events selected";
        limits.Text = $"Up to {creatures.Preview.QueryLimit} nearest rows / {creatures.Preview.ModelLimit} models per spawn layer. Limits also apply to All stored spawns.";
    }

    public void UpdateCounts()
    {
        counts["doodads"].Text = scenery.DoodadCount.ToString("N0");
        counts["wmos"].Text = scenery.WmoCount.ToString("N0");
        counts["creatures"].Text = creatures.ModelCreatureCount.ToString("N0");
        counts["gameobjects"].Text = gameObjects.ModelObjectCount.ToString("N0");
        counts["creatures"].TooltipText = $"{creatures.Records.Count} SQL matches · {creatures.AdmittedCreatureCount} admitted · {creatures.ModelCreatureCount} native models ready";
        counts["gameobjects"].TooltipText = $"{gameObjects.Records.Count} SQL matches · {gameObjects.AdmittedObjectCount} admitted · {gameObjects.ModelObjectCount} native models ready";
        var catalog = creatures.EventCatalog.Length > 0 ? creatures.EventCatalog : gameObjects.EventCatalog;
        string signature = string.Join(',', catalog.Select(e => e.Id));
        if (signature != eventSignature && !eventPicker.GetPopup().Visible)
        {
            eventSignature = signature;
            var menu = eventPicker.GetPopup();
            menu.Clear();
            foreach (var entry in catalog)
            {
                menu.AddCheckItem($"{entry.Id}  ·  {entry.Name}", entry.Id);
                menu.SetItemChecked(menu.ItemCount - 1, selectedEvents.Contains(entry.Id));
            }

            UpdateControlState();
        }
    }
}
