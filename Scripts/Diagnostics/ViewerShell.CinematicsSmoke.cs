using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --cinematics-smoke: the player-choice editor against the configured TrinityCore world
/// database. Search finds choice 240, its three answer cards and their reward render, a header edit
/// is staged, and undo removes it. Nothing is written; the draft is the throwaway one every smoke
/// run opens.
/// </summary>
public partial class ViewerShell
{
    bool cinematicsSmoke;
    double cinematicsSmokeTime;
    int cinematicsStage;

    void CinematicsSmoke(double delta)
    {
        cinematicsSmokeTime += delta;
        if (cinematicsSmokeTime > 120)
        {
            SmokeFail("CINEMATICS_SMOKE_TIMEOUT stage=" + cinematicsStage + " " + cinematicsEditor.Panel?.SmokeStatus);
            return;
        }

        if (busy)
        {
            return;
        }

        if (!databaseEnabled)
        {
            SmokeFail("CINEMATICS_SMOKE_FAILED no world database connection is configured");
            return;
        }

        if (DatabaseConnection().Dialect != WorldDialect.TrinityCore)
        {
            SmokeFail("CINEMATICS_SMOKE_FAILED cinematics are TrinityCore only; use the retail profile's connection");
            return;
        }

        if (cinematicsStage == 0)
        {
            cinematicsEditor.Open(0);
            cinematicsStage = 1;
            _ = cinematicsEditor.Search("240");
            return;
        }

        if (cinematicsStage == 1)
        {
            if (cinematicsEditor.ResultCount == 0)
            {
                if (cinematicsEditor.Panel.SmokeStatus == "Searching…")
                {
                    return;
                }

                SmokeFail("CINEMATICS_SMOKE_FAILED the choice search found nothing: " + cinematicsEditor.Panel.SmokeStatus);
                return;
            }

            cinematicsStage = 2;
            _ = cinematicsEditor.Load(240);
            return;
        }

        if (cinematicsEditor.Panel is not TcCinematicsPanel panel || panel.PlayerChoice is not { } view)
        {
            SmokeFail("CINEMATICS_SMOKE_FAILED the TrinityCore panel did not open");
            return;
        }

        if (cinematicsStage == 2)
        {
            if (panel.CurrentId != 240)
            {
                if (panel.SmokeStatus.Length > 0)
                {
                    SmokeFail("CINEMATICS_SMOKE_FAILED choice 240 did not open: " + panel.SmokeStatus);
                }

                return; // Load is still running.
            }

            if (view.CardCount != 3)
            {
                SmokeFail($"CINEMATICS_SMOKE_FAILED choice 240 has {view.CardCount} cards, expected 3 ({panel.SmokeDetail}; status='{panel.SmokeStatus}')");
                return;
            }

            if (!view.Positions.SequenceEqual(new[] { 0, 1, 2 }))
            {
                SmokeFail($"CINEMATICS_SMOKE_FAILED choice 240 positions are {string.Join(",", view.Positions)}, expected 0,1,2");
                return;
            }

            if (view.RewardSummary(504) == "No reward")
            {
                SmokeFail("CINEMATICS_SMOKE_FAILED response 504 has no reward");
                return;
            }

            var card = (Button?)view.FindChild("Response_504", true, false);
            if (card == null)
            {
                SmokeFail("CINEMATICS_SMOKE_FAILED the response card was not built");
                return;
            }

            card.EmitSignal(BaseButton.SignalName.Pressed);
            cinematicsStage = 3;
            return;
        }

        if (cinematicsStage == 3)
        {
            var header = (LineEdit?)view.FindChild("Header", true, false);
            if (header == null)
            {
                SmokeFail("CINEMATICS_SMOKE_FAILED the response editor was not built");
                return;
            }

            header.Text = "CINEMATICS_SMOKE_HEADER";
            header.EmitSignal(LineEdit.SignalName.TextSubmitted, "CINEMATICS_SMOKE_HEADER");
            cinematicsStage = 4;
            return;
        }

        var draft = drafts.Draft;
        if (draft == null)
        {
            SmokeFail("CINEMATICS_SMOKE_FAILED the content draft is not open");
            return;
        }

        bool staged = draft.NetFor(panel.SourceKey).Any(e => e.Edit.Table == "playerchoice_response" && e.Edit.After?.T("Header") == "CINEMATICS_SMOKE_HEADER");
        if (cinematicsStage == 4)
        {
            if (!staged)
            {
                SmokeFail("CINEMATICS_SMOKE_FAILED the header edit was not staged: " + panel.SmokeStatus);
                return;
            }

            panel.History(false);
            cinematicsStage = 5;
            return;
        }

        if (staged)
        {
            SmokeFail("CINEMATICS_SMOKE_FAILED undo left the header edit staged");
            return;
        }

        GD.Print($"CINEMATICS_SMOKE_PASSED choice=240 responses={view.CardCount} reward='{view.RewardSummary(504)}' edit+undo");
        GetTree().Quit();
    }
}
