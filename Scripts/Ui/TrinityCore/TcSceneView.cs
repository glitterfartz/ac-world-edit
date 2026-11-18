using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The scene editor: the template fields (script package, flags, encryption, script) and the client
/// package's script steps read-only, so the package can be chosen knowing what it plays.
/// </summary>
public partial class TcSceneView : VBoxContainer
{
    public Func<string, RowEdit[], bool> Commit { get; set; } = (_, _) => false;
    public Action NewRequested { get; set; } = () => { };

    public int StepCount { get; private set; }
    public string PackageName { get; private set; } = "";

    SceneSnapshot? snapshot;
    uint sceneId;

    static readonly string[] FlagNames = ["Flag 1", "Flag 2", "Flag 4", "Flag 8", "Flag 16", "Flag 32", "Flag 64", "Flag 128"];

    public TcSceneView()
    {
        AddThemeConstantOverride("separation", 8);
    }

    public void Show(SceneSnapshot? view)
    {
        snapshot = view;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        StepCount = 0;
        PackageName = "";
        if (view?.Rows.All(TcTables.SceneTemplate).FirstOrDefault() is not { } template)
        {
            AddChild(ClientStyle.Text("No scene open.", 14, ClientStyle.Faded));
            return;
        }

        sceneId = template.U("SceneId");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var card = new PanelContainer();
        card.AddChild(box);
        AddChild(card);
        box.AddChild(ClientStyle.Text($"Scene {sceneId}", 20, ClientStyle.Header));

        PackageName = view.Package?.Name ?? (template.U("ScriptPackageID") == 0 ? "No package" : $"Package {template.U("ScriptPackageID")}");
        box.AddChild(EditorFields.Row("Script package", "The client SceneScriptPackage the scene plays.",
            EditorFields.Number(template.U("ScriptPackageID"), value => Set(template, "Change script package", ("ScriptPackageID", (uint)Math.Max(value, 0))), 90, "The client SceneScriptPackage the scene plays."),
            EditorFields.Line(view.Package?.Name ?? "", _ => { }, "package name", 0, "The package's client name.")));

        box.AddChild(ClientStyle.Text("Flags", 14, ClientStyle.Header));
        var flags = new HBoxContainer();
        flags.AddThemeConstantOverride("separation", 8);
        box.AddChild(flags);
        for (int bit = 0; bit < FlagNames.Length; bit++)
        {
            uint mask = 1u << bit;
            var before = template;
            var check = new CheckBox
            {
                Name = $"SceneFlag_{mask}",
                Text = FlagNames[bit],
                ButtonPressed = (before.U("Flags") & mask) != 0,
                TooltipText = $"Scene flag {mask}."
            };
            check.Toggled += on => SetFlag(mask, on);
            flags.AddChild(check);
        }

        var advanced = new HBoxContainer();
        advanced.AddThemeConstantOverride("separation", 10);
        box.AddChild(advanced);
        advanced.AddChild(EditorFields.Row("Encrypted", "Whether the client may only play this scene for encrypted data.",
            EditorFields.Number(template.U("Encrypted"), value => Set(template, "Change scene encryption", ("Encrypted", (uint)Math.Max(value, 0))), 60, "The scene's encryption hint.")));
        advanced.AddChild(EditorFields.Row("Script", "The server script that runs while the scene plays.",
            EditorFields.Line(template.T("ScriptName"), text => Set(template, "Change scene script", ("ScriptName", text.Trim())), "ScriptName", 64, "The server script for this scene.")));

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        AddChild(actions);
        var create = ContentEditorWindow.ButtonIn(actions, "+ New scene", () => NewRequested());
        create.Name = "NewScene";
        create.TooltipText = "Create an empty scene with a new ID.";

        AddChild(ClientStyle.Text("Script steps", 16, ClientStyle.Header));
        if (view.Package is not { Steps.Count: > 0 } package)
        {
            AddChild(ClientStyle.Text("The client data for this package is unavailable, so its steps are not shown.", 12, ClientStyle.Faded));
            return;
        }

        foreach (var step in package.Steps)
        {
            StepCount++;
            var text = ClientStyle.Text($"{step.Order + 1}. {step.Name}", 12, ClientStyle.Faded);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.TooltipText = step.Script;
            AddChild(text);
        }
    }

    /// <summary>The step's name and script as one line, for the tab's checks.</summary>
    public string StepText(int index)
    {
        if (snapshot?.Package is not { } package || index < 0 || index >= package.Steps.Count)
        {
            return "";
        }

        var step = package.Steps[index];
        return $"{step.Name} {step.Script}";
    }

    /// <summary>Turn one flag bit on or off on the open scene's template row.</summary>
    public void SetFlag(uint mask, bool on)
    {
        if (snapshot?.Rows.All(TcTables.SceneTemplate).FirstOrDefault() is not { } template)
        {
            return;
        }

        Set(template, on ? $"Turn on scene flag {mask}" : $"Turn off scene flag {mask}",
            ("Flags", on ? template.U("Flags") | mask : template.U("Flags") & ~mask));
    }

    void Set(Row before, string label, params (string Column, object Value)[] values)
    {
        var after = before;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        if (before != after)
        {
            Commit(label, ItemListSpec.Change(TcTables.SceneTemplate, before, after));
        }
    }
}
