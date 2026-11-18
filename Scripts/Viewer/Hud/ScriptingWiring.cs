using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit;

/// <summary>
/// The scripting editor's lazy entry point. The window is built on the first View → Script editor
/// open and then only hidden, so no scripting control tree enters the startup dump. The workspace
/// and export directory are per client profile; the UI globalises the saved user:// paths.
/// </summary>
public partial class ViewerShell
{
    ScriptEditorWindow? scriptEditor;
    Node? scriptingLayer;

    /// <summary>Remember where the lazy window will be attached (the UI layer, above the panels).</summary>
    void InitScripting(Node layer) => scriptingLayer = layer;

    void OpenScriptEditor()
    {
        if (scriptEditor is null)
        {
            scriptEditor = new ScriptEditorWindow
            {
                WorkspaceDirectory = ProjectSettings.GlobalizePath(UserSettings.LoadScriptingWorkspace(profile.Kind)),
                ExportDirectory = GlobalizeScriptPath(UserSettings.LoadScriptingExportDirectory(profile.Kind)),
                AutoExportOnSave = UserSettings.LoadScriptingAutoExport(),
                Host = ScriptHostRegistry.Profile(UserSettings.LoadScriptingTargetHost()) ?? AcScriptHosts.Profile,
                SaveHost = UserSettings.SaveScriptingTargetHost,
                ReferenceCheck = CheckScriptReferences,
                Report = statusLog.Post,
                SaveFolders = (workspace, export, autoExport) =>
                {
                    UserSettings.SaveScriptingWorkspace(profile.Kind, workspace);
                    UserSettings.SaveScriptingExportDirectory(profile.Kind, export);
                    UserSettings.SaveScriptingAutoExport(autoExport);
                }
            };
            scriptingLayer!.AddChild(scriptEditor);
        }

        scriptEditor.OpenEditor();
    }

    /// <summary>
    /// Confirm script ids against the connected world database before export. Without a usable
    /// connection the check is skipped (the values stay unchecked, exactly as the docs say).
    /// </summary>
    IReadOnlyList<ScriptReferenceIssue> CheckScriptReferences(ScriptDocument document, ScriptHostProfile host)
    {
        try
        {
            var database = DBSource.Open(DatabaseConnection(), clientLookup);
            var resolver = new ScriptReferenceResolver(new ScriptReferenceResolver.DbSource(database));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return Task.Run(() => resolver.CheckAsync(document, host, timeout.Token), timeout.Token)
                .GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is DatabaseFeatureException or OperationCanceledException)
        {
            return [];
        }
    }

    static string GlobalizeScriptPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : ProjectSettings.GlobalizePath(path);
}
