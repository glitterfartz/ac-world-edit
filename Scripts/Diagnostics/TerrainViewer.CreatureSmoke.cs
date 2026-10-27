using ACWorldEdit.Foundation;

namespace ACWorldEdit;

public partial class TerrainViewer
{
    /// <summary>The behavior editor body inside the creature properties window, driven by the behavior and content smokes.</summary>
    AcBehaviorPanel behaviorEditor => creatureProps.Context.Behavior;
}
