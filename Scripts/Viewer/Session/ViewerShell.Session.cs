using Godot;

namespace ACWorldEdit;

/// <summary>Delegation surface: the client/map flow lives in <see cref="ViewerSession"/>.</summary>
public partial class ViewerShell
{
    Task LoadClient() => viewerSession.LoadClient();
    Task OpenMap() => viewerSession.OpenMap();
    void ResetTerrain() => viewerSession.ResetTerrain();
}
