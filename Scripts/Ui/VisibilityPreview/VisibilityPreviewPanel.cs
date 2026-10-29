using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The "Visibility preview" card: a preset, one sentence saying what it shows, then three titled sections.
/// Who is looking (phase and difficulty, in <c>VisibilityPreviewPanel.Phase.cs</c>), events (<c>.Events.cs</c>)
/// and what else counts as visible. It builds only the visibility rules; the sidebar adds the display budget.
/// Controls never alter source rows.
/// </summary>
public sealed partial class VisibilityPreviewPanel : VBoxContainer
{
    const int NormalPreset = 0, AllStoredPreset = 1, CustomPreset = 2;

    static readonly string[] PresetNotes =
    [
        "What a player in the normal world sees: the default phase and difficulty, events on their database schedule, no helpers or pool alternatives.",
        "Every row in the database, whatever its phase, difficulty, event or condition.",
        "Your own settings."
    ];

    /// <summary>Raised when a control changed the rules; the flag says whether the preset should now read "Custom".</summary>
    public event Action<bool>? Changed;

    WorldDialect dialect = WorldDialect.AzerothCore;
    bool syncing;
    long changedAt;
    bool pending;
    OptionButton preset = null!;
    Label presetNote = null!, summary = null!, validation = null!;
    Button reset = null!;
    CheckBox helpers = null!, pools = null!, conditional = null!, scripted = null!, despawned = null!;

    public VisibilityPreviewPanel()
    {
        Name = "VisibilityPreview";
        BuildPreset();
        BuildPhaseSection();
        BuildEventsSection();
        BuildIncludeSection();
        validation = InspectorUi.Text("", 12, true);
        AddChild(validation);
        ShowDialectControls();
        Sync(new SpawnPreview());
    }

    void BuildPreset()
    {
        preset = new OptionButton
        {
            Name = "VisibilityPreset",
            FitToLongestItem = false
        };
        preset.AddItem("Normal world");
        preset.AddItem("Everything stored");
        preset.AddItem("Custom");
        preset.SetItemDisabled(CustomPreset, true);
        preset.TooltipText = "Normal world and Everything stored are starting points. Changing any control below makes it Custom.";
        AddChild(preset);
        preset.ItemSelected += index =>
        {
            Sync(index == AllStoredPreset ? SpawnPreview.AllStored with { Dialect = dialect } : new SpawnPreview { Dialect = dialect });
            Changed?.Invoke(false);
        };
        presetNote = InspectorUi.Text(PresetNotes[NormalPreset], 12, true);
        AddChild(presetNote);
        reset = InspectorUi.Button("Reset to normal world", () =>
        {
            Sync(new SpawnPreview { Dialect = dialect });
            Changed?.Invoke(false);
        });
        reset.Visible = false;
        AddChild(reset);
        summary = InspectorUi.Text("", 13);
        summary.Name = "VisibilitySummary";
        AddChild(summary);
    }

    void BuildIncludeSection()
    {
        Section("Also show");
        helpers = Toggle("Invisible helper creatures", false, "Creatures the server hides from players: triggers, ghost-only and other helpers.");
        pools = Toggle("Pool alternatives", false, "All members of a spawn pool. The editor cannot know which random member the live core picked.");
        conditional = Toggle("Spawns with conditions", false, "Spawns the server shows or hides by quest, aura or world state. The editor lists them without evaluating the player's state.");
        scripted = Toggle("Script-controlled spawns", true, "Spawns with a ScriptName or AIName in the database. A script does not mean the spawn is invisible, and scripts without a database binding cannot be detected.");
        despawned = Toggle("Objects that start despawned", false, "GameObjects stored as initially despawned.");
    }

    void Section(string title)
    {
        AddChild(new HSeparator());
        var label = InspectorUi.Text(title, 13);
        label.AddThemeColorOverride("font_color", InspectorUi.Accent);
        AddChild(label);
    }

    CheckBox Toggle(string text, bool initial, string tooltip)
    {
        var check = new CheckBox
        {
            Text = text,
            ButtonPressed = initial,
            TooltipText = tooltip
        };
        AddChild(check);
        check.Toggled += _ => Edited();
        return check;
    }

    /// <summary>A control changed the rules: apply them now.</summary>
    void Edited()
    {
        if (!syncing)
        {
            pending = false;
            Changed?.Invoke(true);
        }
    }

    /// <summary>A control changed while the user may still be typing: apply the rules shortly.</summary>
    void EditedSoon()
    {
        if (!syncing)
        {
            pending = true;
            changedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    public override void _Process(double delta)
    {
        if (pending && System.Diagnostics.Stopwatch.GetElapsedTime(changedAt).TotalSeconds >= 0.3)
        {
            Edited();
        }
    }

    /// <summary>Switch to the open client's server: phase masks for AzerothCore, phase and difficulty IDs for TrinityCore. The preview restarts from that server's defaults.</summary>
    public void SetDialect(WorldDialect value)
    {
        if (dialect == value)
        {
            return;
        }

        dialect = value;
        ShowDialectControls();
        Sync(new SpawnPreview { Dialect = dialect });
    }

    /// <summary>Put every control to a policy's values without raising <see cref="Changed"/>.</summary>
    void Sync(SpawnPreview value)
    {
        syncing = true;
        pending = false;
        ShowPhase(value);
        SyncEvents(value);
        helpers.ButtonPressed = value.IncludeHelpers;
        pools.ButtonPressed = value.IncludePools;
        conditional.ButtonPressed = value.IncludeConditional;
        scripted.ButtonPressed = value.IncludeScripted;
        despawned.ButtonPressed = value.IncludeDespawned;
        syncing = false;
        preset.Select(value.RespectMasks ? NormalPreset : AllStoredPreset);
        ShowPresetNote();
        validation.Text = "";
        Build();
    }

    /// <summary>The sidebar applied a change the user made: the preset now reads "Custom".</summary>
    public void MarkCustom()
    {
        preset.Select(CustomPreset);
        ShowPresetNote();
    }

    void ShowPresetNote()
    {
        presetNote.Text = PresetNotes[preset.Selected];
        reset.Visible = preset.Selected == CustomPreset;
    }

    /// <summary>The visibility rules the controls hold, or null (with the reason shown) when a value is not valid. Also refreshes the summary sentence.</summary>
    public SpawnPreview? Build()
    {
        if (!TryReadPhase(out var phaseRules, out string error))
        {
            validation.Text = error;
            return null;
        }

        validation.Text = "";
        var rules = phaseRules with
        {
            Dialect = dialect,
            Events = EventMode,
            ActiveEvents = selectedEvents.Order().ToArray(),
            IncludeHelpers = helpers.ButtonPressed,
            IncludePools = pools.ButtonPressed,
            IncludeConditional = conditional.ButtonPressed,
            IncludeScripted = scripted.ButtonPressed,
            IncludeDespawned = despawned.ButtonPressed
        };
        summary.Text = PreviewSummary.Describe(rules);
        UpdateEnabled();
        return rules;
    }

    public void ShowError(string message) => validation.Text = message;
}
