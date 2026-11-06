namespace ACWorldEdit.Foundation;

/// <summary>
/// Names of the `gameobject_template.type` values, per dialect. The names and fields live in
/// the generated <see cref="GameObjectTypeCatalog"/> (AzerothCore has types 0-35, TrinityCore
/// master has up to 66; type 29 is a capture point on AzerothCore and a control zone on
/// TrinityCore). Use <see cref="For"/> when the dialect is known.
/// </summary>
public static class GameObjectTypes
{
    public static GameObjectTypeCatalog For(WorldDialect dialect) => GameObjectTypeCatalog.For(dialect);

    /// <summary>The highest type with a name in this dialect.</summary>
    public static int MaxNamed(WorldDialect dialect) => For(dialect).MaxType;

    public static string Name(WorldDialect dialect, int type) => For(dialect).Name(type);
}
