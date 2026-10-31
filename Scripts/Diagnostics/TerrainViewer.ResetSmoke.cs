using Godot;

namespace ACWorldEdit;

/// <summary>
/// --reset-smoke: runs the File → Reset configs and delete cache… action without its dialog, so a
/// harness can check from outside that the user data folder is gone after the viewer exits. It
/// deletes the whole user folder, so it only runs when ACWORLD_RESET_SMOKE=1 is set, and the
/// harness points APPDATA at a throwaway folder first. Prints RESET_SMOKE_SCHEDULED, then quits.
/// </summary>
public partial class TerrainViewer
{
    async Task ResetSmoke()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (System.Environment.GetEnvironmentVariable("ACWORLD_RESET_SMOKE") != "1")
        {
            SmokeFail("RESET_SMOKE_REFUSED: set ACWORLD_RESET_SMOKE=1 (and point APPDATA at a throwaway folder); this deletes the user data folder.");
            return;
        }

        string folder = OS.GetUserDataDir();
        GD.Print($"RESET_SMOKE_TARGET {folder}");
        if (!await BeginReset(folder))
        {
            SmokeFail("RESET_SMOKE_FAILED: the cleanup did not start.");
            return;
        }

        GD.Print("RESET_SMOKE_SCHEDULED");
    }
}
