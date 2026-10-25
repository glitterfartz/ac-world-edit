using Godot;

namespace ACWorldEdit;

/// <summary>
/// The creature's speech lines (creature_text): the other view of the shared <see cref="BehaviorPanel"/>.
/// </summary>
public sealed partial class AcSpeechSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override string Id => "speech";
    public override string Title => "Speech";
    public override string IconPath => "Interface/Icons/INV_Misc_Note_01.blp";
    public override bool ScrollsItself => true;

    public override void OnShown(object? argument)
    {
        AcBehaviorSection.Host(Ctx, this);
        Ctx.EnsureBehaviorOpen();
        Ctx.Behavior.ShowSpeech();
    }

    public override void Refresh()
    {
    }
}
