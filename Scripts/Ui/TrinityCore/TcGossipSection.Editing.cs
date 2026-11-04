using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using ACWorldEdit.Data;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>Creating, copying and reference-checking gossip rows: new texts, menus and links.</summary>
public sealed partial class TcGossipSection
{
    /// <summary>
    /// The rows for a new broadcast text numbered after the highest in the hotfixes database and this draft;
    /// false, with a message, when that database is not connected.
    /// </summary>
    bool NewText(string text, out uint id, out RowEdit[] edits)
    {
        var lookup = Ctx.Snapshot!.Lookup;
        if (lookup.MaxBroadcastTextId == 0)
        {
            Ctx.Status = "A new text is stored in the hotfixes database, which is not connected.";
            id = 0;
            edits = [];
            return false;
        }

        id = Ctx.NextId(TcTables.BroadcastText, "ID", lookup.MaxBroadcastTextId);
        edits = TcBroadcastText.Create(id, text, (int)Ctx.NextId(TcTables.HotfixData, "Id", (uint)lookup.MaxHotfixId));
        return true;
    }

    /// <summary>Ask for one line of text; <paramref name="accepted"/> gets it trimmed when it is not empty.</summary>
    void PromptText(string title, Action<string> accepted)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = "Create" };
        var text = new LineEdit { Name = "NewText", PlaceholderText = "Text", CustomMinimumSize = new(420, 0) };
        dialog.AddChild(text);
        dialog.Confirmed += () =>
        {
            if (text.Text.Trim() is { Length: > 0 } entered) accepted(entered);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        // The window owns the dialog: the section frees its children whenever it redraws, which a reload can do while this is open.
        GetWindow().AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>A MenuID free in both gossip_menu and gossip_menu_option (the options of a menu can exist without a gossip_menu row).</summary>
    uint NewMenuId() => Math.Max(Ctx.NextId(TcTables.GossipMenu, "MenuID", Ctx.Snapshot!.Lookup.MaxMenuId), Ctx.NextId(TcTables.GossipOption, "MenuID", 0));

    /// <summary>A creature opens gossip only with the gossip npcflag, so a menu given to one without it also sets the flag.</summary>
    void EnsureGossipFlag(TcCreatureView v, List<RowEdit> edits)
    {
        if ((v.NpcFlags & GossipPageSpec.GossipFlag) == 0)
        {
            edits.Add(Update(TcTables.CreatureTemplate, v.Template, Ctx.Schema.GossipPage.EnableGossip(v.Template)));
        }
    }

    Row? RootLink(uint entry, uint menu) => Ctx.Effective.All(TcTables.CreatureGossip).FirstOrDefault(r => r.U("CreatureID") == entry && r.U("MenuID") == menu);

    void CopyMenu(uint entry, TcMenuView menu, Row link)
    {
        uint copy = Math.Max(Ctx.NextId(TcTables.GossipMenu, "MenuID", Ctx.Snapshot!.Lookup.MaxMenuId), Ctx.NextId(TcTables.GossipOption, "MenuID", 0));
        List<RowEdit> edits = [];
        edits.AddRange(Ctx.Effective.All(TcTables.GossipMenu).Where(r => r.U("MenuID") == menu.MenuId).Select(r => Insert(TcTables.GossipMenu, r.With("MenuID", copy))));
        edits.AddRange(Ctx.Effective.All(TcTables.GossipOption).Where(r => r.U("MenuID") == menu.MenuId).Select(r => Insert(TcTables.GossipOption, r.With("MenuID", copy))));
        edits.AddRange(GossipMenuCopy.Conditions(TcTables.Conditions, Ctx.Effective, menu.MenuId, copy));
        edits.AddRange(GossipMenuCopy.RepointSelectEvents(TcTables.SmartScripts, Ctx.Effective, entry, menu.MenuId, copy));
        edits.Add(Delete(TcTables.CreatureGossip, link));
        edits.Add(Insert(TcTables.CreatureGossip, Row.Of(("CreatureID", entry), ("MenuID", copy))));
        Ctx.Commit($"Copy gossip menu {menu.MenuId}", edits);
        selected = copy;
    }

    async Task SaveReference(string label, RowEdit[] edits, uint id, Reference kind)
    {
        int current = generation;
        string source = Ctx.SourceKey;
        try
        {
            if (id != 0)
            {
                var db = DBSource.Open(Ctx.Connection(), Ctx.Lookup());
                bool found = kind switch
                {
                    Reference.Menu => Ctx.Effective.All(Ctx.Schema.GossipPage.Menus.Table).Any(r => r.U("MenuID") == id)
                        || Ctx.Effective.All(Ctx.Schema.GossipPage.Options.Table).Any(r => r.U("MenuID") == id)
                        || await db.MenuExistsAsync(id, CancellationToken.None),
                    Reference.Poi => Ctx.Snapshot?.Lookup.PoiExists(id) == true || await db.PoiExistsAsync(id, CancellationToken.None),
                    _ => Ctx.Snapshot?.Lookup.BroadcastText(id) != null
                        || (await db.SearchBroadcastAsync(id.ToString(CultureInfo.InvariantCulture), CancellationToken.None)).Any(r => r.Id == id)
                };
                if (current != generation || source != Ctx.SourceKey) return;
                if (!found)
                {
                    Ctx.Status = $"Not saved: {kind switch { Reference.Menu => "gossip menu", Reference.Poi => "point of interest", _ => "broadcast text" }} {id} was not found.";
                    Ctx.Redraw();
                    return;
                }

                if (kind == Reference.Poi) Ctx.Snapshot?.Lookup.Pois.Add(id);
            }
            if (current == generation && source == Ctx.SourceKey) Ctx.Commit(label, edits);
        }
        catch (Exception error)
        {
            if (current != generation || source != Ctx.SourceKey) return;
            Ctx.Status = DbErrors.Safe(error);
            Ctx.Redraw();
        }
    }
}
