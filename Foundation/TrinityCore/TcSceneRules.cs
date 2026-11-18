namespace ACWorldEdit.Foundation;

/// <summary>
/// TrinityCore scene rules: the template table and a complete row for a new scene. The script steps
/// are client SceneScriptPackage data, so the checks take an optional package lookup.
/// </summary>
public static class TcSceneRules
{
    public static readonly TableSchema[] Tables = [TcTables.SceneTemplate];

    /// <summary>A scene with no package chosen yet and no flags.</summary>
    public static Row NewScene(uint id) =>
        new Row(TcTables.SceneTemplate.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Kind == ColumnKind.Text ? "" : "0")))
            .With("SceneId", id);

    /// <summary>
    /// Loader checks for one scene. <paramref name="packageExists"/> answers whether the client has
    /// the named SceneScriptPackage; null skips that check when the client is unavailable.
    /// </summary>
    public static IEnumerable<InteractionIssue> Check(uint id, RowSet rows, Func<uint, bool>? packageExists)
    {
        if (rows.Get(TcTables.SceneTemplate, id.ToString()) is not { } scene)
        {
            yield break;
        }

        uint package = scene.U("ScriptPackageID");
        if (package == 0)
        {
            yield return new(false, "No script package chosen; the scene plays nothing.");
        }
        else if (packageExists != null && !packageExists(package))
        {
            yield return new(true, $"Script package {package} does not exist in the client.");
        }
    }
}
