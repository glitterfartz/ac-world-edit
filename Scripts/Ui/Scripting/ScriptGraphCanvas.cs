using System.Text.Json;
using Godot;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit;

/// <summary>
/// The graph canvas. It builds one <see cref="ScriptNodeView"/> per model node, translates
/// GraphEdit's request signals into model edits (through the session, so they are undoable), and
/// enforces the connection rules again after the hover hints: one connection per data input, no
/// exec fan-out, no exec/data mixing, and type assignability.
/// </summary>
public partial class ScriptGraphCanvas : GraphEdit
{
    readonly List<ScriptNodeView> views = [];
    ScriptGraph? clipboard;
    ScriptDocumentSession? session;

    public ScriptGraphCanvas()
    {
        Name = "ScriptGraph";
        RightDisconnects = true;
        ConnectionRequest += OnConnectionRequest;
        DisconnectionRequest += OnDisconnectionRequest;
        EndNodeMove += OnEndNodeMove;
        CopyNodesRequest += CopySelection;
        PasteNodesRequest += Paste;
        DuplicateNodesRequest += DuplicateSelection;
        DeleteNodesRequest += _ => RemoveSelection();
        NodeSelected += node =>
        {
            if (node is ScriptNodeView view)
            {
                SelectionChanged?.Invoke(view.NodeId);
            }
        };
    }

    public ScriptHostProfile Host { get; set; } = AcScriptHosts.Profile;

    public ScriptDocumentSession? Session => session;

    /// <summary>The graph being edited: the document's event graph or one function graph.</summary>
    public ScriptGraph Graph { get; private set; } = new();

    /// <summary>The open function's id when <see cref="Graph"/> is a function body.</summary>
    public string? GraphFunctionId { get; private set; }

    public IReadOnlyList<ScriptNodeView> NodeViews => views;

    public event Action? Changed;

    public event Action<string?>? SelectionChanged;

    /// <summary>Adopt a session and rebuild every node and connection.</summary>
    public void Load(ScriptDocumentSession next)
    {
        session = next;
        GraphFunctionId = null;
        Graph = next.Document.Graph;
        Rebuild();
    }

    /// <summary>Show another graph of the same document (a function body).</summary>
    public void ShowGraph(ScriptGraph graph, string? functionId = null)
    {
        GraphFunctionId = functionId;
        Graph = graph;
        Rebuild();
    }

    /// <summary>After undo/redo the session holds a fresh document; re-resolve the shown graph.</summary>
    void RestoreGraph()
    {
        if (session is null)
        {
            return;
        }

        Graph = GraphFunctionId is null
            ? session.Document.Graph
            : session.Document.Functions.FirstOrDefault(function => function.Id == GraphFunctionId)?.Graph
                ?? session.Document.Graph;
    }

    public ScriptNodeView? NodeView(string nodeId) =>
        views.FirstOrDefault(view => view.NodeId == nodeId);

    public string? SelectedNodeId =>
        views.FirstOrDefault(view => view.Selected)?.NodeId;

    /// <summary>Add a node of a semantic kind at a graph position and return its new id.</summary>
    public string? AddNode(string kind, Vector2 position)
    {
        if (session is null)
        {
            return null;
        }

        string id = NextId("n", Graph.Nodes.Select(node => node.Id));
        session.Edit(document => Graph.Nodes.Add(new ScriptNode
        {
            Id = id,
            Kind = kind,
            Position = new ScriptPosition { X = position.X, Y = position.Y }
        }));
        Rebuild();
        return id;
    }

    public bool RemoveNode(string nodeId)
    {
        if (session is null || Graph.Nodes.All(node => node.Id != nodeId))
        {
            return false;
        }

        session.Edit(document =>
        {
            Graph.Nodes.RemoveAll(node => node.Id == nodeId);
            Graph.Edges.RemoveAll(edge => edge.From.Node == nodeId || edge.To.Node == nodeId);
        });
        Rebuild();
        return true;
    }

    public bool ConnectPins(string fromNode, string fromPin, string toNode, string toPin)
    {
        if (session is null || !CanConnect(fromNode, fromPin, toNode, toPin))
        {
            return false;
        }

        string id = NextId("e", Graph.Edges.Select(edge => edge.Id));
        session.Edit(document => Graph.Edges.Add(new ScriptEdge
        {
            Id = id,
            From = new ScriptPinRef { Node = fromNode, Pin = fromPin },
            To = new ScriptPinRef { Node = toNode, Pin = toPin }
        }));
        Rebuild();
        return true;
    }

    public bool DisconnectEdge(string edgeId)
    {
        if (session is null || Graph.Edges.All(edge => edge.Id != edgeId))
        {
            return false;
        }

        session.Edit(document => Graph.Edges.RemoveAll(edge => edge.Id == edgeId));
        Rebuild();
        return true;
    }

    public void SetLiteral(string nodeId, string pinId, JsonElement value)
    {
        if (session is null)
        {
            return;
        }

        session.Edit(document =>
        {
            var node = Graph.Nodes.First(item => item.Id == nodeId);
            node.Literals[pinId] = value.Clone();
        });
        Rebuild();
    }

    public void SetProperty(string nodeId, string name, JsonElement value)
    {
        if (session is null)
        {
            return;
        }

        session.Edit(document =>
        {
            var node = Graph.Nodes.First(item => item.Id == nodeId);
            node.Properties[name] = value.Clone();
        });
        Rebuild();
    }

    /// <summary>Rebuild the views after a change made directly through the session.</summary>
    public void RebuildFromSession()
    {
        RestoreGraph();
        Rebuild();
    }

    public void SelectNode(string nodeId)
    {
        if (NodeView(nodeId) is not { } view)
        {
            return;
        }

        view.Selected = true;
        SelectionChanged?.Invoke(nodeId);
    }

    public void ClearLiteral(string nodeId, string pinId)
    {
        if (session is null)
        {
            return;
        }

        session.Edit(document =>
        {
            var node = Graph.Nodes.First(item => item.Id == nodeId);
            node.Literals.Remove(pinId);
        });
        Rebuild();
    }

    public bool Undo()
    {
        if (session is null || !session.Undo())
        {
            return false;
        }

        RestoreGraph();
        Rebuild();
        return true;
    }

    public bool Redo()
    {
        if (session is null || !session.Redo())
        {
            return false;
        }

        RestoreGraph();
        Rebuild();
        return true;
    }

    public void CenterOnNode(string nodeId)
    {
        if (NodeView(nodeId) is not { } view)
        {
            return;
        }

        ScrollOffset = view.PositionOffset + view.Size * 0.5f - Size * 0.5f / Zoom;
        view.Selected = true;
        SelectionChanged?.Invoke(nodeId);
    }

    public void FrameAll()
    {
        if (views.Count == 0)
        {
            return;
        }

        Rect2 bounds = new(views[0].PositionOffset, views[0].Size);
        foreach (var view in views.Skip(1))
        {
            bounds = bounds.Merge(new Rect2(view.PositionOffset, view.Size));
        }

        Zoom = Mathf.Clamp(Mathf.Min(Size.X / Mathf.Max(bounds.Size.X + 120, 1), Size.Y / Mathf.Max(bounds.Size.Y + 120, 1)), 0.25f, 1f);
        ScrollOffset = bounds.Position + bounds.Size * 0.5f - Size * 0.5f / Zoom;
    }

    void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        if (NodeView(fromNode) is not { } from || NodeView(toNode) is not { } to)
        {
            return;
        }

        string? fromPin = from.Ports.PinForOutput((int)fromPort);
        string? toPin = to.Ports.PinForInput((int)toPort);
        if (fromPin is not null && toPin is not null)
        {
            ConnectPins(from.NodeId, fromPin, to.NodeId, toPin);
        }
    }

    void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        string? edge = session is null ? null : Graph.Edges.FirstOrDefault(item =>
            item.From.Node == fromNode.ToString() && item.To.Node == toNode.ToString())?.Id;
        if (edge is not null)
        {
            DisconnectEdge(edge);
        }
    }

    void OnEndNodeMove()
    {
        if (session is null)
        {
            return;
        }

        bool moved = views.Any(view =>
        {
            var node = Graph.Nodes.FirstOrDefault(item => item.Id == view.NodeId);
            return node is not null
                && (node.Position.X != view.PositionOffset.X || node.Position.Y != view.PositionOffset.Y);
        });
        foreach (var frame in GetChildren().OfType<GraphFrame>())
        {
            string id = frame.Name.ToString()["Frame_".Length..];
            var comment = Graph.Comments.FirstOrDefault(item => item.Id == id);
            moved |= comment is not null
                && (comment.Position.X != frame.PositionOffset.X || comment.Position.Y != frame.PositionOffset.Y
                    || comment.Size.X != frame.Size.X || comment.Size.Y != frame.Size.Y);
        }

        if (!moved)
        {
            return;
        }

        session.Edit(document =>
        {
            foreach (var view in views)
            {
                var node = Graph.Nodes.FirstOrDefault(item => item.Id == view.NodeId);
                if (node is not null)
                {
                    node.Position.X = view.PositionOffset.X;
                    node.Position.Y = view.PositionOffset.Y;
                }
            }

            foreach (var frame in GetChildren().OfType<GraphFrame>())
            {
                string id = frame.Name.ToString()["Frame_".Length..];
                var comment = Graph.Comments.FirstOrDefault(item => item.Id == id);
                if (comment is not null)
                {
                    comment.Position.X = frame.PositionOffset.X;
                    comment.Position.Y = frame.PositionOffset.Y;
                    comment.Size.X = frame.Size.X;
                    comment.Size.Y = frame.Size.Y;
                }
            }
        });
        Changed?.Invoke();
    }

    bool CanConnect(string fromNode, string fromPin, string toNode, string toPin)
    {
        if (session is null)
        {
            return false;
        }

        var from = Graph.Nodes.FirstOrDefault(node => node.Id == fromNode);
        var to = Graph.Nodes.FirstOrDefault(node => node.Id == toNode);
        if (from is null || to is null)
        {
            return false;
        }

        var fromPins = ScriptPins.Build(session.Document, Host, from);
        var toPins = ScriptPins.Build(session.Document, Host, to);
        var fromPinDef = ScriptPins.Find(fromPins, fromPin);
        var toPinDef = ScriptPins.Find(toPins, toPin);
        if (fromPinDef is null || toPinDef is null
            || fromPinDef.Direction != ScriptPinDirection.Out || toPinDef.Direction != ScriptPinDirection.In)
        {
            return false;
        }

        if ((fromPinDef.Type == ScriptValueType.Exec) != (toPinDef.Type == ScriptValueType.Exec)
            || !ScriptTypeChecker.PinAccepts(fromPinDef, toPinDef))
        {
            return false;
        }

        bool exec = fromPinDef.Type == ScriptValueType.Exec;
        var edges = Graph.Edges;
        if (exec)
        {
            return edges.All(edge => !(edge.From.Node == fromNode && edge.From.Pin == fromPin));
        }

        return edges.All(edge => !(edge.To.Node == toNode && edge.To.Pin == toPin));
    }

    public IReadOnlyList<ScriptNodeView> SelectedViews =>
        views.Where(view => view.Selected).ToList();

    /// <summary>Delete the selected nodes and every edge touching them.</summary>
    public void RemoveSelection()
    {
        var ids = SelectedViews.Select(view => view.NodeId).ToHashSet(StringComparer.Ordinal);
        if (session is null || ids.Count == 0)
        {
            return;
        }

        session.Edit(document =>
        {
            Graph.Nodes.RemoveAll(node => ids.Contains(node.Id));
            Graph.Edges.RemoveAll(edge => ids.Contains(edge.From.Node) || ids.Contains(edge.To.Node));
        });
        Rebuild();
    }

    public void CopySelection()
    {
        var ids = SelectedViews.Select(view => view.NodeId).ToHashSet(StringComparer.Ordinal);
        clipboard = Fragment(ids);
    }

    public void DuplicateSelection()
    {
        CopySelection();
        PasteClipboard(new Vector2(30, 30));
    }

    public void Paste() => PasteClipboard(new Vector2(30, 30));

    /// <summary>Add a titled comment frame at a graph position.</summary>
    public string? AddComment(Vector2 position)
    {
        if (session is null)
        {
            return null;
        }

        string id = NextId("c", Graph.Comments.Select(comment => comment.Id));
        session.Edit(document => Graph.Comments.Add(new ScriptComment
        {
            Id = id,
            Text = "Comment",
            Position = new ScriptPosition { X = position.X, Y = position.Y },
            Size = new ScriptSize { X = 320, Y = 200 }
        }));
        Rebuild();
        return id;
    }

    public bool RemoveComment(string id)
    {
        if (session is null || Graph.Comments.All(comment => comment.Id != id))
        {
            return false;
        }

        session.Edit(document => Graph.Comments.RemoveAll(comment => comment.Id == id));
        Rebuild();
        return true;
    }

    ScriptGraph? Fragment(HashSet<string> ids)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        var fragment = new ScriptGraph();
        foreach (var node in Graph.Nodes.Where(node => ids.Contains(node.Id)))
        {
            fragment.Nodes.Add(Clone(node)!);
        }

        foreach (var edge in Graph.Edges.Where(edge => ids.Contains(edge.From.Node) && ids.Contains(edge.To.Node)))
        {
            fragment.Edges.Add(Clone(edge)!);
        }

        return fragment;
    }

    void PasteClipboard(Vector2 offset)
    {
        if (session is null || clipboard is null || clipboard.Nodes.Count == 0)
        {
            return;
        }

        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        session.Edit(document =>
        {
            foreach (var source in clipboard.Nodes)
            {
                var node = Clone(source)!;
                node.Id = NextId("n", Graph.Nodes.Select(item => item.Id));
                node.Position.X += offset.X;
                node.Position.Y += offset.Y;
                remap[source.Id] = node.Id;
                Graph.Nodes.Add(node);
            }

            foreach (var source in clipboard.Edges)
            {
                var edge = Clone(source)!;
                edge.Id = NextId("e", Graph.Edges.Select(item => item.Id));
                edge.From.Node = remap.GetValueOrDefault(edge.From.Node, edge.From.Node);
                edge.To.Node = remap.GetValueOrDefault(edge.To.Node, edge.To.Node);
                Graph.Edges.Add(edge);
            }
        });
        Rebuild();
        foreach (string id in remap.Values)
        {
            if (NodeView(id) is { } view)
            {
                view.Selected = true;
            }
        }
    }

    static T? Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, ScriptDocumentJson.Options), ScriptDocumentJson.Options);

    void Rebuild()
    {
        ClearConnections();
        foreach (var view in views)
        {
            RemoveChild(view);
            view.QueueFree();
        }

        foreach (var frame in GetChildren().OfType<GraphFrame>())
        {
            RemoveChild(frame);
            frame.QueueFree();
        }

        views.Clear();
        if (session is null)
        {
            Changed?.Invoke();
            return;
        }

        foreach (var comment in Graph.Comments)
        {
            var frame = new GraphFrame
            {
                Name = "Frame_" + comment.Id,
                Title = comment.Text,
                PositionOffset = new Vector2((float)comment.Position.X, (float)comment.Position.Y),
                Size = new Vector2((float)comment.Size.X, (float)comment.Size.Y)
            };
            AddChild(frame);
        }

        foreach (var node in Graph.Nodes)
        {
            var view = new ScriptNodeView(session.Document, Host, node, new ScriptPortMap());
            AddChild(view);
            views.Add(view);
        }

        foreach (var edge in Graph.Edges)
        {
            var from = NodeView(edge.From.Node);
            var to = NodeView(edge.To.Node);
            int? fromPort = from?.Ports.OutputPort(edge.From.Pin);
            int? toPort = to?.Ports.InputPort(edge.To.Pin);
            if (from is not null && to is not null && fromPort is not null && toPort is not null)
            {
                ConnectNode(from.Name, (int)fromPort, to.Name, (int)toPort);
            }
        }

        Changed?.Invoke();
    }

    static string NextId(string prefix, IEnumerable<string> ids)
    {
        int max = 0;
        foreach (string id in ids)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(id[prefix.Length..], out int value))
            {
                max = Math.Max(max, value);
            }
        }

        return prefix + (max + 1);
    }
}
