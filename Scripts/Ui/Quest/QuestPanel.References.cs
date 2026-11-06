using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Reference pickers and the compact icon rows the quest pages show. Item, spell, currency, skill,
/// mail, faction, title and area names come from the client the open database uses (Wrath DBCs or
/// retail DB2s), and the snapshot fills what the world database itself holds.
/// </summary>
public abstract partial class QuestPanel
{
    static readonly (uint Value, string Name)[] Expansions =
    [
        (uint.MaxValue, "Not set"), (0, "Classic"), (1, "The Burning Crusade"), (2, "Wrath of the Lich King"),
        (3, "Cataclysm"), (4, "Mists of Pandaria"), (5, "Warlords of Draenor"), (6, "Legion"),
        (7, "Battle for Azeroth"), (8, "Shadowlands"), (9, "Dragonflight"), (10, "The War Within"),
        (11, "Midnight")
    ];

    /// <summary>The quest kind dropdown, named from the client's QuestInfo table and the shared fallbacks.</summary>
    protected IReadOnlyList<(uint Value, string Name)> QuestInfoChoices()
    {
        var client = catalog?.QuestInfos() ?? Lookup()?.QuestInfos() ?? [];
        var choices = client.Select(info => (info.Id, info.Name)).Where(info => info.Name.Length > 0).ToList();
        foreach (var fallback in QuestVocabulary.Types)
        {
            if (choices.All(choice => choice.Item1 != fallback.Value))
            {
                choices.Add(fallback);
            }
        }

        return choices.OrderBy(choice => choice.Item1 == 0 ? 0 : 1).ThenBy(choice => choice.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The masked playable races, named from the retail client when it has ChrRaces.</summary>
    protected IReadOnlyList<(ulong Bit, string Name)> RaceBits()
    {
        var client = Lookup()?.Races();
        return QuestVocabulary.PlayableRaces
            .Select(race =>
            {
                string name = client?.FirstOrDefault(c => c.Id == race.Id).Name ?? "";
                return (Bit: QuestVocabulary.RaceBit(race.Id), Name: name.Length > 0 ? name : race.Name);
            })
            .Where(race => race.Bit != 0)
            .ToList();
    }

    /// <summary>The union of every named race bit, the starting point for a custom selection.</summary>
    protected ulong NamedRaceMask() => RaceBits().Aggregate(0UL, (mask, race) => mask | race.Bit);

    /// <summary>The expansion dropdown for quest_template.Expansion (-1 is unset).</summary>
    protected OptionButton ExpansionChoice(Row q) =>
        EditorFields.Choice(Expansions, (uint)q.I("Expansion"), value => Set(Template, "Change expansion", ("Expansion", unchecked((int)value))),
            "The expansion the quest belongs to. 'Not set' stores -1.");

    protected Texture2D? IconOfPath(string? path) => path is { Length: > 0 } ? icons.Get(path) ?? icons.Get(ClientStyle.MissingIcon) : null;

    static string FileIcon(uint fileDataId) => fileDataId == 0 ? "" : AssetRef.FromFileId(fileDataId);

    // ---- items -----------------------------------------------------------------------------

    protected ItemDetails? ItemDetailsOf(uint id) =>
        snapshot?.Names.Details(id) ?? (id != 0 ? Lookup()?.ItemDetails(id) : null);

    protected string? ItemNameOf(uint id) => snapshot?.Names.Item(id) ?? Lookup()?.Item(id)?.Name;

    protected string? ItemIconOf(uint id, ItemDetails? details = null)
    {
        details ??= ItemDetailsOf(id);
        if (details != null && catalog?.ItemIconPath(details.Info.DisplayId) is { } path)
        {
            return path;
        }

        return Lookup()?.Item(id) is { IconFileId: > 0 } item ? FileIcon(item.IconFileId) : null;
    }

    protected Tooltip? ItemTipOf(uint id, ItemDetails? details = null)
    {
        details ??= ItemDetailsOf(id);
        return details == null ? null : ItemTooltipBuilder.Build(details, Dialect == WorldDialect.TrinityCore ? ItemVocabulary.Retail : null);
    }

    protected async Task<IReadOnlyList<PickerRecord>> SearchItemRecords(string term)
    {
        if (Dialect == WorldDialect.AzerothCore)
        {
            var found = await DBSource.Open(Connection(), Lookup()).SearchItemDetailsAsync(term, CancellationToken.None);
            return found.Select(details => new PickerRecord(details.Info.Id, details.Info.Name,
                $"item · {details.Info.Id}", ItemIconOf(details.Info.Id, details) ?? "", ItemTooltipBuilder.Build(details), details)).ToList();
        }

        if (Lookup() is not { } client)
        {
            return [];
        }

        var items = await Task.Run(() => client.SearchItems(term, 60));
        return items.Select(item =>
        {
            var details = client.ItemDetails(item.Id);
            var tip = details == null
                ? new Tooltip([new(item.Name, "", TooltipTone.Title), new($"Item {item.Id}", "", TooltipTone.Muted)])
                : ItemTooltipBuilder.Build(details, ItemVocabulary.Retail);
            return new PickerRecord(item.Id, item.Name, $"item · {item.Id}", FileIcon(item.IconFileId), tip, details);
        }).ToList();
    }

    /// <summary>Search items (with icons and game tooltips) and hand the picked template to the page.</summary>
    protected void ChooseItem(string heading, Action<uint, ItemDetails?> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, SearchItemRecords, record =>
        {
            if (record.Value is ItemDetails details && snapshot != null)
            {
                snapshot.Names.Items[details.Info.Id] = details.Info.Name;
                snapshot.Names.ItemDetails[details.Info.Id] = details;
            }
            else if (snapshot != null && record.Label.Length > 0)
            {
                snapshot.Names.Items[record.Id] = record.Label;
            }

            apply(record.Id, record.Value as ItemDetails);
        }, current: current);

    /// <summary>An item shown with its client icon and WoW tooltip, plus Choose… and clear.</summary>
    protected HBoxContainer ItemReference(uint id, string what, Action choose, Action? clear, string tooltip)
    {
        var details = ItemDetailsOf(id);
        return EditorFields.Reference(id == 0 ? $"Choose {what}…" : ItemNameOf(id) is { Length: > 0 } name ? $"{name}  ·  {id}" : $"Missing #{id}",
            choose, clear, id != 0 && ItemNameOf(id) == null, tooltip, IconOfPath(ItemIconOf(id, details)), ItemTipOf(id, details));
    }

    // ---- spells ----------------------------------------------------------------------------

    protected string? SpellNameOf(uint id) => catalog?.SpellName(id) ?? Lookup()?.SpellName(id);
    protected string? SpellIconOf(uint id) =>
        catalog?.SpellIconPath(id) ?? (Lookup()?.SpellIcon(id) is uint file && file != 0 ? FileIcon(file) : null);

    protected Tooltip? SpellTipOf(uint id) => id == 0
        ? null
        : new Tooltip([new(SpellNameOf(id) ?? $"Spell {id}", "", TooltipTone.Title), new($"Spell {id}", "", TooltipTone.Muted)]);

    /// <summary>Spell search with the spell's client icon and a hover tooltip.</summary>
    protected void ChooseSpell(string heading, Action<uint> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, async term =>
        {
            var found = await SearchSpells(term);
            return found.Select(s => new PickerRecord(s.Id, s.Name, $"spell · {s.Id}", SpellIconOf(s.Id) ?? "", SpellTipOf(s.Id))).ToList();
        }, record => apply(record.Id), current: current);

    protected HBoxContainer SpellReference(TableSchema table, string column, string what, string label, string tooltip)
    {
        uint id = RowOf(table).U(column);
        return EditorFields.Reference(id == 0 ? $"Choose {what}…" : SpellNameOf(id) is { Length: > 0 } name ? $"{name}  ·  {id}" : $"Missing #{id}",
            () => ChooseSpell($"Choose {what}", picked => Set(table, label, (column, picked)), id), id == 0 ? null : () => Set(table, label, (column, 0)),
            id != 0 && SpellNameOf(id) == null, tooltip, IconOfPath(SpellIconOf(id)), SpellTipOf(id));
    }

    // ---- currencies ------------------------------------------------------------------------

    protected ClientCurrency? CurrencyOf(uint id) => Lookup()?.Currency(id);
    protected string? CurrencyNameOf(uint id) => id == 0 ? null : CurrencyOf(id)?.Name ?? catalog?.CurrencyName(id);
    protected string? CurrencyIconOf(uint id) => CurrencyOf(id) is { IconFileId: > 0 } currency ? FileIcon(currency.IconFileId) : null;

    protected Tooltip? CurrencyTipOf(uint id) => id == 0 ? null : CurrencyOf(id) is { } currency
        ? new Tooltip([new(currency.Name, "", TooltipTone.Title), new(currency.Description, "", TooltipTone.Muted), new($"Currency {id}", "", TooltipTone.Muted)])
        : new Tooltip([new(CurrencyNameOf(id) ?? $"Currency {id}", "", TooltipTone.Title), new($"Currency {id}", "", TooltipTone.Muted)]);

    protected void ChooseCurrency(string heading, Action<uint> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, term =>
        {
            if (Lookup() is { } client)
            {
                return Task.FromResult<IReadOnlyList<PickerRecord>>(client.SearchCurrencies(term, 60).Select(currency =>
                    new PickerRecord(currency.Id, currency.Name, $"currency · {currency.Id}", FileIcon(currency.IconFileId), CurrencyTipOf(currency.Id))).ToList());
            }

            return Task.FromResult<IReadOnlyList<PickerRecord>>((catalog?.SearchCurrencies(term) ?? []).Select(c =>
                new PickerRecord(c.Id, c.Name, $"currency · {c.Id}", "", CurrencyTipOf(c.Id))).ToList());
        }, record => apply(record.Id), current: current);

    protected HBoxContainer CurrencyReference(TableSchema table, string column, string label, string tooltip)
    {
        uint id = RowOf(table).U(column);
        return EditorFields.Reference(id == 0 ? "Choose currency…" : CurrencyNameOf(id) is { Length: > 0 } name ? $"{name}  ·  {id}" : $"Missing #{id}",
            () => ChooseCurrency("Choose currency", picked => Set(table, label, (column, picked)), id),
            id == 0 ? null : () => Set(table, label, (column, 0)), id != 0 && CurrencyNameOf(id) == null,
            tooltip, IconOfPath(CurrencyIconOf(id)), CurrencyTipOf(id));
    }

    // ---- skills ----------------------------------------------------------------------------

    protected ClientSkillLine? SkillOf(uint id) => Lookup()?.SkillLine(id);
    protected string? SkillNameOf(uint id) => id == 0 ? null : SkillOf(id)?.Name ?? catalog?.SkillName(id);
    protected string? SkillIconOf(uint id) => SkillOf(id) is { IconFileId: > 0 } skill ? FileIcon(skill.IconFileId) : catalog?.SkillIconPath(id);

    Tooltip? SkillTipOf(uint id) => id == 0 ? null : SkillOf(id) is { } skill
        ? new Tooltip([new(skill.Name, "", TooltipTone.Title), new(skill.Description, "", TooltipTone.Muted), new($"Skill {id}", "", TooltipTone.Muted)])
        : new Tooltip([new(SkillNameOf(id) ?? $"Skill {id}", "", TooltipTone.Title), new($"Skill {id}", "", TooltipTone.Muted)]);

    protected void ChooseSkill(string heading, Action<uint> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, term =>
        {
            if (Lookup() is { } client)
            {
                return Task.FromResult<IReadOnlyList<PickerRecord>>(client.SearchSkillLines(term, 60).Select(skill =>
                    new PickerRecord(skill.Id, skill.Name, $"skill · {skill.Id}", FileIcon(skill.IconFileId), SkillTipOf(skill.Id))).ToList());
            }

            var found = catalog?.SearchSkills(term) ?? [];
            return Task.FromResult<IReadOnlyList<PickerRecord>>(found.Select(s =>
                new PickerRecord(s.Id, s.Name, $"skill · {s.Id}", catalog?.SkillIconPath(s.Id) ?? "", SkillTipOf(s.Id))).ToList());
        }, record => apply(record.Id), current: current);

    protected HBoxContainer SkillReference(TableSchema table, string column, string label, string tooltip)
    {
        uint id = RowOf(table).U(column);
        return EditorFields.Reference(id == 0 ? "Choose skill…" : SkillNameOf(id) is { Length: > 0 } name ? $"{name}  ·  {id}" : $"Missing #{id}",
            () => ChooseSkill("Choose skill", picked => Set(table, label, (column, picked)), id),
            id == 0 ? null : () => Set(table, label, (column, 0)), id != 0 && SkillNameOf(id) == null,
            tooltip, IconOfPath(SkillIconOf(id)), SkillTipOf(id));
    }

    // ---- factions --------------------------------------------------------------------------

    protected string? FactionNameOf(uint id) => id == 0 ? null : catalog?.FactionName(id) ?? Lookup()?.Faction(id)?.Name;

    protected void ChooseFaction(string heading, Action<uint> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, term =>
        {
            if (Lookup() is { } client)
            {
                return Task.FromResult<IReadOnlyList<PickerRecord>>(client.SearchFactions(term, 80).Select(faction =>
                    new PickerRecord(faction.Id, faction.Name, $"faction · {faction.Id}", "", FactionTip(faction.Id))).ToList());
            }

            var list = catalog?.All("Faction") ?? [];
            return Task.FromResult<IReadOnlyList<PickerRecord>>(list
                .Where(f => term.Length == 0 || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || f.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
                .OrderBy(f => f.Name.Length).Take(200)
                .Select(f => new PickerRecord(f.Id, f.Name, $"faction · {f.Id}", "", FactionTip(f.Id))).ToList());
        }, record => apply(record.Id), current: current);

    protected Tooltip? FactionTip(uint id) => id == 0 ? null : new Tooltip([new(FactionNameOf(id) ?? $"Faction {id}", "", TooltipTone.Title), new($"Reputation faction {id}", "", TooltipTone.Muted)]);

    // ---- mail templates --------------------------------------------------------------------

    protected string? MailSubjectOf(uint id) => id == 0 ? null : catalog?.MailSubject(id) ?? Lookup()?.MailTemplate(id)?.Name;

    protected void ChooseMail(string heading, Action<uint> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, term =>
        {
            if (Lookup() is { } client)
            {
                return Task.FromResult<IReadOnlyList<PickerRecord>>(client.SearchMailTemplates(term, 80).Select(mail =>
                    new PickerRecord(mail.Id, mail.Name, $"mail · {mail.Id}", "", MailTip(mail.Id))).ToList());
            }

            return Task.FromResult<IReadOnlyList<PickerRecord>>((catalog?.SearchMails(term) ?? []).Select(mail =>
                new PickerRecord(mail.Id, mail.Name, $"mail · {mail.Id}", "", MailTip(mail.Id))).ToList());
        }, record => apply(record.Id), current: current);

    Tooltip? MailTip(uint id) => id == 0 ? null : new Tooltip([new(MailSubjectOf(id) ?? $"Mail template {id}", "", TooltipTone.Title), new($"Mail template {id}", "", TooltipTone.Muted)]);

    protected HBoxContainer MailReference(TableSchema table, string column, string label, string tooltip)
    {
        uint id = RowOf(table).U(column);
        return EditorFields.Reference(id == 0 ? "No mail" : MailSubjectOf(id) is { Length: > 0 } subject ? $"{subject}  ·  {id}" : $"Mail template {id}",
            () => ChooseMail("Choose reward mail", picked => Set(table, label, (column, picked)), id),
            id == 0 ? null : () => Set(table, label, (column, 0)), id != 0 && MailSubjectOf(id) == null,
            tooltip, IconOfPath("Interface/Icons/INV_Letter_15.blp"), MailTip(id));
    }

    // ---- titles ----------------------------------------------------------------------------

    protected string? TitleNameOf(uint id) => id == 0 ? null : catalog?.TitleName(id) ?? Lookup()?.SearchTitles(id.ToString(CultureInfo.InvariantCulture), 1).FirstOrDefault().Name;

    protected void ChooseTitle(string heading, Action<uint> apply, uint? current = null) =>
        RecordPicker.Show(this, Store, heading, term =>
        {
            if (Lookup() is { } client)
            {
                var list = term.Length == 0 ? client.Titles().Take(80) : client.SearchTitles(term, 80);
                return Task.FromResult<IReadOnlyList<PickerRecord>>(list.Select(title =>
                    new PickerRecord(title.Id, title.Name, $"title · {title.Id}", "", TitleTip(title.Id))).ToList());
            }

            return Task.FromResult<IReadOnlyList<PickerRecord>>((catalog?.SearchTitles(term) ?? []).Select(title =>
                new PickerRecord(title.Id, title.Name, $"title · {title.Id}", "", TitleTip(title.Id))).ToList());
        }, record => apply(record.Id), current: current);

    Tooltip? TitleTip(uint id) => id == 0 ? null : new Tooltip([new(TitleNameOf(id) ?? $"Title {id}", "", TooltipTone.Title), new($"Character title {id}", "", TooltipTone.Muted)]);

    protected HBoxContainer TitleReference(TableSchema table, string column, string label, string tooltip)
    {
        uint id = RowOf(table).U(column);
        return EditorFields.Reference(id == 0 ? "No title" : TitleNameOf(id) is { Length: > 0 } title ? $"{title}  ·  {id}" : $"Missing #{id}",
            () => ChooseTitle("Choose reward title", picked => Set(table, label, (column, picked)), id),
            id == 0 ? null : () => Set(table, label, (column, 0)), id != 0 && TitleNameOf(id) == null, tooltip, null, TitleTip(id));
    }

    // ---- areas -----------------------------------------------------------------------------

    /// <summary>The quest log zone: an AreaTable picker that also lists the negative quest-log sorts.</summary>
    protected void ChooseArea(Action<int> apply, int current)
    {
        var areas = Areas();
        if (areas == null)
        {
            status = "Zone names need the client's AreaTable; enter the zone ID in the field instead.";
            UpdateStatus();
            return;
        }

        static IReadOnlyList<(uint Id, string Name)> Sorts(InteractionCatalog? catalog, IClientLookup? client) =>
            catalog?.QuestSorts() ?? client?.QuestSorts() ?? [];

        RecordPicker.Show(this, Store, "Choose quest log zone", term =>
        {
            var found = new List<PickerRecord>
            {
                new(0, "No zone", "the quest has no zone", "", null, 0)
            };
            foreach (var sort in Sorts(catalog, Lookup()))
            {
                if (term.Length == 0 || sort.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || sort.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
                {
                    found.Add(new(sort.Id, sort.Name, "quest log category", "", null, -unchecked((int)sort.Id)));
                }
            }

            foreach (var area in areas.Search(term, null, 80))
            {
                string detail = area.Parent is { Length: > 0 } parent ? $"in {parent}" : $"zone · map {area.Map}";
                found.Add(new(area.Id, area.Name, detail, "", null, (int)area.Id));
            }

            return Task.FromResult<IReadOnlyList<PickerRecord>>(found);
        }, record => apply(record.Value is int value ? value : (int)record.Id), initial: "", current: current > 0 ? (uint)current : null);
    }

    // ---- emotes ----------------------------------------------------------------------------

    /// <summary>Animation emotes for the giver/turn-in gesture dropdowns, from either client kind.</summary>
    protected IReadOnlyList<(uint Id, string Name)> EmoteChoices()
    {
        var list = catalog?.All("Emote") ?? [];
        if (!list.Any() && Lookup() is { } client)
        {
            list = client.Emotes();
        }

        return list.Where(e => e.Item2.Length > 0).Prepend((0u, "none")).DistinctBy(e => e.Item1).ToList();
    }
}
