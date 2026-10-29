using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// "Who is looking": which phase and difficulty the preview stands in. AzerothCore phases are mask bits, so they
/// get quick buttons, one checkbox per phase and the typed mask underneath; TrinityCore phases are plain IDs.
/// </summary>
public sealed partial class VisibilityPreviewPanel
{
    // TrinityCore difficulty IDs worth previewing; the last entry matches every difficulty.
    static readonly (string Text, int Id)[] TrinityDifficulties =
    [
        ("Open world (0)", 0),
        ("Normal dungeon (1)", 1),
        ("Heroic dungeon (2)", 2),
        ("Mythic dungeon (23)", 23),
        ("Normal raid (14)", 14),
        ("Heroic raid (15)", 15),
        ("Mythic raid (16)", 16),
        ("Any difficulty", SpawnPreview.AnyDifficulty)
    ];

    // Wrath spawn masks; the item ID of each entry is its index here.
    static readonly (string Text, uint Mask)[] AzerothDifficulties =
    [
        ("Normal / 10-player normal", 1),
        ("Heroic / 25-player normal", 2),
        ("10-player heroic", 4),
        ("25-player heroic", 8),
        ("Any difficulty", uint.MaxValue)
    ];

    CheckBox masks = null!;
    OptionButton difficulty = null!;
    LineEdit phase = null!;
    Label phaseCaption = null!, maskReadout = null!;
    HFlowContainer phaseGrid = null!;
    HBoxContainer phaseButtons = null!;
    Button normalWorld = null!, allPhases = null!, noPhase = null!;
    readonly List<CheckBox> phaseBoxes = [];

    bool Trinity => dialect == WorldDialect.TrinityCore;

    void BuildPhaseSection()
    {
        Section("Who is looking");
        masks = new CheckBox
        {
            Text = "Only what this player would see",
            ButtonPressed = true,
            TooltipText = "Off shows every stored spawn, whatever its phase or difficulty."
        };
        AddChild(masks);
        masks.Toggled += _ => Edited();

        phaseButtons = new HBoxContainer();
        AddChild(phaseButtons);
        normalWorld = InspectorUi.Button("Normal world", () => SetMask(AcPhaseMasks.Normal));
        allPhases = InspectorUi.Button("All phases", () => SetMask(AcPhaseMasks.All));
        noPhase = InspectorUi.Button("No phase", () => SetPhaseId(0));
        foreach (var button in new[] { normalWorld, allPhases, noPhase })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            phaseButtons.AddChild(button);
        }

        phaseGrid = new HFlowContainer
        {
            Name = "PhaseChecklist"
        };
        AddChild(phaseGrid);
        for (int number = 1; number <= AcPhaseMasks.Listed; number++)
        {
            int phaseNumber = number;
            var box = new CheckBox
            {
                Text = $"Phase {number}",
                TooltipText = $"Mask value {1u << (number - 1)}" + (number == 1 ? ": the normal world" : ""),
            };
            box.Toggled += on =>
            {
                if (!syncing)
                {
                    SetMask(AcPhaseMasks.With(CurrentMask(), phaseNumber, on));
                }
            };
            phaseGrid.AddChild(box);
            phaseBoxes.Add(box);
        }

        var row = new HBoxContainer();
        AddChild(row);
        phaseCaption = InspectorUi.Text("Mask", 13, true);
        phaseCaption.AutowrapMode = TextServer.AutowrapMode.Off;
        phaseCaption.CustomMinimumSize = new(90, 0);
        row.AddChild(phaseCaption);
        phase = new LineEdit
        {
            Name = "PhaseMask",
            Text = "1",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 12
        };
        row.AddChild(phase);
        phase.TextSubmitted += _ =>
        {
            ShowMaskReadout();
            Edited();
        };
        phase.TextChanged += _ =>
        {
            if (!syncing)
            {
                ShowMaskReadout();
                EditedSoon();
            }
        };
        maskReadout = InspectorUi.Text("", 12, true);
        AddChild(maskReadout);

        var difficultyRow = new HBoxContainer();
        AddChild(difficultyRow);
        var difficultyCaption = InspectorUi.Text("Difficulty", 13, true);
        difficultyCaption.AutowrapMode = TextServer.AutowrapMode.Off;
        difficultyCaption.CustomMinimumSize = new(90, 0);
        difficultyRow.AddChild(difficultyCaption);
        difficulty = new OptionButton
        {
            Name = "Difficulty",
            FitToLongestItem = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        difficultyRow.AddChild(difficulty);
        difficulty.ItemSelected += _ => Edited();
    }

    /// <summary>Show the controls that fit the open server and relabel the typed field.</summary>
    void ShowDialectControls()
    {
        phaseCaption.Text = Trinity ? "Phase ID" : "Mask";
        phase.PlaceholderText = Trinity ? "0 for none" : "1 or 0x1";
        phase.TooltipText = Trinity
            ? "The phase to preview. 0 shows only spawns outside any phase; a phase ID also shows that phase's spawns."
            : "Advanced: the mask as a number. Decimal and 0x hex are accepted; 3 means phase 1 and phase 2.";
        normalWorld.Visible = allPhases.Visible = phaseGrid.Visible = !Trinity;
        noPhase.Visible = Trinity;
        difficulty.Clear();
        if (Trinity)
        {
            foreach (var (text, id) in TrinityDifficulties)
            {
                difficulty.AddItem(text, id);
            }
        }
        else
        {
            for (int i = 0; i < AzerothDifficulties.Length; i++)
            {
                difficulty.AddItem(AzerothDifficulties[i].Text, i);
            }
        }
    }

    /// <summary>Show a policy's phase and difficulty.</summary>
    void ShowPhase(SpawnPreview value)
    {
        masks.ButtonPressed = value.RespectMasks;
        phase.Text = Trinity ? value.PhaseId.ToString() : value.PhaseMask.ToString();
        difficulty.Select(Trinity
            ? Math.Max(0, Array.FindIndex(TrinityDifficulties, d => d.Id == value.DifficultyId))
            : Math.Max(0, Array.FindIndex(AzerothDifficulties, d => d.Mask == value.SpawnMask)));
        ShowMaskReadout();
    }

    uint CurrentMask() => AcPhaseMasks.TryParse(phase.Text, out uint mask) ? mask : AcPhaseMasks.Normal;

    void SetMask(uint mask)
    {
        phase.Text = mask.ToString();
        ShowMaskReadout();
        Edited();
    }

    void SetPhaseId(uint id)
    {
        phase.Text = id.ToString();
        Edited();
    }

    /// <summary>Keep the checklist and the sentence under the field in step with the typed mask.</summary>
    void ShowMaskReadout()
    {
        if (Trinity)
        {
            maskReadout.Text = "";
            return;
        }

        bool valid = AcPhaseMasks.TryParse(phase.Text, out uint mask);
        maskReadout.Text = valid ? "= " + AcPhaseMasks.Describe(mask) + (mask == 0 ? ": nothing will show" : "") : "Not a mask yet. Use a number such as 1, 3 or 0x3.";
        bool wasSyncing = syncing;
        syncing = true;
        for (int i = 0; i < phaseBoxes.Count; i++)
        {
            phaseBoxes[i].ButtonPressed = valid && AcPhaseMasks.Has(mask, i + 1);
        }

        syncing = wasSyncing;
    }

    /// <summary>The phase and difficulty rules the controls hold, or the reason they are not valid.</summary>
    bool TryReadPhase(out SpawnPreview rules, out string error)
    {
        rules = new SpawnPreview();
        error = "";
        string text = phase.Text.Trim();
        uint value;
        if (Trinity)
        {
            if (!uint.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                error = "Enter a valid phase ID (0 for none).";
                return false;
            }
        }
        else if (!AcPhaseMasks.TryParse(text, out value))
        {
            error = "Enter a valid phase mask (for example 1, 3 or 0x3).";
            return false;
        }

        rules = new SpawnPreview
        {
            RespectMasks = masks.ButtonPressed,
            PhaseMask = Trinity ? 1 : value,
            PhaseId = Trinity ? value : 0,
            DifficultyId = Trinity ? difficulty.GetItemId(difficulty.Selected) : 0,
            SpawnMask = Trinity ? 1 : AzerothDifficulties[difficulty.GetItemId(difficulty.Selected)].Mask
        };
        return true;
    }

    void UpdateEnabled()
    {
        bool on = masks.ButtonPressed;
        phase.Editable = on;
        difficulty.Disabled = !on;
        normalWorld.Disabled = allPhases.Disabled = noPhase.Disabled = !on;
        foreach (var box in phaseBoxes)
        {
            box.Disabled = !on;
        }
    }
}
