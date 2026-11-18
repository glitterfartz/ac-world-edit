using System.Text.Json;
using Godot;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit;

/// <summary>
/// The scripting editor: toolbar, document list, searchable palette, graph canvas, node inspector,
/// variables/functions, problems and the Lua preview. It owns the document session, workspace and
/// export service; the canvas owns graph mutation so every change is one undo entry.
/// </summary>
public partial class ScriptEditorWindow : ScriptWindowFrame
{
    readonly List<string> paletteKinds = [];
    ScriptFileStamp? stamp;

    public ScriptEditorWindow() : base("Script editor")
    {
        Name = "ScriptEditorWindow";

        var toolbar = new HBoxContainer();
        Body.AddChild(toolbar);
        var newButton = new Button { Text = "New", TooltipText = "New blank script" };
        newButton.Pressed += () => NewDocument("blank");
        toolbar.AddChild(newButton);
        var templates = new OptionButton { TooltipText = "New from a template" };
        foreach (string name in ScriptTemplateLibrary.Names)
        {
            templates.AddItem(name);
        }

        templates.ItemSelected += index => NewDocument(ScriptTemplateLibrary.Names[(int)index]);
        toolbar.AddChild(templates);
        toolbar.AddChild(ToolbarButton("Save", () => SaveDocument(), "Save to the workspace (Ctrl+S)"));
        toolbar.AddChild(ToolbarButton("Compile", () => Compile(), "Compile and show the Lua (Ctrl+Enter)"));
        toolbar.AddChild(ToolbarButton("Undo", () => Undo(), "Undo (Ctrl+Z)"));
        toolbar.AddChild(ToolbarButton("Redo", () => Redo(), "Redo (Ctrl+Y)"));
        toolbar.AddChild(ToolbarButton("Frame", () => Canvas.FrameAll(), "Frame all nodes (Home)"));
        toolbar.AddChild(ToolbarButton("Export", () => ExportCurrent(), "Export this script (Ctrl+Shift+E)"));
        toolbar.AddChild(ToolbarButton("Export all", () => ExportAllDocument(), "Export every script in the workspace"));
        toolbar.AddChild(ToolbarButton("Folders…", OpenFolders, "Workspace and export folders"));
        toolbar.AddChild(ToolbarButton("Comment", AddComment, "Add a comment frame"));
        toolbar.AddChild(ToolbarButton("Reveal", RevealExportFolder, "Open the export folder"));
        HostLabel = ClientStyle.Text("Host: " + Host.Label, 13, ClientStyle.Faded);
        toolbar.AddChild(HostLabel);
        HostPicker = new OptionButton { TooltipText = "Compile for another host" };
        foreach (var info in ScriptHostRegistry.Hosts)
        {
            int index = HostPicker.ItemCount;
            HostPicker.AddItem(info.Experimental ? $"{info.Label} (experimental)" : info.Label);
            HostPicker.SetItemMetadata(index, info.Id);
            if (info.Id == Host.Host)
            {
                HostPicker.Selected = index;
            }
        }

        HostPicker.ItemSelected += index => ChangeHost((string)HostPicker.GetItemMetadata((int)index));
        toolbar.AddChild(HostPicker);
        StatusLabel = ClientStyle.Text("", 13, ClientStyle.Faded);
        StatusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        StatusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        toolbar.AddChild(StatusLabel);

        var main = new HSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Body.AddChild(main);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };
        main.AddChild(left);
        left.AddChild(ClientStyle.Text("Documents", 15, ClientStyle.Header));
        Documents = new ItemList { CustomMinimumSize = new Vector2(0, 150), AllowReselect = true };
        Documents.ItemActivated += index => OpenDocument((string)Documents.GetItemMetadata((int)index));
        left.AddChild(Documents);
        left.AddChild(new HSeparator());
        left.AddChild(ClientStyle.Text("Palette", 15, ClientStyle.Header));
        PaletteSearchField = new LineEdit { PlaceholderText = "Search nodes… (Ctrl+F)" };
        PaletteSearchField.TextChanged += text => FillPalette(text);
        left.AddChild(PaletteSearchField);
        PaletteResults = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        PaletteResults.ItemActivated += index => AddFromPalette((string)PaletteResults.GetItemMetadata((int)index));
        left.AddChild(PaletteResults);

        var center = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        main.AddChild(center);
        Canvas = new ScriptGraphCanvas { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Canvas.Changed += OnDocumentChanged;
        Canvas.SelectionChanged += ShowInspector;
        center.AddChild(Canvas);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        main.AddChild(right);
        right.AddChild(ClientStyle.Text("Inspector", 15, ClientStyle.Header));
        var inspectorScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) };
        right.AddChild(inspectorScroll);
        Inspector = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inspectorScroll.AddChild(Inspector);
        right.AddChild(new HSeparator());
        right.AddChild(ClientStyle.Text("Variables", 15, ClientStyle.Header));
        Variables = new ItemList { CustomMinimumSize = new Vector2(0, 90) };
        right.AddChild(Variables);
        var variableButtons = new HBoxContainer();
        variableButtons.AddChild(ToolbarButton("Add", AddVariable, "Add a module variable"));
        variableButtons.AddChild(ToolbarButton("Remove", RemoveVariable, "Remove the selected variable"));
        right.AddChild(variableButtons);
        right.AddChild(ClientStyle.Text("Functions", 15, ClientStyle.Header));
        Functions = new ItemList { CustomMinimumSize = new Vector2(0, 60), AllowReselect = true };
        Functions.ItemActivated += index => OpenFunction((string)Functions.GetItemMetadata((int)index));
        right.AddChild(Functions);
        var functionButtons = new HBoxContainer();
        functionButtons.AddChild(ToolbarButton("New", AddFunction, "Add a user function"));
        functionButtons.AddChild(ToolbarButton("Edit…", EditFunction, "Rename and edit parameters/returns"));
        functionButtons.AddChild(ToolbarButton("Delete", RemoveFunction, "Delete the selected function"));
        functionButtons.AddChild(ToolbarButton("Events", ShowEventGraph, "Return to the event graph"));
        right.AddChild(functionButtons);

        var bottom = new HSplitContainer { CustomMinimumSize = new Vector2(0, 190) };
        Body.AddChild(bottom);
        var problems = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        bottom.AddChild(problems);
        ProblemsTitle = ClientStyle.Text("Problems (0)", 15, ClientStyle.Header);
        problems.AddChild(ProblemsTitle);
        Problems = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Problems.ItemSelected += index => OpenProblem((int)index);
        problems.AddChild(Problems);

        var preview = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bottom.AddChild(preview);
        var previewHeader = new HBoxContainer();
        previewHeader.AddChild(ClientStyle.Text("Lua preview", 15, ClientStyle.Header));
        var copy = new Button { Text = "Copy", TooltipText = "Copy the Lua", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        copy.Pressed += () => DisplayServer.ClipboardSet(Preview.Text);
        previewHeader.AddChild(copy);
        preview.AddChild(previewHeader);
        Preview = new CodeEdit
        {
            Editable = false,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true,
            ScrollFitContentHeight = false
        };
        Preview.AddThemeFontSizeOverride("font_size", 12);
        preview.AddChild(Preview);

        CloseRequested += AttemptClose;
        NewDocument("blank");
        FillPalette("");
    }

    public ScriptHostProfile Host { get; set; } = AcScriptHosts.Profile;

    /// <summary>Absolute workspace directory (the UI globalises the saved <c>user://</c> path).</summary>
    public string WorkspaceDirectory { get; set; } = "";

    /// <summary>Absolute ALE <c>lua_scripts</c> directory; empty means not configured.</summary>
    public string ExportDirectory { get; set; } = "";

    public bool AutoExportOnSave { get; set; }

    /// <summary>Status and error text; the viewer routes this to the status bar.</summary>
    public Action<string>? Report { get; set; }

    /// <summary>Persists the folder choices (the viewer writes them to UserSettings).</summary>
    public Action<string, string, bool>? SaveFolders { get; set; }

    public ScriptDocumentSession Session { get; } = new();

    /// <summary>The function graph currently on the canvas, or null for the document's event graph.</summary>
    public ScriptFunction? CurrentFunction { get; private set; }

    public ScriptGraphCanvas Canvas { get; }

    public ItemList Documents { get; }

    public LineEdit PaletteSearchField { get; }

    public ItemList PaletteResults { get; }

    public VBoxContainer Inspector { get; }

    public ItemList Variables { get; }

    public ItemList Functions { get; }

    public ItemList Problems { get; }

    public Label ProblemsTitle { get; }

    public CodeEdit Preview { get; }

    public Label HostLabel { get; }

    public OptionButton HostPicker { get; }

    public Label StatusLabel { get; }

    ScriptWorkspace Workspace => new(WorkspaceDirectory);

    ScriptExportService Exporter => new(new ScriptCompiler(Host));

    /// <summary>Refresh the document list and palette, then show the window.</summary>
    public void OpenEditor()
    {
        RefreshDocuments();
        if (Session.Path is not null)
        {
            RefreshTitle();
        }

        Open();
    }

    public void NewDocument(string template)
    {
        var document = ScriptTemplateLibrary.Create(template);
        Session.Load(document);
        stamp = null;
        RefreshAll();
        Status($"New script '{document.Name}'. Drag from the palette to start.");
    }

    public bool OpenDocument(string path)
    {
        try
        {
            var document = Workspace.Load(path);
            stamp = Workspace.Stamp(path);
            Session.Load(document, path);
            RefreshAll();
            Status($"Opened '{document.Name}'.");
            return true;
        }
        catch (ScriptWorkspaceException error)
        {
            Status(error.Message);
            return false;
        }
    }

    /// <summary>Save to the workspace; a new document gets a slug file, an existing one keeps its path.</summary>
    public bool SaveDocument()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceDirectory))
        {
            Status("Choose a workspace folder first.");
            return false;
        }

        try
        {
            string path = Workspace.Save(Session.Document, stamp);
            stamp = Workspace.Stamp(path);
            Session.MarkSaved(path);
            RefreshDocuments();
            RefreshTitle();
            Status("Saved.");
            if (AutoExportOnSave && !HasErrors())
            {
                ExportCurrent();
            }

            return true;
        }
        catch (ScriptWorkspaceException error)
        {
            Status(error.Message);
            return false;
        }
    }

    public ScriptCompilation Compile()
    {
        string source = Session.Path is null ? "script.acwscript" : Path.GetFileName(Session.Path);
        var result = new ScriptCompiler(Host).Compile(Session.Document, source);
        if (result.LuaText is not null)
        {
            Preview.Text = result.LuaText;
        }

        FillProblems(result.Diagnostics);
        return result;
    }

    public bool ExportCurrent()
    {
        if (string.IsNullOrWhiteSpace(ExportDirectory))
        {
            Status("Choose the server's lua_scripts folder first.");
            return false;
        }

        var result = Exporter.Export(Session.Document, ExportDirectory);
        if (!result.Success)
        {
            FillProblems(result.Diagnostics);
            Status(result.Error ?? "Export failed.");
            return false;
        }

        FillProblems(result.Diagnostics);
        Status($"Exported {result.Files.Count} file. Reload with .reload ale.{OrphanNote()}");
        return true;
    }

    string OrphanNote()
    {
        try
        {
            int orphans = ScriptManifest.Load(ExportDirectory).Orphans(ExportDirectory).Count;
            return orphans == 0 ? "" : $" {orphans} manifest entr{(orphans == 1 ? "y has" : "ies have")} no file.";
        }
        catch (ScriptManifestException)
        {
            return " The export manifest could not be read.";
        }
    }

    void RevealExportFolder()
    {
        if (Directory.Exists(ExportDirectory))
        {
            OS.ShellOpen(ExportDirectory);
            Status("Opened the export folder.");
        }
        else
        {
            Status("The export folder does not exist yet.");
        }
    }

    public bool ExportAllDocument()
    {
        if (string.IsNullOrWhiteSpace(ExportDirectory))
        {
            Status("Choose the server's lua_scripts folder first.");
            return false;
        }

        try
        {
            var documents = Workspace.Scan().Files.Select(file => Workspace.Load(file.Path)).ToList();
            var result = Exporter.ExportAll(documents, ExportDirectory);
            if (!result.Success)
            {
                FillProblems(result.Diagnostics);
                Status(result.Error ?? "Export failed.");
                return false;
            }

            Status($"Exported {result.Files.Count} files. Reload with .reload ale.");
            return true;
        }
        catch (ScriptWorkspaceException error)
        {
            Status(error.Message);
            return false;
        }
    }

    public bool Undo()
    {
        if (!Canvas.Undo())
        {
            return false;
        }

        Status("Undo.");
        return true;
    }

    public bool Redo()
    {
        if (!Canvas.Redo())
        {
            return false;
        }

        Status("Redo.");
        return true;
    }

    /// <summary>Add a palette entry at the view centre and select it; returns the new node id.</summary>
    public string? AddFromPalette(string kind)
    {
        Vector2 centre = Canvas.ScrollOffset + Canvas.Size * 0.5f / Canvas.Zoom;
        string? id = Canvas.AddNode(kind, centre);
        if (id is not null)
        {
            Canvas.SelectNode(id);
            Status($"Added {DisplayName(kind)}.");
        }

        return id;
    }

    public IReadOnlyList<string> PaletteKinds(string search)
    {
        string needle = search.Trim();
        var entries = new List<(string Text, string Kind)>();
        foreach (var capability in Host.Capabilities.Values.Where(item => item.Availability == ScriptAvailability.Implemented))
        {
            entries.Add(($"{capability.Category} · {capability.Display}", capability.Id));
        }

        foreach (var definition in Host.Events.Values.Where(item => item.Availability == ScriptAvailability.Implemented))
        {
            if (CurrentFunction is null)
            {
                entries.Add(($"Event · {definition.Display}", definition.Id));
            }
        }

        foreach (var builtin in ScriptBuiltinCatalog.All)
        {
            entries.Add(($"{builtin.Category} · {builtin.Display}", builtin.Id));
        }

        return entries
            .Where(entry => needle.Length == 0
                || entry.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || entry.Kind.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Text, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Kind)
            .ToList();
    }

    public void FillPalette(string search)
    {
        PaletteResults.Clear();
        paletteKinds.Clear();
        foreach (string kind in PaletteKinds(search))
        {
            int index = PaletteResults.AddItem(DisplayName(kind, withCategory: true));
            PaletteResults.SetItemMetadata(index, kind);
            paletteKinds.Add(kind);
        }
    }

    /// <summary>Rebuild the inspector for a node (null clears it).</summary>
    public void ShowInspector(string? nodeId)
    {
        foreach (Node child in Inspector.GetChildren())
        {
            Inspector.RemoveChild(child);
            child.QueueFree();
        }

        var node = nodeId is null ? null : Session.Document.Graph.Nodes.FirstOrDefault(item => item.Id == nodeId);
        if (node is null || nodeId is null)
        {
            Inspector.AddChild(ClientStyle.Text("Select a node to edit its values.", 13, ClientStyle.Faded));
            return;
        }

        Inspector.AddChild(ClientStyle.Text(DisplayName(node.Kind), 16, ClientStyle.Header));
        Inspector.AddChild(ClientStyle.Text(node.Kind, 12, ClientStyle.Faded));

        if (Host.Event(node.Kind) is { } definition)
        {
            foreach (var scope in definition.ScopeParams)
            {
                AddScopeField(node, scope.Name, scope.Type);
            }
        }

        if (node.Properties.ContainsKey("shots"))
        {
            AddScopeField(node, "shots", ScriptValueType.Int);
        }

        if (node.Kind is ScriptBuiltinCatalog.VariableGet or ScriptBuiltinCatalog.VariableSet)
        {
            AddVariableSelector(node);
        }

        if (node.Kind == ScriptBuiltinCatalog.ParameterGet)
        {
            AddParameterSelector(node);
        }

        foreach (var pin in ScriptPins.Build(Session.Document, Host, node).Where(pin => pin.Direction == ScriptPinDirection.In && pin.Type != ScriptValueType.Exec))
        {
            bool connected = Session.Document.Graph.Edges.Any(edge => edge.To.Node == node.Id && edge.To.Pin == pin.Id);
            if (connected)
            {
                Inspector.AddChild(ClientStyle.Text($"{pin.Id}: connected", 12, ClientStyle.Faded));
                continue;
            }

            AddLiteralField(node, pin);
        }
    }

    void AddLiteralField(ScriptNode node, ScriptPin pin)
    {
        bool has = node.Literals.TryGetValue(pin.Id, out var literal);
        switch (pin.Type)
        {
            case ScriptValueType.Bool:
            {
                var toggle = new CheckBox { Text = pin.Id, Name = "Literal_" + pin.Id };
                toggle.ButtonPressed = has && literal.ValueKind == JsonValueKind.True;
                toggle.Toggled += on => SetLiteral(node.Id, pin.Id, JsonSerializer.SerializeToElement(on));
                Inspector.AddChild(toggle);
                break;
            }
            case ScriptValueType.String:
            {
                var field = new LineEdit
                {
                    Name = "Literal_" + pin.Id,
                    PlaceholderText = pin.Id,
                    Text = has && literal.ValueKind == JsonValueKind.String ? literal.GetString() ?? "" : ""
                };
                field.TextSubmitted += text => SetLiteral(node.Id, pin.Id, JsonSerializer.SerializeToElement(text));
                field.FocusExited += () => SetLiteral(node.Id, pin.Id, JsonSerializer.SerializeToElement(field.Text));
                Inspector.AddChild(new Label { Text = pin.Id });
                Inspector.AddChild(field);
                break;
            }
            case ScriptValueType.Int or ScriptValueType.Float
                or ScriptValueType.CreatureEntry or ScriptValueType.GameObjectEntry
                or ScriptValueType.ItemEntry or ScriptValueType.SpellId or ScriptValueType.QuestId
                or ScriptValueType.MapId or ScriptValueType.InstanceId or ScriptValueType.PhaseMask
                or ScriptValueType.FactionId or ScriptValueType.ObjectGuid or ScriptValueType.GuidLow:
            {
                var field = new LineEdit
                {
                    Name = "Literal_" + pin.Id,
                    PlaceholderText = pin.Id,
                    Text = has && literal.ValueKind == JsonValueKind.Number ? literal.GetRawText() : ""
                };
                field.TextSubmitted += text => SetNumberLiteral(node.Id, pin.Id, pin.Type, text);
                field.FocusExited += () => SetNumberLiteral(node.Id, pin.Id, pin.Type, field.Text);
                Inspector.AddChild(new Label { Text = pin.Id });
                Inspector.AddChild(field);
                break;
            }
            default:
            {
                var field = new LineEdit
                {
                    Name = "Literal_" + pin.Id,
                    PlaceholderText = pin.Id + " (JSON, or empty for nil)",
                    Text = has ? literal.GetRawText() : ""
                };
                field.TextSubmitted += text => SetRawLiteral(node.Id, pin.Id, text);
                field.FocusExited += () => SetRawLiteral(node.Id, pin.Id, field.Text);
                Inspector.AddChild(new Label { Text = pin.Id });
                Inspector.AddChild(field);
                break;
            }
        }
    }

    void AddScopeField(ScriptNode node, string name, ScriptValueType type)
    {
        node.Properties.TryGetValue(name, out var current);
        var field = new LineEdit
        {
            Name = "Scope_" + name,
            PlaceholderText = name,
            Text = current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? "",
                JsonValueKind.Undefined => "",
                _ => current.GetRawText()
            }
        };
        void Commit(string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            JsonElement value = type is ScriptValueType.String or ScriptValueType.Any
                ? JsonSerializer.SerializeToElement(trimmed)
                : long.TryParse(trimmed, out long number)
                    ? JsonSerializer.SerializeToElement(number)
                    : JsonSerializer.SerializeToElement(trimmed);
            Canvas.Session!.Edit(document =>
            {
                var target = Canvas.Graph.Nodes.First(item => item.Id == node.Id);
                target.Properties[name] = value;
            });
            Canvas.RebuildFromSession();
        }

        field.TextSubmitted += Commit;
        field.FocusExited += () => Commit(field.Text);
        Inspector.AddChild(new Label { Text = name });
        Inspector.AddChild(field);
    }

    void AddVariableSelector(ScriptNode node)
    {
        var picker = new OptionButton { Name = "Variable" };
        string selected = ScriptPins.PropertyString(node, "variable") ?? "";
        for (int i = 0; i < Session.Document.Variables.Count; i++)
        {
            var variable = Session.Document.Variables[i];
            picker.AddItem(variable.Name.Length == 0 ? variable.Id : variable.Name);
            picker.SetItemMetadata(i, variable.Id);
            if (variable.Id == selected)
            {
                picker.Selected = i;
            }
        }

        picker.ItemSelected += index => Canvas.SetProperty(node.Id, "variable", JsonSerializer.SerializeToElement((string)picker.GetItemMetadata((int)index)));
        Inspector.AddChild(picker);
    }

    void AddParameterSelector(ScriptNode node)
    {
        var picker = new OptionButton { Name = "Parameter" };
        var parameters = CurrentFunction?.Parameters ?? [];
        for (int i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            picker.AddItem(parameter.Name);
            picker.SetItemMetadata(i, parameter.Id);
            if (parameter.Id == ScriptPins.PropertyString(node, "parameter"))
            {
                picker.Selected = i;
            }
        }

        picker.ItemSelected += index => Canvas.SetProperty(node.Id, "parameter",
            JsonSerializer.SerializeToElement((string)picker.GetItemMetadata((int)index)));
        Inspector.AddChild(picker);
    }

    void SetLiteral(string nodeId, string pinId, JsonElement value)
    {
        Canvas.SetLiteral(nodeId, pinId, value);
        Status($"Set {pinId}.");
    }

    void SetNumberLiteral(string nodeId, string pinId, ScriptValueType type, string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            Canvas.ClearLiteral(nodeId, pinId);
            return;
        }

        if (type == ScriptValueType.Float && double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double number))
        {
            SetLiteral(nodeId, pinId, JsonSerializer.SerializeToElement(number));
        }
        else if (long.TryParse(trimmed, out long integer))
        {
            SetLiteral(nodeId, pinId, JsonSerializer.SerializeToElement(integer));
        }
        else
        {
            Status($"{pinId} needs a number.");
        }
    }

    void SetRawLiteral(string nodeId, string pinId, string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            Canvas.ClearLiteral(nodeId, pinId);
            return;
        }

        try
        {
            using var parsed = JsonDocument.Parse(trimmed);
            SetLiteral(nodeId, pinId, parsed.RootElement.Clone());
        }
        catch (JsonException)
        {
            Status($"{pinId} needs JSON.");
        }
    }

    void FillProblems(IReadOnlyList<ScriptDiagnostic> diagnostics)
    {
        Problems.Clear();
        foreach (var diagnostic in diagnostics)
        {
            int index = Problems.AddItem($"{diagnostic.Code}  {diagnostic.Message}");
            Problems.SetItemMetadata(index, diagnostic.NodeId ?? "");
            Problems.SetItemCustomFgColor(index, diagnostic.IsError ? ClientStyle.Error
                : diagnostic.Severity == ScriptSeverity.Warning ? ClientStyle.Warning : ClientStyle.Faded);
        }

        int errors = diagnostics.Count(diagnostic => diagnostic.IsError);
        ProblemsTitle.Text = $"Problems ({diagnostics.Count})" + (errors > 0 ? $" — {errors} error(s)" : "");
    }

    public void OpenProblem(int index)
    {
        if (index < 0 || index >= Problems.ItemCount)
        {
            return;
        }

        string nodeId = (string)Problems.GetItemMetadata(index);
        if (nodeId.Length > 0)
        {
            Canvas.SelectNode(nodeId);
            Canvas.CenterOnNode(nodeId);
        }
    }

    bool HasErrors()
    {
        var result = Compile();
        if (!result.Success)
        {
            Status("Fix the errors before exporting.");
        }

        return !result.Success;
    }

    void OnDocumentChanged()
    {
        RefreshTitle();
        Compile();
        ShowInspector(Canvas.SelectedNodeId);
    }

    void RefreshAll()
    {
        CurrentFunction = null;
        Canvas.Load(Session);
        RefreshDocuments();
        RefreshVariables();
        RefreshFunctions();
        RefreshTitle();
        ShowInspector(Canvas.SelectedNodeId);
        Compile();
    }

    void RefreshDocuments()
    {
        Documents.Clear();
        if (string.IsNullOrWhiteSpace(WorkspaceDirectory))
        {
            return;
        }

        foreach (var file in Workspace.Scan().Files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            int index = Documents.AddItem(file.Name);
            Documents.SetItemMetadata(index, file.Path);
            if (file.Path == Session.Path)
            {
                Documents.Select(index);
            }
        }
    }

    /// <summary>Create an empty function with its Return anchor and open its graph.</summary>
    public void AddFunction()
    {
        string id = "f" + (Session.Document.Functions.Count + 1);
        Session.Edit(document => document.Functions.Add(new ScriptFunction
        {
            Id = id,
            Name = "function" + (document.Functions.Count + 1),
            Graph = new ScriptGraph
            {
                Nodes =
                {
                    new ScriptNode
                    {
                        Id = "n1",
                        Kind = ScriptBuiltinCatalog.Return,
                        Pins = [new ScriptPin { Id = "exec", Direction = ScriptPinDirection.In, Type = ScriptValueType.Exec }]
                    }
                }
            }
        }));
        RefreshFunctions();
        OpenFunction(id);
        Status("Added a function.");
    }

    /// <summary>Show one function's graph, or the event graph when <paramref name="functionId"/> is null.</summary>
    public void OpenFunction(string? functionId)
    {
        var function = functionId is null ? null : Session.Document.Functions.FirstOrDefault(item => item.Id == functionId);
        if (functionId is not null && function is null)
        {
            return;
        }

        CurrentFunction = function;
        if (function is null)
        {
            Canvas.Load(Session);
        }
        else
        {
            Canvas.ShowGraph(function.Graph, function.Id);
        }

        FillPalette(PaletteSearchField.Text);
        RefreshTitle();
        Status(function is null ? "Event graph." : $"Function '{function.Name}'.");
    }

    public void ShowEventGraph() => OpenFunction(null);

    public void RemoveFunction()
    {
        int[] selected = Functions.GetSelectedItems();
        if (selected.Length == 0)
        {
            Status("Select a function first.");
            return;
        }

        string id = (string)Functions.GetItemMetadata(selected[0]);
        Session.Edit(document => document.Functions.RemoveAll(function => function.Id == id));
        if (CurrentFunction?.Id == id)
        {
            OpenFunction(null);
        }

        RefreshFunctions();
        Status("Removed a function.");
    }

    /// <summary>Replace a function's name, parameters and returns; Return nodes get the new slots.</summary>
    public void SetFunctionSignature(
        string functionId,
        string name,
        IReadOnlyList<(string Name, ScriptValueType Type)> parameters,
        IReadOnlyList<(string Name, ScriptValueType Type)> returns)
    {
        Session.Edit(document =>
        {
            var function = document.Functions.First(item => item.Id == functionId);
            function.Name = name;
            function.Parameters = parameters.Select((parameter, index) => new ScriptParameter
            {
                Id = index < function.Parameters.Count ? function.Parameters[index].Id : $"p{index + 1}",
                Name = parameter.Name,
                Type = parameter.Type
            }).ToList();
            function.Returns = returns.Select(returnSlot => new ScriptReturn
            {
                Name = returnSlot.Name,
                Type = returnSlot.Type
            }).ToList();
            foreach (var node in function.Graph.Nodes.Where(node => node.Kind == ScriptBuiltinCatalog.Return))
            {
                node.Pins =
                [
                    new ScriptPin { Id = "exec", Direction = ScriptPinDirection.In, Type = ScriptValueType.Exec },
                    .. function.Returns.Select(slot => new ScriptPin
                    {
                        Id = slot.Name,
                        Direction = ScriptPinDirection.In,
                        Type = slot.Type
                    })
                ];
            }
        });
        Canvas.RebuildFromSession();
        RefreshFunctions();
        RefreshTitle();
        Compile();
        Status($"Updated '{name}'.");
    }

    void EditFunction()
    {
        int[] selected = Functions.GetSelectedItems();
        if (selected.Length == 0)
        {
            Status("Select a function first.");
            return;
        }

        string id = (string)Functions.GetItemMetadata(selected[0]);
        var function = Session.Document.Functions.First(item => item.Id == id);
        var dialog = new AcceptDialog { Title = "Function", Size = new Vector2I(620, 520) };
        var form = new VBoxContainer();
        dialog.AddChild(form);
        var nameField = new LineEdit { Text = function.Name };
        form.AddChild(new Label { Text = "Name" });
        form.AddChild(nameField);
        var parameterRows = new VBoxContainer();
        var returnRows = new VBoxContainer();
        var parameterEditors = new List<(LineEdit Name, OptionButton Type)>();
        var returnEditors = new List<(LineEdit Name, OptionButton Type)>();

        void AddRow(VBoxContainer rows, List<(LineEdit, OptionButton)> editors, string name, ScriptValueType type)
        {
            var row = new HBoxContainer();
            var field = new LineEdit { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var picker = TypePicker(type);
            var remove = new Button { Text = "Remove" };
            row.AddChild(field);
            row.AddChild(picker);
            row.AddChild(remove);
            rows.AddChild(row);
            var editor = (field, picker);
            editors.Add(editor);
            remove.Pressed += () =>
            {
                editors.Remove(editor);
                rows.RemoveChild(row);
                row.QueueFree();
            };
        }

        form.AddChild(new Label { Text = "Parameters" });
        form.AddChild(parameterRows);
        form.AddChild(ToolbarButton("Add parameter", () =>
            AddRow(parameterRows, parameterEditors, "argument", ScriptValueType.Any), "Add a parameter"));
        form.AddChild(new Label { Text = "Returns" });
        form.AddChild(returnRows);
        form.AddChild(ToolbarButton("Add return", () =>
            AddRow(returnRows, returnEditors, "result", ScriptValueType.Int), "Add a return slot"));
        foreach (var parameter in function.Parameters)
        {
            AddRow(parameterRows, parameterEditors, parameter.Name, parameter.Type);
        }

        foreach (var returnSlot in function.Returns)
        {
            AddRow(returnRows, returnEditors, returnSlot.Name, returnSlot.Type);
        }

        dialog.Confirmed += () =>
        {
            SetFunctionSignature(id, nameField.Text.Trim(),
                parameterEditors.Select(editor => (editor.Name.Text.Trim(), TypeOf(editor.Type))).ToList(),
                returnEditors.Select(editor => (editor.Name.Text.Trim(), TypeOf(editor.Type))).ToList());
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    static OptionButton TypePicker(ScriptValueType selected)
    {
        var picker = new OptionButton();
        var types = new[]
        {
            ScriptValueType.Any, ScriptValueType.Bool, ScriptValueType.Int, ScriptValueType.Float,
            ScriptValueType.String, ScriptValueType.Unit, ScriptValueType.Player,
            ScriptValueType.Creature, ScriptValueType.GameObject, ScriptValueType.ObjectGuid,
            ScriptValueType.SpellId, ScriptValueType.ItemEntry, ScriptValueType.QuestId, ScriptValueType.MapId
        };
        for (int i = 0; i < types.Length; i++)
        {
            picker.AddItem(ScriptValueTypeNames.ToJson(types[i]));
            picker.SetItemMetadata(i, types[i].ToString());
            if (types[i] == selected)
            {
                picker.Selected = i;
            }
        }

        return picker;
    }

    static ScriptValueType TypeOf(OptionButton picker) =>
        Enum.TryParse<ScriptValueType>((string)picker.GetItemMetadata(picker.Selected), out var type)
            ? type
            : ScriptValueType.Any;

    public void AddComment()
    {
        Vector2 centre = Canvas.ScrollOffset + Canvas.Size * 0.5f / Canvas.Zoom;
        if (Canvas.AddComment(centre) is not null)
        {
            Status("Added a comment frame.");
        }
    }

    void RefreshVariables()    {
        Variables.Clear();
        foreach (var variable in Session.Document.Variables)
        {
            int index = Variables.AddItem($"{variable.Name}: {ScriptValueTypeNames.ToJson(variable.Type)}");
            Variables.SetItemMetadata(index, variable.Id);
        }
    }

    void RefreshFunctions()
    {
        Functions.Clear();
        foreach (var function in Session.Document.Functions)
        {
            int index = Functions.AddItem(function.Name);
            Functions.SetItemMetadata(index, function.Id);
        }
    }

    void RefreshTitle()
    {
        string where = CurrentFunction is null ? "" : $" › {CurrentFunction.Name}";
        TitleLabel.Text = $"Script editor — {Session.Document.Name}{where}{(Session.Dirty ? " *" : "")}";
    }

    void AddVariable()
    {
        string id = "v" + (Session.Document.Variables.Count + 1);
        Session.Edit(document => document.Variables.Add(new ScriptVariable
        {
            Id = id,
            Name = "variable" + (document.Variables.Count + 1),
            Type = ScriptValueType.Int,
            Default = JsonSerializer.SerializeToElement(0)
        }));
        Canvas.RebuildFromSession();
        RefreshVariables();
        Status("Added a variable.");
    }

    void RemoveVariable()
    {
        int[] selected = Variables.GetSelectedItems();
        if (selected.Length == 0)
        {
            Status("Select a variable first.");
            return;
        }

        string id = (string)Variables.GetItemMetadata(selected[0]);
        Session.Edit(document => document.Variables.RemoveAll(variable => variable.Id == id));
        Canvas.RebuildFromSession();
        RefreshVariables();
        Status("Removed a variable.");
    }

    void UndoAction()
    {
        Undo();
    }

    public void AttemptClose()
    {
        if (!Session.Dirty)
        {
            Close();
            return;
        }

        // A headless run has no user to answer the modal; keep the window open instead.
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        Dialogs.Confirm(this, "Unsaved script",
            $"'{Session.Document.Name}' has unsaved changes. Close and discard them?",
            () =>
            {
                Close();
                Session.Load(ScriptDocument.Create("Untitled"));
                RefreshAll();
            },
            ok: "Discard changes", cancel: "Keep editing");
    }

    /// <summary>Switch the compilation host; the document records the choice and is re-validated.</summary>
    public bool ChangeHost(string hostId)
    {
        var profile = ScriptHostRegistry.Profile(hostId);
        if (profile is null || profile.Host == Host.Host)
        {
            return false;
        }

        Host = profile;
        Canvas.Host = profile;
        HostLabel.Text = "Host: " + profile.Label;
        Session.Edit(document => document.Host = profile.Host);
        FillPalette(PaletteSearchField.Text);
        Canvas.RebuildFromSession();
        RefreshTitle();
        Compile();
        Status($"Compiling for {profile.Label}.");
        return true;
    }

    public void Status(string message)
    {
        StatusLabel.Text = message;
        Report?.Invoke(message);
    }

    /// <summary>Apply folder choices from the dialog (or a test) and persist them.</summary>
    public void ApplyFolders(string workspace, string export, bool autoExport)
    {
        WorkspaceDirectory = workspace;
        ExportDirectory = export;
        AutoExportOnSave = autoExport;
        SaveFolders?.Invoke(workspace, export, autoExport);
        RefreshDocuments();
        Status("Script folders updated.");
    }

    void OpenFolders()
    {
        var dialog = new AcceptDialog { Title = "Script folders", Size = new Vector2I(640, 220) };
        var form = new VBoxContainer();
        dialog.AddChild(form);
        var workspace = new LineEdit { Text = WorkspaceDirectory, PlaceholderText = "Workspace folder" };
        var export = new LineEdit { Text = ExportDirectory, PlaceholderText = "Server lua_scripts folder" };
        var auto = new CheckBox { Text = "Export after save (only when there are no errors)", ButtonPressed = AutoExportOnSave };
        form.AddChild(new Label { Text = "Workspace (documents)" });
        form.AddChild(FolderRow(workspace, dialog));
        form.AddChild(new Label { Text = "Export folder (ALE lua_scripts)" });
        form.AddChild(FolderRow(export, dialog));
        form.AddChild(auto);
        dialog.Confirmed += () => ApplyFolders(workspace.Text.Trim(), export.Text.Trim(), auto.ButtonPressed);
        dialog.Canceled += dialog.QueueFree;
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    static HBoxContainer FolderRow(LineEdit field, Window owner)
    {
        var row = new HBoxContainer();
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(field);
        var browse = new Button { Text = "Browse…" };
        browse.Pressed += () =>
        {
            var picker = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenDir,
                Access = FileDialog.AccessEnum.Filesystem,
                Size = new Vector2I(760, 520)
            };
            picker.DirSelected += path =>
            {
                field.Text = path;
                picker.QueueFree();
            };
            picker.Canceled += () => picker.QueueFree();
            owner.AddChild(picker);
            picker.PopupCentered();
        };
        row.AddChild(browse);
        return row;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        bool ctrl = key.CtrlPressed;
        bool shift = key.ShiftPressed;
        switch (key.Keycode)
        {
            case Key.Escape:
                AttemptClose();
                break;
            case Key.S when ctrl && !shift:
                SaveDocument();
                break;
            case Key.S when ctrl && shift:
                ExportCurrent();
                break;
            case Key.E when ctrl && shift:
                ExportCurrent();
                break;
            case Key.Z when ctrl && !shift:
                UndoAction();
                break;
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Enter when ctrl:
                Compile();
                break;
            case Key.F when ctrl:
                PaletteSearchField.GrabFocus();
                break;
            case Key.Home:
                Canvas.FrameAll();
                break;
            case Key.Key0 when ctrl:
                Canvas.Zoom = 1;
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    static string DisplayName(string kind, bool withCategory = false)
    {
        var host = AcScriptHosts.Profile;
        if (host.Capability(kind) is { } capability)
        {
            return withCategory ? $"{capability.Category} · {capability.Display}" : capability.Display;
        }

        if (host.Event(kind) is { } definition)
        {
            return withCategory ? $"Event · {definition.Display}" : definition.Display;
        }

        if (ScriptBuiltinCatalog.Find(kind) is { } builtin)
        {
            return withCategory ? $"{builtin.Category} · {builtin.Display}" : builtin.Display;
        }

        return kind;
    }

    static Button ToolbarButton(string text, Action action, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None
        };
        button.Pressed += action;
        return button;
    }
}
