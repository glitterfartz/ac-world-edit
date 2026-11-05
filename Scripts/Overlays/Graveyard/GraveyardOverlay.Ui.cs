using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Graveyards tab: nearby list, selection fields, zone-link editor and placement actions. Typed
/// values commit on Enter; every change goes through the editing partial's validated drafts.
/// </summary>
public partial class GraveyardOverlay
{
    static readonly (string Name, ushort Value)[] Factions = [("Neutral (0)", GraveyardLink.Neutral), ("Alliance (469)", GraveyardLink.Alliance), ("Horde (67)", GraveyardLink.Horde)];
    ItemList list = null!, zoneResults = null!;
    LineEdit search = null!, comment = null!, zoneSearch = null!;
    readonly LineEdit[] position = new LineEdit[3];
    Label status = null!, title = null!, identity = null!, details = null!;
    VBoxContainer links = null!;
    OptionButton newFaction = null!;
    Button fly = null!, addLink = null!, drop = null!, reset = null!, copyGo = null!, undo = null!, redo = null!;

    public Control BuildPanel()
    {
        var page = InspectorUi.Page("Graveyards", out var scroll);
        (search, list, status, fly) = InspectorUi.ListSection(page, "Graveyards", "game_graveyard + graveyard_zone in the streamed region.",
            "Name or ID…", "Waiting for a world…", id => Select((uint)id), Refresh, FlyToSelection, query.MarkSearchChanged, 170);
        var selection = InspectorUi.SelectionCard(page, "Select a graveyard", "Click an icon or choose one above");
        var card = selection.Box;
        title = selection.Title;
        identity = selection.Identity;
        EditorFields.CoordinateGrid(card, ["X", "Y", "Z"], position, SubmitPosition);

        card.AddChild(InspectorUi.Text("Name (game_graveyard.Comment)", 12, true));
        comment = new LineEdit
        {
            MaxLength = GraveyardRecord.MaxCommentLength,
            SelectAllOnFocus = true
        };
        comment.TextSubmitted += text => SetComment(text);
        card.AddChild(comment);
        (drop, reset, copyGo) = InspectorUi.PlacementButtons(card, this);
        var history = new HBoxContainer();
        card.AddChild(history);
        undo = InspectorUi.Button("Undo", Undo);
        redo = InspectorUi.Button("Redo", Redo);
        history.AddChild(undo);
        history.AddChild(redo);

        var linkCard = InspectorUi.Card(page, "Zone links (graveyard_zone)");
        linkCard.AddChild(InspectorUi.Text("Ghosts who die in a linked area (checked first) or zone are sent here, by faction.", 11, true));
        links = new VBoxContainer();
        linkCard.AddChild(links);
        linkCard.AddChild(new HSeparator());
        linkCard.AddChild(InspectorUi.Text("Add link", 12, true));
        zoneSearch = new LineEdit
        {
            PlaceholderText = "Zone or area name / ID…",
            ClearButtonEnabled = true
        };
        zoneSearch.TextChanged += _ => PopulateZones();
        linkCard.AddChild(zoneSearch);
        zoneResults = new ItemList
        {
            CustomMinimumSize = new(0, 120),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        linkCard.AddChild(zoneResults);
        var addRow = new HBoxContainer();
        linkCard.AddChild(addRow);
        newFaction = FactionPicker(GraveyardLink.Neutral);
        addRow.AddChild(newFaction);
        addLink = InspectorUi.Button("Add link", () =>
        {
            var picked = zoneResults.GetSelectedItems();
            if (picked.Length > 0)
            {
                AddLink(uint.Parse(zoneResults.GetItemMetadata(picked[0]).AsString(), CultureInfo.InvariantCulture), Factions[newFaction.Selected].Value);
            }
        });
        addRow.AddChild(addLink);

        var notes = InspectorUi.Card(page, "Details");
        details = InspectorUi.Text("Selection details appear here.", 12, true);
        notes.AddChild(details);
        ShowSelection();
        return scroll;
    }

    static OptionButton FactionPicker(ushort faction) =>
        InspectorUi.Choice(Factions.Select(f => f.Name).ToArray(), Math.Max(0, Array.FindIndex(Factions, f => f.Value == faction)),
            fitToLongest: false);

    void PopulateList()
    {
        list.Clear();
        foreach (var site in sites)
        {
            var record = RecordOf(site);
            int index = list.AddItem($"{(record.Comment.Length > 0 ? record.Comment : "Graveyard")}   ·   {record.Id}");
            list.SetItemMetadata(index, record.Id.ToString(CultureInfo.InvariantCulture));
            list.SetItemTooltip(index, $"ID {record.Id} · {record.Links.Length} zone link(s)");
            if (selected == record.Id)
            {
                list.Select(index);
            }
        }
    }

    void PopulateZones()
    {
        zoneResults.Clear();
        if (Areas() is not { } areas)
        {
            zoneResults.AddItem("AreaTable unavailable");
            return;
        }

        uint? map = selected.HasValue && lookup.TryGetValue(selected.Value, out var site) ? site.Record.Map : null;
        foreach (var (id, name, parent, zoneMap) in areas.Search(zoneSearch.Text, map))
        {
            int index = zoneResults.AddItem($"{name}{(parent != null ? $" ({parent})" : "")}   ·   {id}{(zoneMap != map ? $"   ·   map {zoneMap}" : "")}");
            zoneResults.SetItemMetadata(index, id.ToString(CultureInfo.InvariantCulture));
        }
    }

    void SubmitPosition()
    {
        if (!EditorFields.TryParseAll(position, out var values))
        {
            Say("Position fields must be numbers (use . for decimals).");
            ShowSelection();
            return;
        }

        SetPosition(values[0], values[1], values[2]);
    }

    /// <summary>
    /// Refresh the selection card and rebuild the link rows from the effective record.
    /// </summary>
    void ShowSelection()
    {
        foreach (var child in links.GetChildren())
        {
            child.QueueFree();
        }

        bool editable = CanEdit;
        if (!selected.HasValue || !lookup.TryGetValue(selected.Value, out var site))
        {
            title.Text = "Select a graveyard";
            identity.Text = "Click an icon or choose one above";
            details.Text = "Selection details appear here.";
            comment.Text = "";
            foreach (var field in position)
            {
                field.Text = "";
                field.Editable = false;
            }

            comment.Editable = false;
            return;
        }

        var record = RecordOf(site);
        title.Text = record.Comment.Length > 0 ? record.Comment : "Graveyard";
        identity.Text = $"ID {record.Id}   /   Map {record.Map}";
        double[] values = [record.X, record.Y, record.Z];
        for (int i = 0; i < 3; i++)
        {
            position[i].Text = values[i].ToString("F4", CultureInfo.InvariantCulture);
            position[i].Editable = editable;
        }

        comment.Text = record.Comment;
        comment.Editable = editable;
        if (record.Links.Length == 0)
        {
            links.AddChild(InspectorUi.Text("No zone links: the server never sends ghosts here.", 12, true));
        }

        var areas = Areas();
        List<string> warnings = [];
        foreach (var link in record.Links)
        {
            uint zone = link.GhostZone;
            string name = ZoneName(zone) is { Length: > 0 } known ? known : $"Area {zone}";
            if (areas?.MapOf(zone) is uint zoneMap && zoneMap != record.Map)
            {
                warnings.Add($"Zone {zone} ({name}) is on map {zoneMap}: ghosts from that map (e.g. an instance) are sent here.");
            }

            var rowBox = new VBoxContainer();
            links.AddChild(rowBox);
            rowBox.AddChild(InspectorUi.Text($"{name}   ·   {zone}", 13));
            var controls = new HBoxContainer();
            rowBox.AddChild(controls);
            bool locked = site.IsLocked(zone);
            var faction = FactionPicker(link.Faction);
            faction.Disabled = !editable || locked;
            faction.TooltipText = locked ? "Battleground or world-state conditions decide this link's team; edit them in the conditions table." : "";
            faction.ItemSelected += index => SetLinkFaction(zone, Factions[index].Value);
            controls.AddChild(faction);
            var linkComment = new LineEdit
            {
                Text = link.Comment,
                PlaceholderText = "Comment",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Editable = editable
            };
            linkComment.TextSubmitted += text => SetLinkComment(zone, text);
            controls.AddChild(linkComment);
            var remove = InspectorUi.Button("Remove", () => RemoveLink(zone));
            remove.Disabled = !editable || locked;
            controls.AddChild(remove);
        }

        string healers = site.Healers.Length == 0
            ? $"No spirit healer within {Data.DbLimits.HealerRadius:0} yd."
            : "Spirit healers (separate creature spawns, not moved with the graveyard): " + string.Join(", ", site.Healers.Select(h => $"{h.Name} #{h.Guid}"));
        if (site.TeleportUses > 0)
        {
            warnings.Add($"Also the destination of {site.TeleportUses} area trigger teleport(s); moving the graveyard moves them.");
        }

        string conflict = draft?.Conflict(sourceKey, site.Record) == true ? "Source graveyard changed since this draft; draft requires review.\n" : "";
        details.Text = $"{conflict}\n{healers}{(warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : "")}";
        InspectorUi.SelectRow(list, record.Id);

        if (zoneResults.ItemCount == 0)
        {
            PopulateZones();
        }
    }

    void UpdateInspectorActions()
    {
        bool editable = CanEdit;
        fly.Disabled = !selected.HasValue;
        drop.Disabled = !editable || GroundAt == null;
        reset.Disabled = !editable;
        copyGo.Disabled = !selected.HasValue;
        addLink.Disabled = !editable || zoneResults.GetSelectedItems().Length == 0;
        undo.Disabled = !CanUndo;
        redo.Disabled = !CanRedo;
    }
}
