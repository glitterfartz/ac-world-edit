using System.Text.Json;
using ACWorldEdit;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The scripting canvas: port mapping, model-backed connections, edits and history.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ScriptCanvasTests
{
    static ScriptDocumentSession Session()
    {
        var session = new ScriptDocumentSession();
        var document = ScriptDocument.Create("Canvas");
        document.Graph.Nodes.Add(new ScriptNode
        {
            Id = "n1",
            Kind = "event.creature.on_died",
            Properties = { ["entry"] = JsonSerializer.SerializeToElement(1234) }
        });
        session.Load(document);
        return session;
    }

    static ScriptGraphCanvas Canvas(ScriptDocumentSession session)
    {
        var canvas = new ScriptGraphCanvas();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(canvas);
        AutoFree(canvas);
        canvas.Load(session);
        return canvas;
    }

    [TestCase]
    public void PortMapAgreesWithTheGraphNodePorts()
    {
        var session = Session();
        session.Document.Graph.Nodes.Add(new ScriptNode { Id = "n2", Kind = "unit.cast_spell" });
        var map = new ScriptPortMap();
        var view = new ScriptNodeView(session.Document, AcScriptHosts.Profile,
            session.Document.Graph.Nodes[1], map);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        AutoFree(view);

        foreach (var pin in ScriptPins.Build(session.Document, AcScriptHosts.Profile, session.Document.Graph.Nodes[1]))
        {
            AssertThat(map.PinForSlot(map.Slot(pin.Id))).IsEqual(pin.Id);
            if (pin.Direction == ScriptPinDirection.In)
            {
                AssertThat(view.GetInputPortSlot((int)map.InputPort(pin.Id)!.Value)).IsEqual(map.Slot(pin.Id));
            }
            else
            {
                AssertThat(view.GetOutputPortSlot((int)map.OutputPort(pin.Id)!.Value)).IsEqual(map.Slot(pin.Id));
            }
        }
    }

    [TestCase]
    public void CanvasBuildsOneViewPerNode()
    {
        var canvas = Canvas(Session());

        AssertThat(canvas.NodeViews.Count).IsEqual(1);
        AssertThat(canvas.NodeView("n1")).IsNotNull();
        AssertThat(canvas.NodeView("n1")!.Title).IsEqual("Creature · On Died");
    }

    [TestCase]
    public void ConnectingPinsAddsAnEdgeAndRejectsFanOut()
    {
        var session = Session();
        var canvas = Canvas(session);
        AssertThat(canvas.AddNode("unit.send_unit_say", new Vector2(200, 0))).IsNotNull();
        AssertThat(canvas.AddNode("unit.get_level", new Vector2(400, 0))).IsNotNull();
        string say = session.Document.Graph.Nodes[1].Id;

        AssertThat(canvas.ConnectPins("n1", "exec", say, "exec")).IsTrue();
        AssertThat(canvas.ConnectPins("n1", "exec", say, "exec")).IsFalse();
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(1);

        // The same exec output cannot fan out, but a second node may still take data.
        AssertThat(canvas.ConnectPins("n1", "exec", session.Document.Graph.Nodes[2].Id, "exec")).IsFalse();
    }

    [TestCase]
    public void ADataInputTakesAtMostOneConnection()
    {
        var session = Session();
        var canvas = Canvas(session);
        canvas.AddNode("unit.send_unit_say", new Vector2(200, 0));
        string say = session.Document.Graph.Nodes[1].Id;

        AssertThat(canvas.ConnectPins("n1", "creature", say, "self")).IsTrue();
        AssertThat(canvas.ConnectPins("n1", "killer", say, "self")).IsFalse();
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(1);
    }

    [TestCase]
    public void WrongPinTypeIsRejected()
    {
        var session = Session();
        var canvas = Canvas(session);
        canvas.AddNode("unit.send_unit_say", new Vector2(200, 0));
        string say = session.Document.Graph.Nodes[1].Id;

        AssertThat(canvas.ConnectPins("n1", "entry", say, "message")).IsFalse();
    }

    [TestCase]
    public void DisconnectingAndRemovingNodesUpdateTheModel()
    {
        var session = Session();
        var canvas = Canvas(session);
        canvas.AddNode("unit.send_unit_say", new Vector2(200, 0));
        string say = session.Document.Graph.Nodes[1].Id;
        canvas.ConnectPins("n1", "exec", say, "exec");
        canvas.ConnectPins("n1", "creature", say, "self");
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(2);

        string edge = session.Document.Graph.Edges[0].Id;
        canvas.DisconnectEdge(edge);
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(1);

        canvas.RemoveNode(say);
        AssertThat(session.Document.Graph.Nodes.Count).IsEqual(1);
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(0);
        AssertThat(canvas.NodeView(say)).IsNull();
    }

    [TestCase]
    public void UndoAndRedoRestoreEdgesAndNodes()
    {
        var session = Session();
        var canvas = Canvas(session);
        canvas.AddNode("unit.send_unit_say", new Vector2(200, 0));
        string say = session.Document.Graph.Nodes[1].Id;
        canvas.ConnectPins("n1", "exec", say, "exec");

        AssertThat(canvas.Undo()).IsTrue();
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(0);
        AssertThat(canvas.Undo()).IsTrue();
        AssertThat(session.Document.Graph.Nodes.Count).IsEqual(1);
        AssertThat(canvas.NodeView(say)).IsNull();

        AssertThat(canvas.Redo()).IsTrue();
        AssertThat(canvas.Redo()).IsTrue();
        AssertThat(session.Document.Graph.Nodes.Count).IsEqual(2);
        AssertThat(session.Document.Graph.Edges.Count).IsEqual(1);
    }

    [TestCase]
    public void DuplicateAndPasteCloneNodesWithFreshIds()
    {
        var session = Session();
        var canvas = Canvas(session);
        canvas.AddNode("unit.send_unit_say", new Vector2(200, 0));
        string say = session.Document.Graph.Nodes[1].Id;
        canvas.SetLiteral(say, "message", JsonSerializer.SerializeToElement("Hi"));
        canvas.NodeView(say)!.Selected = true;

        canvas.CopySelection();
        canvas.Paste();
        AssertThat(session.Document.Graph.Nodes.Count).IsEqual(3);
        AssertThat(session.Document.Graph.Nodes.Select(node => node.Id).Distinct().Count()).IsEqual(3);
        AssertThat(session.Document.Graph.Nodes[2].Literals["message"].GetString()).IsEqual("Hi");

        canvas.NodeView(say)!.Selected = false;
        foreach (var view in canvas.NodeViews)
        {
            view.Selected = false;
        }

        canvas.NodeView(say)!.Selected = true;
        canvas.DuplicateSelection();
        AssertThat(session.Document.Graph.Nodes.Count).IsEqual(4);
    }

    [TestCase]
    public void CommentFramesPersistAndCanBeRemoved()
    {
        var session = Session();
        var canvas = Canvas(session);

        string id = canvas.AddComment(new Vector2(10, 20))!;

        AssertThat(session.Document.Graph.Comments.Count).IsEqual(1);
        AssertThat(session.Document.Graph.Comments[0].Text).IsEqual("Comment");
        canvas.RemoveComment(id);
        AssertThat(session.Document.Graph.Comments.Count).IsEqual(0);
    }

    [TestCase]
    public void SelectingASecondNodeAndDeletingRemovesBoth()
    {
        var session = Session();
        var canvas = Canvas(session);
        string say = canvas.AddNode("unit.send_unit_say", new Vector2(200, 0))!;
        string level = canvas.AddNode("unit.get_level", new Vector2(400, 0))!;

        canvas.NodeView(say)!.Selected = true;
        canvas.NodeView(level)!.Selected = true;
        canvas.RemoveSelection();

        AssertThat(session.Document.Graph.Nodes.Count).IsEqual(1);
    }

    [TestCase]
    public void LiteralEditsAreUndoable()
    {
        var session = Session();
        var canvas = Canvas(session);
        canvas.AddNode("unit.send_unit_say", new Vector2(200, 0));
        string say = session.Document.Graph.Nodes[1].Id;

        canvas.SetLiteral(say, "message", JsonSerializer.SerializeToElement("Hello"));
        AssertThat(session.Document.Graph.Nodes[1].Literals["message"].GetString()).IsEqual("Hello");

        canvas.Undo();
        AssertThat(session.Document.Graph.Nodes[1].Literals.ContainsKey("message")).IsFalse();
    }
}
