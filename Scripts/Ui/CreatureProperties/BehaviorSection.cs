using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Hosts the shared <see cref="BehaviorPanel"/> (SmartAI rules, AI selection and action lists) for
/// the selected creature of either database. The panel loads and edits its own rows; this section only places it and
/// points it at the creature.
/// </summary>
public sealed partial class BehaviorSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override bool Supports(WorldDialect dialect) => true;

    public override string Id => "behavior";
    public override string Title => "Behavior";
    public override string IconPath => "Interface/Icons/INV_Misc_Gear_01.blp";
    public override bool ScrollsItself => true;

    public override void OnShown(object? argument)
    {
        Host(Ctx.Behavior, this);
        Ctx.EnsureBehaviorOpen();
        Ctx.Behavior.ShowRules();
    }

    public override void Refresh()
    {
    }
}

/// <summary>
/// The creature's speech lines (creature_text): the other view of the shared <see cref="BehaviorPanel"/>.
/// </summary>
public sealed partial class SpeechSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override bool Supports(WorldDialect dialect) => true;

    public override string Id => "speech";
    public override string Title => "Speech";
    public override string IconPath => "Interface/Icons/INV_Misc_Note_01.blp";
    public override bool ScrollsItself => true;

    public override void OnShown(object? argument)
    {
        Host(Ctx.Behavior, this);
        Ctx.EnsureBehaviorOpen();
        Ctx.Behavior.ShowSpeech();
    }

    public override void Refresh()
    {
    }
}
