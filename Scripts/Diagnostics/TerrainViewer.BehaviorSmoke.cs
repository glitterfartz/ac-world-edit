using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --behavior-smoke (graphical): behavior editor interaction checks on an isolated draft,
/// driven by real mouse and key events at the controls' screen positions.
/// </summary>
public partial class TerrainViewer
{
    bool behaviorSmoke;
    int behaviorStep, behaviorWait;
    double behaviorTime, behaviorStepTime;
    List<(string Name, Func<bool> Step)>? behaviorSteps;
    int behaviorRuleCount;

    void BehaviorSmoke(double delta)
    {
        shotPrefix = "behavior";
        behaviorTime += delta;
        behaviorStepTime += delta;
        behaviorSteps ??= BehaviorSteps();
        TakeScreenshot();
        if (shot != null)
        {
            return;
        }

        string Where() => $"status='{behaviorEditor.SmokeStatus}' scope={behaviorEditor.Scope}";
        if (behaviorStepTime > 40 || behaviorTime > 240)
        {
            SmokeFail($"BEHAVIOR_SMOKE_TIMEOUT step={behaviorSteps[Math.Min(behaviorStep, behaviorSteps.Count - 1)].Name} {Where()}");
            return;
        }

        if (behaviorWait > 0)
        {
            behaviorWait--;
            return;
        }

        if (busy || world == null)
        {
            return;
        }

        if (behaviorStep >= behaviorSteps.Count)
        {
            GD.Print("BEHAVIOR_SMOKE_PASSED");
            GetTree().Quit();
            return;
        }

        var (name, step) = behaviorSteps[behaviorStep];
        try
        {
            if (step())
            {
                GD.Print($"BEHAVIOR_SMOKE_STEP {name} {Where()}");
                behaviorStep++;
                behaviorStepTime = 0;
                behaviorWait = 4;
            }
        }
        catch (Exception error)
        {
            SmokeFail($"BEHAVIOR_SMOKE_FAILED {name}: {error.Message} {Where()}");
        }
    }

    /// <summary>
    /// A left click at a control's center through the normal input path (hover, press, release).
    /// </summary>
    static void ClickControl(Control control)
    {
        if (!Revealed(control))
        {
            throw new InvalidOperationException($"{control.Name} is scrolled out of view; reveal it first");
        }

        var at = control.GetScreenTransform() * (control.Size / 2);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    /// <summary>
    /// Whether a control lies inside its scroll view; if not, scroll to it (positions update on the
    /// next layout pass, so callers retry next frame) like a user scrolling before a click.
    /// </summary>
    static bool Revealed(Control control)
    {
        for (var parent = control.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer scroll)
            {
                if (scroll.GetGlobalRect().Encloses(control.GetGlobalRect()))
                {
                    return true;
                }

                GD.Print($"BEHAVIOR_SMOKE_REVEAL {control.Name} rect={control.GetGlobalRect()} scroll={scroll.GetGlobalRect()} v={scroll.ScrollVertical} max={scroll.GetVScrollBar().MaxValue} page={scroll.GetVScrollBar().Page} window={scroll.GetParent<Control>()?.GetParent<Control>()?.Size}");
                scroll.EnsureControlVisible(control);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Click a behavior page control by node name once it is scrolled into view.
    /// </summary>
    bool ClickPage(string name)
    {
        var control = behaviorEditor.SmokeControl(name) ?? throw new InvalidOperationException($"no {name} control");
        if (!Revealed(control))
        {
            return false;
        }

        ClickControl(control);
        return true;
    }

    static void PressKey(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    SmartScope BehaviorScope => behaviorEditor.Scope!.Value;

    List<(string, Func<bool>)> BehaviorSteps() =>
    [
        ("open", () =>
        {
            behaviorEditor.Open(SmokeInnkeeper, 0);
            return true;
        }),
        ("loaded", () =>
        {
            if (behaviorEditor.IsLoading || behaviorEditor.Scope == null || behaviorEditor.SmokeControl("AddFromTemplate") == null)
            {
                return false;
            }

            behaviorRuleCount = behaviorEditor.SmokeRules(BehaviorScope).Count;
            GD.Print($"BEHAVIOR_SMOKE_START rules={behaviorRuleCount}");
            return true;
        }),
        ("click-templates", () => ClickPage("AddFromTemplate")),
        ("pick-template", () => ClickPickerItem(1)),
        ("template-pending", () =>
        {
            Require(behaviorEditor.SmokeControl("PendingRule") != null, "template click did not open the pending card");
            Require(behaviorEditor.SmokeRules(BehaviorScope).Count == behaviorRuleCount, "template drafted a rule before Add");
            return true;
        }),
        ("click-template-add", () => ClickPage("PendingAdd")),
        ("template-added", () =>
        {
            int count = behaviorEditor.SmokeRules(BehaviorScope).Count;
            Require(count == behaviorRuleCount + 1, $"template Add added {count - behaviorRuleCount} rule(s)");
            behaviorRuleCount = count;
            return true;
        }),
        ("click-add", () => ClickPage("AddRule")),
        ("click-composer-add", () =>
        {
            Require(behaviorEditor.SmokeControl("PendingAdd") != null, "pending card did not open");
            return ClickPage("PendingAdd");
        }),
        ("composer-added", () =>
        {
            int count = behaviorEditor.SmokeRules(BehaviorScope).Count;
            Require(count == behaviorRuleCount + 1, $"blank rule Add added {count - behaviorRuleCount} rule(s)");
            behaviorRuleCount = count;
            return true;
        }),
        ("edit-focused-field", () =>
        {
            // Expanded rule with an edited, still-focused field (the usual state after adding).
            var edits = behaviorEditor.SmokeControls<LineEdit>().ToList();
            GD.Print($"BEHAVIOR_SMOKE_FIELDS lineedits={edits.Count}");
            var note = edits.Last(e => e.PlaceholderText == "Note for other editors");
            if (!Revealed(note))
            {
                return false;
            }

            ClickControl(note);
            note.Text += " probe";
            return true;
        }),
        ("click-templates-focused", () => ClickPage("AddFromTemplate")),
        ("pick-template-mouse", () => ClickPickerItem(2)),
        ("template-pending-focused", () =>
        {
            Require(behaviorEditor.SmokeControl("PendingRule") != null, "template did not open after a focused field committed");
            return true;
        }),
        ("click-template-add-2", () => ClickPage("PendingAdd")),
        ("template-mouse-added", () =>
        {
            int count = behaviorEditor.SmokeRules(BehaviorScope).Count;
            Require(count == behaviorRuleCount + 1, $"mouse template added {count - behaviorRuleCount} rule(s)");
            return true;
        })
    ];

    /// <summary>
    /// Click a row of the open picker with the mouse once its list is filled; false until then.
    /// </summary>
    bool ClickPickerItem(int index)
    {
        var list = behaviorEditor.GetChildren().OfType<PickerPopup>().LastOrDefault(p => p.Visible)?.FindChildren("*", "ItemList", true, false).OfType<ItemList>().FirstOrDefault();
        if (list == null || list.ItemCount <= index)
        {
            return false;
        }

        var at = list.GetScreenTransform() * (list.GetItemRect(index).GetCenter());
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        return true;
    }
}
