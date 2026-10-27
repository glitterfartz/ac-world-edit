using Godot;

namespace ACWorldEdit;

/// <summary>
/// Hosts the shared <see cref="BehaviorPanel"/> (SmartAI rules, AI selection and action lists) for
/// the selected creature. The panel loads and edits its own rows; this section only places it and
/// points it at the creature.
/// </summary>
public sealed partial class AcBehaviorSection(CreatureContext ctx) : CreatureSection(ctx)
{
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

