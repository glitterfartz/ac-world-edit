using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The cinematics editor of each dialect, chosen in one place.</summary>
public static class CinematicsDialects
{
    public static CinematicsPanel For(WorldDialect dialect) =>
        dialect == WorldDialect.TrinityCore ? new TcCinematicsPanel() : new AcCinematicsPanel();
}
