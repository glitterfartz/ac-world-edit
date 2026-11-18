using ACWorldEdit;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The scripting editor window: template/new, palette, canvas selection, inspector edits,
/// problems, preview, workspace save/load and export.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ScriptEditorWindowTests
{
    static ScriptEditorWindow Spawn(out string workspace, out string export)
    {
        workspace = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"));
        export = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "lua_scripts");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(export);
        var window = new ScriptEditorWindow
        {
            WorkspaceDirectory = workspace,
            ExportDirectory = export,
            Report = _ => { }
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        AutoFree(window);
        window.OpenEditor();
        return window;
    }

    [TestCase]
    public void OpensWithAnEmptyDocumentAndTheAleHost()
    {
        var window = Spawn(out _, out _);

        AssertThat(window.Visible).IsTrue();
        AssertString(window.HostLabel.Text).Contains("ALE");
        AssertThat(window.Canvas.NodeViews.Count).IsEqual(0);
        AssertThat(window.Documents.ItemCount).IsEqual(0);
    }

    [TestCase]
    public void NewFromTheCreatureDeathTemplateShowsNodesAndCompiles()
    {
        var window = Spawn(out _, out _);

        window.NewDocument("creature-death");

        AssertThat(window.Canvas.NodeViews.Count > 3).IsTrue();
        AssertString(window.Preview.Text).Contains("RegisterCreatureEvent(1234, 4");
        AssertString(window.ProblemsTitle.Text).Contains("Problems (");
        AssertThat(window.Session.Dirty).IsFalse();
    }

    [TestCase]
    public void PaletteSearchFindsANodeAndAddsItToTheGraph()
    {
        var window = Spawn(out _, out _);
        window.FillPalette("cast spell");

        AssertThat(window.PaletteResults.ItemCount > 0).IsTrue();
        string? id = window.AddFromPalette("unit.cast_spell");

        AssertThat(id).IsNotNull();
        AssertThat(window.Canvas.NodeViews.Count).IsEqual(1);
        AssertThat(window.Session.Dirty).IsTrue();
    }

    [TestCase]
    public void InspectorEditsALiteralAndThePreviewFollows()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("creature-death");
        var say = window.Session.Document.Graph.Nodes.First(node => node.Kind == "unit.send_unit_say");
        window.Canvas.SelectNode(say.Id);

        var field = window.Inspector.GetNodeOrNull<LineEdit>("Literal_message");
        AssertThat(field).IsNotNull();
        field!.Text = "Hello from the editor";
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, field.Text);

        AssertThat(window.Session.Document.Graph.Nodes.First(node => node.Id == say.Id)
            .Literals["message"].GetString()).IsEqual("Hello from the editor");
        AssertString(window.Preview.Text).Contains("Hello from the editor");
    }

    [TestCase]
    public void ProblemsSelectTheirNode()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        string? eventId = window.Canvas.AddNode("event.creature.on_died", new Vector2(0, 0));
        window.Session.Edit(document => document.Graph.Nodes.First(node => node.Id == eventId).Properties.Remove("entry"));
        window.Canvas.RebuildFromSession();
        var compilation = window.Compile();

        AssertThat(compilation.Success).IsFalse();
        AssertThat(window.Problems.ItemCount > 0).IsTrue();
        int index = Enumerable.Range(0, window.Problems.ItemCount)
            .First(i => (string)window.Problems.GetItemMetadata(i) == eventId);
        window.OpenProblem(index);

        AssertThat(window.Canvas.SelectedNodeId).IsEqual(eventId);
    }

    [TestCase]
    public void SaveAndReopenRoundTripsThroughTheWorkspace()
    {
        var window = Spawn(out string workspace, out _);
        window.NewDocument("creature-death");
        window.Session.Edit(document => document.Description = "round trip");

        AssertThat(window.SaveDocument()).IsTrue();
        AssertThat(window.Session.Dirty).IsFalse();
        AssertThat(Directory.GetFiles(workspace, "*.acwscript").Length).IsEqual(1);
        AssertThat(window.Documents.ItemCount).IsEqual(1);

        string path = Directory.GetFiles(workspace, "*.acwscript")[0];
        AssertThat(window.OpenDocument(path)).IsTrue();
        AssertThat(window.Session.Document.Description).IsEqual("round trip");
        AssertThat(window.Canvas.NodeViews.Count > 3).IsTrue();
    }

    [TestCase]
    public void ExportWritesTheLuaIntoTheExportDirectory()
    {
        var window = Spawn(out _, out string export);
        window.NewDocument("creature-death");

        AssertThat(window.ExportCurrent()).IsTrue();

        string file = Path.Combine(export, ScriptCompiler.OutputName(window.Session.Document));
        AssertThat(File.Exists(file)).IsTrue();
        AssertString(File.ReadAllText(file)).Contains("RegisterCreatureEvent(1234, 4");
        AssertThat(File.Exists(Path.Combine(export, ScriptManifest.FileName))).IsTrue();
    }

    [TestCase]
    public void ExportIsRefusedWhenCompilationFails()
    {
        var window = Spawn(out _, out string export);
        window.NewDocument("blank");
        window.Canvas.AddNode("future.node", new Vector2(0, 0));

        AssertThat(window.ExportCurrent()).IsFalse();
        AssertThat(Directory.GetFiles(export, "*.lua").Length).IsEqual(0);
    }

    [TestCase]
    public void FunctionGraphsOpenAndSignatureChangesReachTheCompiler()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        window.AddFunction();
        var function = window.CurrentFunction!;
        AssertThat(window.Canvas.Graph).IsSame(function.Graph);
        window.Canvas.AddNode("flow.parameter_get", new Vector2(0, 0));
        string parameterNode = window.Canvas.Graph.Nodes[^1].Id;
        window.Canvas.SetProperty(parameterNode, "parameter", System.Text.Json.JsonSerializer.SerializeToElement("p1"));
        string returnNodeId = window.Canvas.Graph.Nodes.First(node => node.Kind == "flow.return").Id;

        window.SetFunctionSignature(function.Id, "twice",
            [("value", ScriptValueType.Float)], [("result", ScriptValueType.Float)]);
        window.Canvas.ConnectPins(parameterNode, "value", returnNodeId, "result");
        window.Compile();

        AssertThat(window.Session.Document.Functions[0].Parameters.Count).IsEqual(1);
        var returnNode = window.Session.Document.Functions[0].Graph.Nodes.First(node => node.Kind == "flow.return");
        AssertThat(returnNode.Pins.Any(pin => pin.Id == "result")).IsTrue();
        AssertString(window.Preview.Text).Contains("function __fns.twice");
    }

    [TestCase]
    public void RemovingAParameterKeepsTheSurvivingParameterIds()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        window.AddFunction();
        var function = window.CurrentFunction!;
        window.SetFunctionSignature(function.Id, "pair",
            [("unit", ScriptValueType.Unit), ("amount", ScriptValueType.Int)], []);
        string amountId = window.Session.Document.Functions[0].Parameters[1].Id;

        window.SetFunctionSignature(function.Id, "pair", [("amount", ScriptValueType.Int)], []);

        var parameters = window.Session.Document.Functions[0].Parameters;
        AssertThat(parameters.Count).IsEqual(1);
        AssertString(parameters[0].Id).IsEqual(amountId);
    }

    [TestCase]
    public void TheInspectorEditsNodesInTheOpenFunctionGraph()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        window.AddFunction();
        var function = window.CurrentFunction!;
        window.SetFunctionSignature(function.Id, "heal", [("amount", ScriptValueType.Int)], []);
        window.Canvas.AddNode("flow.parameter_get", new Vector2(0, 0));
        string nodeId = window.Canvas.Graph.Nodes[^1].Id;

        window.ShowInspector(nodeId);

        var picker = window.Inspector.GetNodeOrNull<OptionButton>("Parameter");
        AssertThat(picker).IsNotNull();
        AssertString((string)picker!.GetItemMetadata(0)).IsEqual(function.Parameters[0].Id);
    }

    [TestCase]
    public void TheInspectorBindsACallToAUserFunction()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        window.AddFunction();
        var function = window.CurrentFunction!;
        window.SetFunctionSignature(function.Id, "heal", [("amount", ScriptValueType.Int)], []);
        window.ShowEventGraph();
        window.Canvas.AddNode("flow.call_function", new Vector2(0, 0));
        string nodeId = window.Canvas.Graph.Nodes[^1].Id;
        window.ShowInspector(nodeId);

        var picker = window.Inspector.GetNodeOrNull<OptionButton>("Function");
        AssertThat(picker).IsNotNull();
        AssertString((string)picker!.GetItemMetadata(0)).IsEqual(function.Id);
        picker.Select(0);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, 0);

        var node = window.Session.Document.Graph.Nodes.First(item => item.Id == nodeId);
        AssertString(ScriptPins.PropertyString(node, "function")).IsEqual(function.Id);
    }

    [TestCase]
    public void UndoingAHostChangeRestoresTheCompilationHost()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        AssertThat(window.ChangeHost("eluna-trinitycore")).IsTrue();

        window.Undo();

        AssertString(window.Host.Host).IsEqual("ale-azerothcore");
        AssertString(window.Session.Document.Host).IsEqual("ale-azerothcore");
    }

    [TestCase]
    public void OpeningADocumentAdoptsItsHost()
    {
        var window = Spawn(out string workspace, out _);
        var document = ScriptDocument.Create("Eluna script");
        document.Host = "eluna-trinitycore";
        string path = new ScriptWorkspace(workspace).Save(document, null);

        AssertThat(window.OpenDocument(path)).IsTrue();

        AssertString(window.Host.Host).IsEqual("eluna-trinitycore");
    }

    [TestCase]
    public void SavingAfterChangingTheWorkspaceWritesAFreshFile()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        AssertThat(window.SaveDocument()).IsTrue();
        string other = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);

        window.ApplyFolders(other, window.ExportDirectory, false);

        AssertThat(window.SaveDocument()).IsTrue();
        AssertThat(Directory.GetFiles(other, "*.acwscript").Length).IsEqual(1);
    }

    [TestCase]
    public void OpeningAnotherScriptDoesNotDiscardUnsavedEdits()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("creature-death");
        window.AddFromPalette("unit.send_unit_say");
        string originalId = window.Session.Document.Id;

        window.NewDocument("blank");

        AssertString(window.Session.Document.Id).IsEqual(originalId);
        AssertThat(window.Session.Dirty).IsTrue();
    }

    [TestCase]
    public void AConfirmedMissingReferenceBlocksExport()
    {
        var window = Spawn(out _, out string export);
        window.NewDocument("creature-death");
        window.ReferenceCheck = (_, _) =>
        [
            new ScriptReferenceIssue(ScriptDiagnosticCodes.MissingEntry,
                ScriptReferenceKind.Creature, 1234, "Creature 1234 does not exist.", BlocksExport: true)
        ];

        AssertThat(window.ExportCurrent()).IsFalse();
        AssertThat(Directory.GetFiles(export, "*.lua").Length).IsEqual(0);
    }

    [TestCase]
    public void TheSpellCastTemplateCompiles()
    {
        var window = Spawn(out _, out _);

        window.NewDocument("spell-cast");

        AssertString(window.Preview.Text).Contains("RegisterSpellEvent(133, 2");
        AssertThat(window.Compile().Success).IsTrue();
    }

    [TestCase]
    public void CommentFramesAppearInTheDocument()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");

        window.AddComment();

        AssertThat(window.Session.Document.Graph.Comments.Count).IsEqual(1);
        AssertThat(window.Session.Dirty).IsTrue();
    }

    [TestCase]
    public void SwitchingHostRevalidatesAndCompiles()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("creature-death");

        AssertThat(window.ChangeHost("eluna-trinitycore")).IsTrue();

        AssertString(window.HostLabel.Text).Contains("Eluna");
        AssertThat(window.Session.Document.Host).IsEqual("eluna-trinitycore");
        AssertThat(window.Compile().Success).IsTrue();
        AssertString(window.Preview.Text).Contains("RegisterCreatureEvent(1234, 4");
    }

    [TestCase]
    public void UndoAndRedoFollowCanvasEditsAndMarkTheTitle()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        window.Canvas.AddNode("unit.send_unit_say", new Vector2(10, 10));
        AssertString(window.TitleLabel.Text).Contains("*");

        AssertThat(window.Undo()).IsTrue();
        AssertThat(window.Canvas.NodeViews.Count).IsEqual(0);
        AssertThat(window.Redo()).IsTrue();
        AssertThat(window.Canvas.NodeViews.Count).IsEqual(1);
    }

    [TestCase]
    public void ClosingADirtyDocumentAsksBeforeDiscarding()
    {
        var window = Spawn(out _, out _);
        window.NewDocument("blank");
        window.Canvas.AddNode("unit.send_unit_say", new Vector2(0, 0));

        // Close is refused without the dialog answer; the window stays open and dirty.
        window.AttemptClose();

        AssertThat(window.Visible).IsTrue();
        AssertThat(window.Session.Dirty).IsTrue();
    }
}
