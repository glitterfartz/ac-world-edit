using ACWorldEdit.Foundation;

namespace ACWorldEdit;

public partial class ViewerShell
{
    /// <summary>The behavior editor body inside the creature properties window, driven by the behavior and content smokes.</summary>
    BehaviorPanel behaviorEditor => creatureProps.Context.Behavior;
}
