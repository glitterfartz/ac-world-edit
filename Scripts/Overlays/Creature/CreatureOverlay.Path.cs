using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The selected creature's waypoint path: which path it walks (its own addon or its template's),
/// who else uses it, and the link/unlink/new/delete commands. All commands are single undoable
/// row-edit commands on the shared content draft; a delete only proceeds when nothing else uses
/// the path, unlinking this spawn in the same command.
/// </summary>
public partial class CreatureOverlay
{
    /// <summary>The path selected in the waypoint tab, for "link to selected" (wired by the shell).</summary>
    public Func<uint?>? WaypointSelection { get; set; }

    /// <summary>Fired after a new path is created so the shell can show it in the waypoint tab.</summary>
    public event Action<uint>? PathCreated;

    Label pathStatus = null!, pathTemplate = null!;
    LineEdit pathField = null!;
    Button pathLink = null!, pathUnlink = null!, pathNew = null!, pathDelete = null!, pathUseSelected = null!;
    Button pathTemplateLink = null!, pathTemplateUnlink = null!;
    int pathGeneration;
    ContentDrafts? sharedDrafts;

    void BuildPathCard(VBoxContainer page)
    {
        var card = InspectorUi.Card(page, "Path");
        pathStatus = InspectorUi.Text("Select a creature to see its path.", 12, true);
        card.AddChild(pathStatus);
        var row = new HBoxContainer();
        card.AddChild(row);
        pathField = new LineEdit { PlaceholderText = "Path ID", CustomMinimumSize = new(110, 0), SelectAllOnFocus = true, MaxLength = 10 };
        row.AddChild(pathField);
        pathLink = InspectorUi.Button("Link", () => LinkToPath());
        pathLink.TooltipText = "Walk the typed path (the path must have at least one point).";
        row.AddChild(pathLink);
        pathUseSelected = InspectorUi.Button("Use selected", UseWaypointSelection);
        pathUseSelected.TooltipText = "Fill in the path selected in the Waypoints tab.";
        row.AddChild(pathUseSelected);
        var actions = new HBoxContainer();
        card.AddChild(actions);
        pathUnlink = InspectorUi.Button("Unlink", () => UnlinkPath());
        pathUnlink.TooltipText = "Stop following the path and stand still (nodes are kept).";
        actions.AddChild(pathUnlink);
        pathNew = InspectorUi.Button("New path", () => NewPath());
        pathNew.TooltipText = "Create a path at this spawn and walk it.";
        actions.AddChild(pathNew);
        pathDelete = InspectorUi.Button("Delete path", () => DeletePath());
        pathDelete.TooltipText = "Delete the walked path and its points. Only when nothing else uses it.";
        actions.AddChild(pathDelete);
        pathTemplate = InspectorUi.Text("", 12, true);
        card.AddChild(pathTemplate);
        var templateRow = new HBoxContainer();
        card.AddChild(templateRow);
        pathTemplateLink = InspectorUi.Button("Link template", () => LinkTemplate());
        pathTemplateLink.TooltipText = "Point this creature's template at the typed path (every spawn without its own path follows it).";
        templateRow.AddChild(pathTemplateLink);
        pathTemplateUnlink = InspectorUi.Button("Unlink template", () => UnlinkTemplate());
        pathTemplateUnlink.TooltipText = "Detach the template from its path.";
        templateRow.AddChild(pathTemplateUnlink);
        RefreshPathCard();
    }

    /// <summary>The paths this spawn and its template name, from the effective rows.</summary>
    (uint Own, uint Template) PathIds(ulong guid, uint entry)
    {
        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        string column = MovementEdits.AddonPathColumn(dialect);
        uint own = poses?.AddonOf(sourceKey, guid) is { } addon && uint.TryParse(addon[column], NumberStyles.None, CultureInfo.InvariantCulture, out uint p) ? p : 0;
        uint template = templateAddons?.RowOf(sourceKey, entry) is { } row && uint.TryParse(row[column], NumberStyles.None, CultureInfo.InvariantCulture, out uint t) ? t : 0;
        return (own, template);
    }

    void RefreshPathCard()
    {
        if (pathStatus == null)
        {
            return;
        }

        if (!selectedGuid.HasValue || poses == null || !poses.Available || group.Members.Count > 0 || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            pathStatus.Text = "Select a creature to see its path.";
            pathTemplate.Text = "";
            lastPathShown = null;
            SetPathActions(false);
            return;
        }

        var (own, template) = PathIds(selectedGuid.Value, marker.Spawn.TemplateId);
        uint path = own != 0 ? own : template;
        bool editable = poses.Has(sourceKey, selectedGuid.Value);
        pathStatus.Text = own != 0 ? $"Path {own} (this spawn). Loading…" : template != 0 ? $"Path {template} (from the template). Loading…" : "No path: this spawn stands still or wanders.";
        pathTemplate.Text = templateAddons?.IsLoaded(sourceKey, marker.Spawn.TemplateId) == true
            ? template != 0 ? $"Template path: {template}." : "Template path: none."
            : "Template path: loading…";
        SetPathActions(editable);
        if (path != 0 && lastPathShown != (selectedGuid.Value, own, template))
        {
            lastPathShown = (selectedGuid.Value, own, template);
            RefreshPathUsers(selectedGuid.Value, own, template, ++pathGeneration);
        }
        else if (path == 0)
        {
            lastPathShown = null;
        }
    }

    (ulong Guid, uint Own, uint Template)? lastPathShown;

    void SetPathActions(bool editable)
    {
        pathLink.Disabled = !editable;
        pathUnlink.Disabled = !editable;
        pathNew.Disabled = !editable;
        pathDelete.Disabled = !editable;
        pathUseSelected.Disabled = !editable;
        pathTemplateLink.Disabled = !editable;
        pathTemplateUnlink.Disabled = !editable;
        pathField.Editable = editable;
    }

    async void RefreshPathUsers(ulong guid, uint own, uint template, int generation)
    {
        if (poses == null)
        {
            return;
        }

        string key = sourceKey;
        var config = Connection();
        try
        {
            uint path = own != 0 ? own : template;
            var source = DBSource.Open(config);
            var rows = await source.ReadWaypointPathRowsAsync(path, default);
            var users = (await source.ReadPathUsersAsync([path], default)).FirstOrDefault();
            if (!IsInsideTree() || generation != pathGeneration || selectedGuid != guid)
            {
                return;
            }

            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            int nodes = DraftNodeTotal(key, schema, path, rows.All(MovementEdits.NodeTable(schema)).Count);
            string walkers = users == null || users.Creatures.Length == 0
                ? own != 0 ? "linked in this session (not exported yet)" : "no creature in this database uses it"
                : string.Join(", ", users.Creatures.Take(3).Select(u => $"{u.Name} #{u.Guid}{(u.Walks ? "" : " (not following)")}"))
                + (users.Creatures.Length > 3 ? $" +{users.Creatures.Length - 3} more" : "");
            string smart = users != null && users.Smart.Length > 0 ? $" · started by {users.Smart.Length} SmartAI rule(s)" : "";
            pathStatus.Text = own != 0 ? $"Path {own} (this spawn) · {nodes} point(s) · {walkers}{smart}."
                : $"Path {template} (from the template) · {nodes} point(s) · {walkers}{smart}.";
        }
        catch (Exception)
        {
            if (IsInsideTree() && generation == pathGeneration && selectedGuid == guid)
            {
                pathStatus.Text = "Could not read the path; try again.";
            }
        }
    }

    /// <summary>Node count with the draft applied (renumbers are delete+insert pairs).</summary>
    int DraftNodeTotal(string key, IWorldSchema schema, uint pathId, int dbCount)
    {
        if (sharedDrafts?.Draft == null)
        {
            return dbCount;
        }

        string table = MovementEdits.NodeTable(schema).Name;
        string id = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        int adds = 0, drops = 0;
        bool touches = false;
        foreach (var (source, edit) in sharedDrafts.Draft.NetEdits())
        {
            if (source != key || edit.Table != table)
            {
                continue;
            }

            bool before = edit.Before != null && edit.Before.U(id) == pathId;
            bool after = edit.After != null && edit.After.U(id) == pathId;
            if (!before && !after)
            {
                continue;
            }

            touches = true;
            if (edit.After == null)
            {
                drops++;
            }
            else if (edit.Before == null)
            {
                adds++;
            }
        }

        return touches ? dbCount + adds - drops : dbCount;
    }

    void UseWaypointSelection()
    {
        if (WaypointSelection?.Invoke() is uint path)
        {
            pathField.Text = path.ToString(CultureInfo.InvariantCulture);
            Say($"Path {path} filled in; Link walks it.");
        }
        else
        {
            Say("Select a point in the Waypoints tab first.");
        }
    }

    void LinkToPath()
    {
        if (!RequireSpawn(out var guid, out var entry, out _) || !uint.TryParse(pathField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint path) || path == 0)
        {
            Say("Type a path ID first (or Use selected).");
            return;
        }

        LinkSpawnToPath(guid, path);
    }

    async void LinkSpawnToPath(ulong guid, uint path)
    {
        if (poses == null || poses.RowOf(sourceKey, guid) is not { } spawn)
        {
            Say("The spawn is still loading; try again.");
            return;
        }

        string key = sourceKey;
        var config = Connection();
        var schema = ContentDraftFiles.Sources.SchemaFor(key);
        var baseline = SpawnEdits.AddonBaseline(schema, SpawnKind.Creature, templateAddons?.RowOf(key, SelectedEntry ?? 0), guid);
        var addon = poses.AddonOf(key, guid);
        try
        {
            var rows = await DBSource.Open(config).ReadWaypointPathRowsAsync(path, default);
            if (!IsInsideTree() || selectedGuid != guid)
            {
                return;
            }

            int nodes = DraftNodeTotal(key, schema, path, rows.All(MovementEdits.NodeTable(schema)).Count);
            if (nodes == 0)
            {
                Say($"Path {path} has no points; add points before linking it.");
                return;
            }

            var edits = MovementEdits.Link(schema, spawn, addon, baseline, guid, path);
            var (ok, message) = poses.Commit(this, key, $"Link to path {path}", edits);
            Say(ok ? (edits.Length == 0 ? $"Already walks path {path}." : $"Walks path {path} now.") : message);
            RefreshSpawnFields(true);
        }
        catch (Exception)
        {
            Say("Could not read the path; try again.");
        }
    }

    void UnlinkPath()
    {
        if (!RequireSpawn(out var guid, out var _, out var spawn))
        {
            return;
        }

        var (own, _) = PathIds(guid, 0);
        if (own == 0 && spawn.U("MovementType") != 2)
        {
            Say("This spawn follows no path of its own.");
            return;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        var baseline = SpawnEdits.AddonBaseline(schema, SpawnKind.Creature, templateAddons?.RowOf(sourceKey, SelectedEntry ?? 0), guid);
        var edits = MovementEdits.Unlink(schema, spawn, poses!.AddonOf(sourceKey, guid), baseline, guid);
        var (ok, message) = poses.Commit(this, sourceKey, "Unlink path", edits);
        Say(ok ? (edits.Length == 0 ? "Already unlinked." : "Unlinked; the spawn stands still now.") : message);
        RefreshSpawnFields(true);
    }

    async void NewPath()
    {
        if (poses == null || !RequireSpawn(out var guid, out var _, out var spawn))
        {
            return;
        }

        string key = sourceKey;
        var config = Connection();
        try
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            var database = DBSource.Open(config);
            var table = MovementEdits.NodeTable(schema);
            string column = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
            ulong fresh = poses.NextPathId(key, schema, await database.MaxIdAsync(table, column, default));
            if (!IsInsideTree() || selectedGuid != guid)
            {
                return;
            }

            var pose = PoseOf(markers[guid].Spawn);
            var ahead = new WaypointSeed(pose.X + 2 * Math.Cos(pose.Heading), pose.Y + 2 * Math.Sin(pose.Heading), pose.Z, null, 0);
            var here = new WaypointSeed(pose.X, pose.Y, pose.Z, null, 0);
            var baseline = SpawnEdits.AddonBaseline(schema, SpawnKind.Creature, templateAddons?.RowOf(key, SelectedEntry ?? 0), guid);
            var edits = MovementEdits.NewPath(schema, (uint)fresh, [here, ahead])
                .Concat(MovementEdits.Link(schema, spawn, poses.AddonOf(key, guid), baseline, guid, (uint)fresh)).ToArray();
            var (ok, message) = poses.Commit(this, key, $"New path {fresh}", edits);
            Say(ok ? $"Created path {fresh} with two points; click the ground in the Waypoints tab to extend it." : message);
            if (ok)
            {
                PathCreated?.Invoke((uint)fresh);
            }

            RefreshSpawnFields(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not create the path: " + error.Message);
        }
    }

    async void DeletePath()
    {
        if (poses == null || !RequireSpawn(out var guid, out var entry, out var spawn))
        {
            return;
        }

        var (own, _) = PathIds(guid, entry);
        if (own == 0)
        {
            Say(spawn.U("MovementType") == 2 ? "This spawn follows its template's path; unlink the template (or give the spawn its own path) first." : "This spawn follows no path of its own.");
            return;
        }

        string key = sourceKey;
        var config = Connection();
        try
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            var database = DBSource.Open(config);
            var rows = await database.ReadWaypointPathRowsAsync(own, default);
            var users = (await database.ReadPathUsersAsync([own], default)).FirstOrDefault();
            if (!IsInsideTree() || selectedGuid != guid)
            {
                return;
            }

            var others = (users?.Creatures.Where(u => u.Guid != guid).Select(u => $"{u.Name} #{u.Guid}").ToList() ?? [])
                .Concat(users?.Smart.Select(s => $"SmartAI {(s.Owner < 0 ? "spawn" : "entry")} {Math.Abs(s.Owner)} action {s.Action}") ?? [])
                .Concat(users?.Creatures.Where(u => u.Guid == guid && u.FromTemplate).Select(u => $"{u.Name} #{u.Guid} (template)") ?? [])
                .ToList();
            if (others.Count > 0)
            {
                Say($"Path {own} is still used by {string.Join(", ", others.Take(3))}; unlink them first.");
                return;
            }

            var nodes = rows.All(MovementEdits.NodeTable(schema)).ToArray();
            var baseline = SpawnEdits.AddonBaseline(schema, SpawnKind.Creature, templateAddons?.RowOf(key, entry), guid);
            var edits = MovementEdits.DeletePath(schema, nodes, MovementEdits.HeaderTable(schema) is { } header ? rows.Get(header, own.ToString(CultureInfo.InvariantCulture)) : null)
                .Concat(MovementEdits.Unlink(schema, spawn, poses.AddonOf(key, guid), baseline, guid)).ToArray();
            var (ok, message) = poses.Commit(this, key, $"Delete path {own}", edits);
            Say(ok ? $"Deleted path {own} ({nodes.Length} point(s))." : message);
            RefreshSpawnFields(true);
        }
        catch (Exception)
        {
            Say("Could not read the path; try again.");
        }
    }

    void LinkTemplate()
    {
        if (templateAddons == null || !RequireSpawn(out _, out var entry, out _))
        {
            return;
        }

        if (!uint.TryParse(pathField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint path) || path == 0)
        {
            Say("Type a path ID first (or Use selected).");
            return;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        string column = MovementEdits.AddonPathColumn(schema.Dialect);
        var (ok, message) = templateAddons.Edit(this, sourceKey, entry, $"Link template to path {path}",
            SpawnEdits.NewTemplateAddon(schema, entry), row => row.With(column, path));
        Say(ok ? $"Template walks path {path} now." : message);
        RefreshSpawnFields(true);
    }

    void UnlinkTemplate()
    {
        if (templateAddons == null || !RequireSpawn(out _, out var entry, out _))
        {
            return;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        string column = MovementEdits.AddonPathColumn(schema.Dialect);
        var (ok, message) = templateAddons.Edit(this, sourceKey, entry, "Unlink template path",
            SpawnEdits.NewTemplateAddon(schema, entry), row => row.With(column, 0u));
        Say(ok ? "Template detached from its path." : message);
        RefreshSpawnFields(true);
    }

    bool RequireSpawn(out ulong guid, out uint entry, out Row spawn)
    {
        guid = 0;
        entry = 0;
        spawn = null!;
        if (poses == null || !selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            Say("Select a creature first.");
            return false;
        }

        if (poses.RowOf(sourceKey, selectedGuid.Value) is not { } row)
        {
            Say("The spawn is still loading; try again.");
            return false;
        }

        guid = selectedGuid.Value;
        entry = marker.Spawn.TemplateId;
        spawn = row;
        return true;
    }
}
