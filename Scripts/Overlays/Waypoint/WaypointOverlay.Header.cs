using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The TrinityCore path header card and the one-time legacy-draft import.
/// </summary>
public partial class WaypointOverlay
{
    Control headerPanel = null!;
    OptionButton headerMove = null!;
    LineEdit headerVelocity = null!, headerFlags = null!, headerComment = null!;
    Label headerUsers = null!;
    int headerGeneration, legacyCount;
    string legacyKey = "";

    void BuildHeaderCard(VBoxContainer page)
    {
        var card = InspectorUi.Card(page, "Path properties");
        headerPanel = (Control)card.GetParent();
        var moveRow = new HBoxContainer();
        card.AddChild(moveRow);
        var moveLabel = InspectorUi.Text("Move type", 12, true);
        moveLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        moveLabel.CustomMinimumSize = new(120, 0);
        moveRow.AddChild(moveLabel);
        headerMove = InspectorUi.Choice(MoveTypes, 0, _ => SubmitHeader(), moveRow,
            "How creatures travel along the whole path.", expand: true, fitToLongest: false);
        headerVelocity = HeaderField(card, "Velocity (yd/s)", "Speed for the whole path; empty means each creature's own speed.");
        headerFlags = HeaderField(card, "Flags", "Path flags bitmask.");
        headerComment = HeaderField(card, "Comment", "Editor note stored on the path.");
        headerUsers = InspectorUi.Text("", 11, true);
        card.AddChild(headerUsers);
        headerPanel.Visible = false;
    }

    LineEdit HeaderField(VBoxContainer card, string caption, string tip)
    {
        var row = new HBoxContainer();
        card.AddChild(row);
        var label = InspectorUi.Text(caption, 12, true);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.CustomMinimumSize = new(120, 0);
        label.TooltipText = tip;
        row.AddChild(label);
        var edit = new LineEdit
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = tip,
            SelectAllOnFocus = true
        };
        edit.TextSubmitted += text => SubmitHeader();
        row.AddChild(edit);
        return edit;
    }

    void SubmitHeader()
    {
        if (!uint.TryParse(headerFlags.Text.Trim(), CultureInfo.InvariantCulture, out uint flags))
        {
            Say("Flags must be a whole number.");
            ShowSelection();
            return;
        }

        string velocityText = headerVelocity.Text.Trim();
        double? velocity = null;
        if (velocityText.Length > 0)
        {
            if (!double.TryParse(velocityText, CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed) || parsed < 0)
            {
                Say("Velocity must be a number, zero or more (empty means each creature's own).");
                ShowSelection();
                return;
            }

            velocity = parsed;
        }

        SetHeader(headerMove.Selected, velocity, flags, headerComment.Text.Trim());
    }

    /// <summary>Fill the header card from the effective header row, or the path record while unloaded.</summary>
    void RefreshHeader(WaypointPath? path)
    {
        bool show = path?.Dialect == WorldDialect.TrinityCore;
        headerPanel.Visible = show;
        if (!show || path == null || shared?.Draft == null)
        {
            return;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var header = MovementEdits.HeaderTable(schema) is { } headerTable
            ? effective?.Get(headerTable, path.Id.ToString(CultureInfo.InvariantCulture)) : null;
        var first = DisplayNodes(path).FirstOrDefault();
        headerMove.Select(Math.Clamp(header?.I("MoveType") ?? first?.MoveType ?? 0, 0, MoveTypes.Length - 1));
        headerVelocity.Text = header != null ? header.IsNull("Velocity") ? "" : header.F("Velocity").ToString("0.####", CultureInfo.InvariantCulture) : "";
        headerVelocity.Editable = true;
        headerFlags.Text = (header?.U("Flags") ?? (uint)path.Flags).ToString(CultureInfo.InvariantCulture);
        headerFlags.Editable = true;
        headerComment.Text = header?.IsNull("Comment") == false ? header.T("Comment") : path.Comment;
        headerComment.Editable = true;
        headerMove.Disabled = false;
        RefreshHeaderUsers(path, ++headerGeneration);
    }

    async void RefreshHeaderUsers(WaypointPath path, int generation)
    {
        string key = sourceKey;
        try
        {
            var users = (await DBSource.Open(Connection()).ReadPathUsersAsync([path.Id], default)).FirstOrDefault();
            if (!IsInsideTree() || generation != headerGeneration || emphasized != path.Id)
            {
                return;
            }

            int spawns = users?.Creatures.Length ?? 0;
            int rules = users?.Smart.Length ?? 0;
            headerUsers.Text = spawns == 0 && rules == 0 ? "No creature or SmartAI rule uses this path."
                : $"Shared by {spawns} spawn(s) and {rules} SmartAI rule(s); every one of them changes together.";
        }
        catch (Exception)
        {
            if (IsInsideTree() && generation == headerGeneration)
            {
                headerUsers.Text = "Could not read who uses this path.";
            }
        }
    }

    /// <summary>
    /// One-time import of the retired waypoint draft: a button appears while the legacy file holds
    /// net moves, converting them into shared-draft row edits against the current records.
    /// </summary>
    void BuildImportButton(VBoxContainer display)
    {
        importLegacy = new Button
        {
            Text = "Import old moves",
            CustomMinimumSize = new(0, 34),
            TooltipText = "Import moves saved by the old waypoint draft into the shared draft."
        };
        importLegacy.Pressed += () => ImportLegacyMoves();
        display.AddChild(importLegacy);
        RefreshLegacyCount();
    }

    void RefreshLegacyCount()
    {
        legacyKey = sourceKey;
        legacyCount = 0;
        if (shared?.Draft != null)
        {
            try
            {
                legacyCount = LegacyWaypointImport.Load(ContentDraftFiles.LocalPath("waypoints.json")).Count;
            }
            catch (Exception)
            {
                legacyCount = -1;
            }
        }

        if (importLegacy == null)
        {
            return;
        }

        importLegacy.Visible = legacyCount != 0;
        importLegacy.Text = legacyCount > 0 ? $"Import {legacyCount} old move(s)" : "Old moves unreadable";
        importLegacy.Disabled = shared?.Draft == null;
    }

    async void ImportLegacyMoves()
    {
        if (shared?.Draft == null)
        {
            return;
        }

        string legacyPath = ContentDraftFiles.LocalPath("waypoints.json");
        if (LegacyWaypointImport.LockHeld(legacyPath))
        {
            Say("Another editor may be using the old waypoint draft; close it and try again.");
            return;
        }

        IReadOnlyList<LegacyWaypointMove> moves;
        try
        {
            moves = LegacyWaypointImport.Load(legacyPath);
        }
        catch (Exception)
        {
            Say("The old waypoint draft is corrupt; it was left alone.");
            RefreshLegacyCount();
            return;
        }

        if (moves.Count == 0)
        {
            Say("No old waypoint moves to import.");
            RefreshLegacyCount();
            return;
        }

        await System.Threading.Tasks.Task.Yield();
        string key = sourceKey;
        if (!IsInsideTree() || key != sourceKey)
        {
            return;
        }

        ImportLegacyEdits(moves, key, legacyPath);
    }

    /// <summary>Convert the legacy moves against the current records and commit them as one command.</summary>
    void ImportLegacyEdits(IReadOnlyList<LegacyWaypointMove> moves, string key, string legacyPath)
    {
        var schema = ContentDraftFiles.Sources.SchemaFor(key);
        var table = MovementEdits.NodeTable(schema);
        var (edits, skipped, conflicted, foreign) = CollectLegacyEdits(moves, key, schema, table);
        if (edits.Count > 0)
        {
            var (ok, message) = shared!.Commit(this, key, Snapshot(key), effective ?? Snapshot(key),
                $"Import {edits.Count} old waypoint move(s)", edits,
                checkRows => SpawnRules.CheckMovement(schema, checkRows));
            Say(ok ? $"Imported {edits.Count} old move(s)." : message);
            if (!ok)
            {
                return;
            }
        }

        if (skipped.Count > 0 || conflicted.Count > 0 || foreign.Count > 0)
        {
            Say($"Import partial: {skipped.Count} point(s) not in this region, {conflicted.Count} changed in the database, {foreign.Count} from another database. The rest was imported.");
            RefreshLegacyCount();
            return;
        }

        try
        {
            LegacyWaypointImport.Clear(legacyPath);
        }
        catch (Exception)
        {
            Say("Imported, but the old file could not be cleared.");
        }

        RefreshLegacyCount();
    }

    /// <summary>Row edits for the legacy moves of one source, with the points that cannot migrate.</summary>
    (List<RowEdit> Edits, List<string> Skipped, List<string> Conflicted, List<LegacyWaypointMove> Foreign) CollectLegacyEdits(
        IReadOnlyList<LegacyWaypointMove> moves, string key, Foundation.IWorldSchema schema, Foundation.TableSchema table)
    {
        var edits = new List<RowEdit>();
        var skipped = new List<string>();
        var conflicted = new List<string>();
        var foreign = moves.Where(m => m.Source != key).ToList();
        foreach (var move in moves.Where(m => m.Source == key))
        {
            if (!lookup.TryGetValue(move.Path, out var path) || DisplayNodes(path).FirstOrDefault(n => n.Point == move.Point) is not { } node)
            {
                skipped.Add($"path {move.Path} point {move.Point}");
                continue;
            }

            var recordRow = DbWaypointRows.RowFromNode(path.Dialect, path.Id, node);
            if (Snapshot(key).Get(table, table.KeyOf(recordRow)) == null && !HasNetEdit(table, table.KeyOf(recordRow)))
            {
                Snapshot(key).Set(table, recordRow);
            }

            var (edit, conflict) = LegacyWaypointImport.ToUpdate(schema, recordRow, move);
            if (conflict)
            {
                conflicted.Add($"path {move.Path} point {move.Point}");
            }
            else if (edit != null)
            {
                edits.Add(edit);
            }
        }

        return (edits, skipped, conflicted, foreign);
    }

    /// <summary>
    /// Parse a typed number and pass it on; bad text shows the message in the status bar and
    /// restores the fields.
    /// </summary>
}
