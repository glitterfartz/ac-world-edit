using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Wires the spawn inspector's named choices, flag checkbox dropdowns and model/creature/spell pickers from
/// the open dialect. The NPC and unit flag names are the template page's own lists, never copies.
/// </summary>
public static class SpawnFieldPickers
{
    public static IReadOnlyList<(uint Value, string Label)>? Emotes(WorldDialect dialect, InteractionCatalog? catalog, IClientLookup? lookup)
    {
        var list = new List<(uint, string)> { (0, "None") };
        if (dialect == WorldDialect.TrinityCore)
        {
            list.AddRange(lookup?.Emotes() ?? []);
        }
        else
        {
            list.AddRange((catalog?.All("Emote") ?? []).Where(e => e.Name.Length > 0).OrderBy(e => e.Name));
        }

        return list.Count > 1 ? list.DistinctBy(e => e.Item1).ToList() : null;
    }

    public static IReadOnlyList<FlagBit>? Flags(WorldDialect dialect, SpawnField field) => field.Column switch
    {
        "npcflag" => dialect == WorldDialect.TrinityCore
            ? TcCreatureView.NpcFlagBits.Select(f => new FlagBit(f.Bit, f.Name, f.Tip)).ToArray()
            : AcNpcFlags.All.Select(f => new FlagBit(f.Bit, f.Name, f.Tip)).ToArray(),
        "unit_flags" => dialect == WorldDialect.TrinityCore ? TcCreaturePage.UnitFlags : AcCreaturePage.UnitFlags,
        "unit_flags2" => dialect == WorldDialect.TrinityCore ? TcCreaturePage.UnitFlags2 : AcCreaturePage.UnitFlags2,
        "unit_flags3" => dialect == WorldDialect.TrinityCore ? TcCreaturePage.UnitFlags3 : null,
        "type_flags" => dialect == WorldDialect.TrinityCore ? TcCreaturePage.TypeFlags : AcCreaturePage.TypeFlags,
        "dynamicflags" => null,
        _ => null
    };

    public static string Describe(SpawnField field, string value) => field.Column switch
    {
        "mount" or "modelid" => value == "0" ? "None" : $"Display {value}",
        "MountCreatureID" => value == "0" ? "None" : $"Creature {value}",
        "equipment_id" => value is "-1" ? "Random template set" : value == "0" ? "None" : $"Set {value}",
        _ => value
    };

    public static void Choose(Node owner, SpawnField field, Action<uint> picked, Func<WorldConnection> connection,
        Func<IClientFiles?> store, Func<IClientLookup?> lookup, IThumbnailSource? thumbnails)
    {
        switch (field.Column)
        {
            case "mount" or "modelid":
                ChooseDisplay(owner, field, picked, connection, store, thumbnails);
                break;
            case "MountCreatureID":
                ChooseCreature(owner, field, picked, connection, store, thumbnails);
                break;
        }
    }

    public static void ChooseAura(Node owner, SpawnField field, Action<uint> picked, Func<WorldConnection> connection,
        Func<IClientFiles?> store, Func<IClientLookup?> lookup, WorldDialect dialect, InteractionCatalog? catalog = null)
    {
        _ = field;
        RecordPicker.Show(owner, store, "Choose an aura spell",
            async term =>
            {
                var client = lookup();
                if (dialect == WorldDialect.TrinityCore)
                {
                    var hits = await DBSource.Open(connection(), client).SearchAsync(SmartParamKind.Spell, term, CancellationToken.None);
                    return hits.Select(h => AuraRecord(catalog, client, h.Id, h.Name)).ToList();
                }

                catalog ??= await ContentEditorWindow.LoadCatalog(store());
                return (catalog?.SearchSpells(term) ?? []).Select(s => AuraRecord(catalog, client, s.Id, s.Name)).ToList();
            },
            record => picked(record.Id));
    }

    static PickerRecord AuraRecord(InteractionCatalog? catalog, IClientLookup? lookup, uint id, string name) =>
        new(id, name.Length > 0 ? name : $"Spell {id}", $"spell · {id}", SpellIcon(catalog, lookup, id), SpellTip(catalog, lookup, id, name));

    static string SpellIcon(InteractionCatalog? catalog, IClientLookup? lookup, uint id) =>
        catalog?.SpellIconPath(id) ?? (lookup?.SpellIcon(id) is uint file && file != 0 ? AssetRef.FromFileId(file) : "");

    static Tooltip? SpellTip(InteractionCatalog? catalog, IClientLookup? lookup, uint id, string name)
    {
        string shown = name.Length > 0 ? name : catalog?.SpellName(id) ?? lookup?.SpellName(id) ?? $"Spell {id}";
        return new Tooltip([new(shown, "", TooltipTone.Title), new($"Spell {id}", "", TooltipTone.Muted)]);
    }

    public static void ChooseEquipment(Node owner, uint entry, Action<long> picked, Func<WorldConnection> connection,
        Func<IClientFiles?> store, Func<IClientLookup?> lookup)
    {
        RecordPicker.Show(owner, store, "Choose equipment set",
            async term =>
            {
                var sets = await DBSource.Open(connection()).ReadEquipmentSetsAsync(entry, CancellationToken.None);
                term = term.Trim();
                var records = new List<PickerRecord>
                {
                    new(0, "None", "bare hands"),
                    new(uint.MaxValue, "Random template set", "-1")
                };
                foreach (var set in sets)
                {
                    string items = string.Join(", ", new[] { set.First, set.Second, set.Third }.Where(id => id != 0)
                        .Select(id => lookup()?.Item(id) is { } item ? $"{item.Name} ({id})" : id.ToString(CultureInfo.InvariantCulture)));
                    string label = $"Set {set.Id}" + (items.Length > 0 ? $" · {items}" : "");
                    if (term.Length == 0 || label.Contains(term, StringComparison.OrdinalIgnoreCase) || set.Id.ToString(CultureInfo.InvariantCulture) == term)
                    {
                        records.Add(new PickerRecord(set.Id, label, $"equipment · {set.Id}"));
                    }
                }

                return records;
            },
            record => picked(record.Id == uint.MaxValue ? -1 : record.Id));
    }

    static void ChooseDisplay(Node owner, SpawnField field, Action<uint> picked, Func<WorldConnection> connection,
        Func<IClientFiles?> store, IThumbnailSource? thumbnails)
    {
        _ = field;
        RecordPicker.Show(owner, store, "Choose a display model",
            async term =>
            {
                var found = await DBSource.Open(connection()).SearchCreatureDisplaysAsync(term, CancellationToken.None);
                return found.Select(hit => new PickerRecord(hit.Id, hit.Name.Length > 0 ? hit.Name : $"Display {hit.Id}", $"display · {hit.Id}",
                    Thumbnail: thumbnails == null ? null : ready => thumbnails.Creature(hit.Id, ready))).ToList();
            },
            record => picked(record.Id));
    }

    static void ChooseCreature(Node owner, SpawnField field, Action<uint> picked, Func<WorldConnection> connection,
        Func<IClientFiles?> store, IThumbnailSource? thumbnails)
    {
        _ = field;
        RecordPicker.Show(owner, store, "Choose a mount creature",
            async term =>
            {
                var db = DBSource.Open(connection());
                var found = await db.SearchTemplatesAsync(SpawnKind.Creature, term, null, CancellationToken.None);
                var looks = found.Count == 0 ? new Dictionary<uint, CreatureLook>() : await db.ReadCreatureLooksAsync(found.Select(f => f.Entry).ToList(), CancellationToken.None);
                return found.Select(hit =>
                {
                    uint display = looks.GetValueOrDefault(hit.Entry)?.DisplayId ?? 0;
                    return new PickerRecord(hit.Entry, hit.Name, $"creature · {hit.Entry}",
                        Thumbnail: thumbnails != null && display != 0 ? ready => thumbnails.Creature(display, ready) : null);
                }).ToList();
            },
            record => picked(record.Id));
    }
}
