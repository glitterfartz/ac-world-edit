using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The quests a GameObject offers and accepts and the up to six quest items it can hold. Quest
/// content keeps its separate editor; this page edits the link rows and the item list only.
/// </summary>
public sealed partial class GameObjectQuestsSection(GameObjectContext ctx) : GameObjectSection(ctx)
{
    public override string Id => PickerPages.Quests.Id;
    public override string Title => PickerPages.Quests.Title;
    public override string IconPath => PickerPages.Quests.IconPath;
    public override string Tip => "Quests this object gives or accepts, and the up to six quest items it can hold.";

    public override bool HasContent => Ctx.Template != null &&
        (Links().Any() || Items().Any());

    public override string Badge
    {
        get
        {
            int count = Links().Count + Items().Count;
            return count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
        }
    }

    List<Row> Links()
    {
        if (Ctx.Entry is not uint entry)
        {
            return [];
        }

        var page = Ctx.QuestPage;
        return page.Links.Starts.Select(Ctx.Effective, entry)
            .Concat(page.Links.Ends.Select(Ctx.Effective, entry))
            .GroupBy(r => r.U("quest"))
            .Select(g => g.First())
            .ToList();
    }

    List<Row> Items() => Ctx.Entry is uint entry
        ? Ctx.QuestPage.Items.Select(Ctx.Effective, entry).ToList()
        : [];

    public override void Refresh()
    {
        Clear();
        if (Ctx.Template == null || Ctx.Entry is not uint entry)
        {
            AddChild(Placeholder());
            return;
        }

        var page = Ctx.QuestPage;
        AddChild(ClientStyle.Text("Linked quests", 16, ClientStyle.Header));
        var starts = page.Links.Starts.Select(Ctx.Effective, entry).ToDictionary(r => r.U("quest"));
        var ends = page.Links.Ends.Select(Ctx.Effective, entry).ToDictionary(r => r.U("quest"));
        var ids = starts.Keys.Concat(ends.Keys).Distinct().OrderBy(id => id).ToList();
        if (ids.Count == 0)
        {
            AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (uint id in ids)
        {
            AddChild(QuestRow(page, entry, id, starts.ContainsKey(id), ends.ContainsKey(id)));
        }

        AddChild(new HSeparator());
        AddChild(ClientStyle.Text("Link a quest", 16, ClientStyle.Header));
        if (!Ctx.Editable)
        {
            AddChild(ClientStyle.Text("Editing is disabled.", 12, ClientStyle.Faded));
        }
        else
        {
            var link = new HBoxContainer();
            link.AddThemeConstantOverride("separation", 8);
            var giver = new Button { Text = "Add as giver…", TooltipText = "Search for a quest this GameObject offers." };
            giver.Pressed += () => PickQuest("Choose a quest to give", id => Link(entry, id, gives: true, takes: false));
            var ender = new Button { Text = "Add as ender…", TooltipText = "Search for a quest players hand in at this GameObject." };
            ender.Pressed += () => PickQuest("Choose a quest to accept", id => Link(entry, id, gives: false, takes: true));
            link.AddChild(giver);
            link.AddChild(ender);
            AddChild(link);
        }

        AddChild(new HSeparator());
        var items = Items();
        AddChild(ClientStyle.Text($"Quest items ({items.Count}/6)", 16, ClientStyle.Header));
        foreach (var item in items)
        {
            AddChild(ItemRow(page, item));
        }

        if (items.Count == 0)
        {
            AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        if (!Ctx.Editable)
        {
            return;
        }

        int next = Ctx.NextQuestItemIndex();
        if (next >= 6)
        {
            AddChild(ClientStyle.Text("The server allows at most six quest items.", 11, ClientStyle.Warning));
            return;
        }

        var add = new Button { Text = "Add item…", TooltipText = "Search for an item this GameObject can hold." };
        add.Pressed += () => PickItem(entry, (uint)next);
        AddChild(add);
    }

    Control QuestRow(GameObjectQuestPageSpec page, uint entry, uint id, bool gives, bool takes)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(QuestIcons(gives, takes));
        var name = new Label
        {
            Text = $"{Ctx.QuestName(id)} ({id})",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        row.AddChild(name);
        if (!Ctx.Editable)
        {
            return row;
        }

        var giver = new CheckBox { Text = "Giver", ButtonPressed = gives, TooltipText = "The GameObject offers this quest." };
        var ender = new CheckBox { Text = "Ender", ButtonPressed = takes, TooltipText = "Players hand this quest in at the GameObject." };
        giver.Toggled += on => Ctx.Commit($"{(on ? "Give" : "Stop giving")} quest {id}", page.SetRoles(Ctx.Effective, entry, id, on, ender.ButtonPressed));
        ender.Toggled += on => Ctx.Commit($"{(on ? "Accept" : "Stop accepting")} quest {id}", page.SetRoles(Ctx.Effective, entry, id, giver.ButtonPressed, on));
        var unlink = new Button { Text = "Unlink", TooltipText = "Remove both quest links." };
        unlink.Pressed += () => Ctx.Commit($"Unlink quest {id}", page.SetRoles(Ctx.Effective, entry, id, false, false));
        row.AddChild(giver);
        row.AddChild(ender);
        row.AddChild(unlink);
        return row;
    }

    void Link(uint entry, uint quest, bool gives, bool takes)
    {
        var edits = Ctx.QuestPage.SetRoles(Ctx.Effective, entry, quest, gives, takes);
        if (edits.Length == 0)
        {
            Ctx.Status = $"{Ctx.QuestName(quest)} is already linked that way.";
            Ctx.Redraw();
            return;
        }

        Ctx.Commit($"Link quest {quest}", edits);
    }

    void PickQuest(string title, Action<uint> picked)
    {
        RecordPicker.Show(this, title, SearchQuests, (id, _) => picked(id), store: Ctx.Store);
    }

    async Task<IReadOnlyList<(uint Id, string Name)>> SearchQuests(string term)
    {
        var found = await DBSource.Open(Ctx.Connection(), Ctx.Lookup()).SearchQuestsAsync(term, CancellationToken.None);
        return found.Select(q => (q.Id, q.Title)).ToList();
    }

    Control ItemRow(GameObjectQuestPageSpec page, Row item)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        uint id = item.U("ItemId");
        row.AddChild(ClientStyle.Text($"#{item.U("Idx")}", 12, ClientStyle.Faded));
        var name = new Label
        {
            Text = $"{Ctx.ItemName(id)} ({id})",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        row.AddChild(name);
        if (Ctx.Editable)
        {
            var remove = new Button { Text = "Remove", TooltipText = "Remove this quest item." };
            remove.Pressed += () => Ctx.Commit($"Remove quest item {id}", [page.Items.Remove(item)]);
            row.AddChild(remove);
        }

        return row;
    }

    void PickItem(uint entry, uint index)
    {
        RecordPicker.Show(this, Ctx.Store, "Choose a quest item", SearchItemRecords, record =>
        {
            var edit = Ctx.QuestPage.Items.Add(Ctx.Effective, entry, ("Idx", index), ("ItemId", record.Id));
            Ctx.Commit($"Add quest item {record.Id}", [edit]);
        });
    }

    async Task<IReadOnlyList<PickerRecord>> SearchItemRecords(string term)
    {
        if (Ctx.Lookup() is { } client)
        {
            var items = await Task.Run(() => client.SearchItems(term, 50));
            return items.Select(item =>
            {
                var details = client.ItemDetails(item.Id);
                Tooltip tip = details == null
                    ? new Tooltip([new(item.Name, "", TooltipTone.Title), new($"Item {item.Id}", "", TooltipTone.Muted)])
                    : ItemTooltipBuilder.Build(details, ItemVocabulary.Retail);
                return new PickerRecord(item.Id, item.Name, $"item · {item.Id}",
                    item.IconFileId > 0 ? AssetRef.FromFileId(item.IconFileId) : "", tip, details);
            }).ToList();
        }

        var found = await DBSource.Open(Ctx.Connection()).SearchItemDetailsAsync(term, CancellationToken.None);
        return found.Select(details => new PickerRecord(details.Info.Id, details.Info.Name, $"item · {details.Info.Id}",
            Ctx.Catalog?.ItemIconPath(details.Info.DisplayId) ?? "", ItemTooltipBuilder.Build(details), details)).ToList();
    }

    HBoxContainer QuestIcons(bool gives, bool takes)
    {
        var slot = new HBoxContainer { CustomMinimumSize = new(34, 0) };
        slot.AddThemeConstantOverride("separation", 2);
        foreach (string? path in new[] { gives ? ClientStyle.AvailableQuestIcon : null, takes ? ClientStyle.ActiveQuestIcon : null }.OfType<string>())
        {
            slot.AddChild(new TextureRect
            {
                Texture = Ctx.Icons.Get(path) ?? Ctx.Icons.Get(ClientStyle.MissingIcon),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new(16, 16)
            });
        }

        return slot;
    }
}
