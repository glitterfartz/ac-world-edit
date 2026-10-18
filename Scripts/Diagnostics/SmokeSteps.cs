using Godot;

namespace ACWorldEdit;

/// <summary>
/// Runs an ordered list of smoke steps one frame at a time: a per-step and a total timeout that
/// name the stuck step, a frame pause after each step, a pass line when the list completes, and
/// failure reporting for exceptions. A step returns true when it is done and false to be polled
/// again next frame. Steps may set <see cref="Wait"/> to pause longer and read <see cref="StepTime"/>.
/// </summary>
internal sealed class SmokeSteps(string tag, double stepTimeout, double totalTimeout, int postStepFrames)
{
    int index;
    double time;

    /// <summary>Seconds spent on the current step.</summary>
    public double StepTime { get; private set; }

    /// <summary>Frames to skip before the next step runs.</summary>
    public int Wait { get; set; }

    /// <summary>
    /// Advance one frame. <paramref name="ready"/> gates running a step (the viewer is idle and a
    /// world is loaded). <paramref name="where"/> adds context to failures; timeouts and the periodic
    /// waiting line may use their own context.
    /// </summary>
    public void Advance(double delta, IReadOnlyList<(string Name, Func<bool> Step)> steps, bool ready, Func<string> where,
        Action<string> fail, Action pass, Func<string>? timeoutWhere = null, Func<string>? waiting = null)
    {
        time += delta;
        StepTime += delta;
        if (StepTime > stepTimeout || time > totalTimeout)
        {
            fail($"{tag}_SMOKE_TIMEOUT step={steps[Math.Min(index, steps.Count - 1)].Name} {(timeoutWhere ?? where)()}");
            return;
        }

        if (Wait > 0)
        {
            Wait--;
            return;
        }

        if (!ready)
        {
            return;
        }

        if (index >= steps.Count)
        {
            GD.Print($"{tag}_SMOKE_PASSED");
            pass();
            return;
        }

        var (name, step) = steps[index];
        if (waiting != null && (int)(StepTime / 5) != (int)((StepTime - delta) / 5))
        {
            GD.Print($"{tag}_SMOKE_WAITING {name} {StepTime:F0}s {waiting()}");
        }

        try
        {
            if (step())
            {
                GD.Print($"{tag}_SMOKE_STEP {name}");
                index++;
                StepTime = 0;
                Wait = postStepFrames;
            }
        }
        catch (Exception error)
        {
            fail($"{tag}_SMOKE_FAILED {name}: {error.Message} {where()}");
        }
    }
}
