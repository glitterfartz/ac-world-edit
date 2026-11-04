using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The quest editor of each dialect, chosen in one place.</summary>
public static class QuestDialects
{
    public static QuestPanel For(WorldDialect dialect) =>
        dialect == WorldDialect.TrinityCore ? new TcQuestPanel() : new AcQuestPanel();
}
