using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The friendly editors under Identity and Combat that need names, icons or a search: faction,
/// display model, unit class, creature type, pet family, trainer class, cursor icon, AI name and the
/// immunity set. A value not named by the client still shows its raw value, so nothing is hidden.
/// </summary>
public static class CreatureReferenceEditor
{
    /// <summary>Cursor names the 3.3.5 client accepts (creature_template.IconName is case sensitive).</summary>
    public static readonly (string Value, string Name, string Icon)[] Cursors =
    [
        ("", "Default", "Point"), ("Directions", "Directions", "Directions"), ("Gunner", "Gunner", "Gunner"), ("vehichleCursor", "Vehicle", "vehichleCursor"),
        ("Driver", "Driver", "Driver"), ("Attack", "Attack", "Attack"), ("Buy", "Buy", "Buy"), ("Speak", "Speak", "Speak"), ("Pickup", "Pickup", "Pickup"),
        ("Interact", "Interact", "Interact"), ("Trainer", "Trainer", "Trainer"), ("Taxi", "Taxi", "Taxi"), ("Repair", "Repair", "Repair"), ("LootAll", "Loot all", "LootAll"),
        ("Quest", "Quest", "Quest"), ("PVP", "PvP", "PVP"), ("Point", "Point", "Point")
    ];

    /// <summary>The AI names the servers select by; an unknown name stays visible as "Other".</summary>
    public static readonly (string Value, string Name)[] AiNames =
    [
        ("", "Default"), ("NullCreatureAI", "Null creature"), ("AggressorAI", "Aggressor"), ("ReactorAI", "Reactor"),
        ("PassiveAI", "Passive"), ("CritterAI", "Critter"), ("GuardAI", "Guard"), ("PetAI", "Pet"), ("TotemAI", "Totem"),
        ("SmartAI", "SmartAI"), ("PlayerAI", "Player"), ("PossessedAI", "Possessed"), ("VehicleAI", "Vehicle"),
        ("EventAI", "EventAI")
    ];

    static readonly (uint Value, string Name)[] UnitClasses = [(1, "Warrior"), (2, "Paladin"), (4, "Rogue"), (8, "Mage")];
    static readonly (uint Value, string Name)[] FallbackTypes =
    [
        (0, "None"), (1, "Beast"), (2, "Dragonkin"), (3, "Demon"), (4, "Elemental"), (5, "Giant"), (6, "Undead"), (7, "Humanoid"),
        (8, "Critter"), (9, "Mechanical"), (10, "Not specified"), (11, "Totem"), (12, "Non-combat pet"), (13, "Gas cloud")
    ];

    /// <summary>The reader for this field when the page is read-only; null uses the standard value.</summary>
    public static string? Describe(CreatureContext ctx, FieldSpec field, Row row) => field.Column switch
    {
        "faction" => FactionName(ctx, row.U("faction")) ?? row.T("faction"),
        "unit_class" => Name(ClassOptions(ctx), row.U("unit_class"), "Class"),
        "type" => Name(TypeOptions(ctx), row.U("type"), "Type"),
        "family" => row.I("family") == 0 ? "None" : Name(FamilyOptions(ctx), row.U("family"), $"Family {row.I("family")}"),
        "trainer_class" => row.U("trainer_class") == 0 ? "None" : Name(AllClassOptions(ctx), row.U("trainer_class"), $"Class {row.U("trainer_class")}"),
        "IconName" => row.T("IconName") is { Length: > 0 } icon ? icon : "Default",
        "AIName" => row.T("AIName") is { Length: > 0 } ai ? ai : "None",
        "CreatureImmunitiesId" => ImmunityName(ctx, row.I("CreatureImmunitiesId")),
        _ => null
    };

    /// <summary>
    /// The editor for a field in this group, or null for the normal control. <paramref name="typeFlagRow"/>
    /// is the difficulty row that owns TypeFlags on TrinityCore (null on AzerothCore).
    /// </summary>
    public static Control? Build(Node owner, CreatureContext ctx, FieldSpec field, Row row, Row? typeFlagRow, TableSchema? typeFlagTable)
    {
        switch (field.Column)
        {
            case "faction":
                return Reference(field, row, FactionName(ctx, row.U("faction")) ?? "None",
                    () => ChooseFaction(owner, ctx, field, row), row.U("faction") == 0 ? null : () => Set(ctx, field, row, 0u),
                    $"Faction {row.U("faction")}");
            case "unit_class":
                return EditorFields.Choice(ClassOptions(ctx), row.U("unit_class"), value => Set(ctx, field, row, value),
                    "The class whose class-level stats supply the creature's health, mana and damage.");
            case "type":
                return EditorFields.Choice(TypeOptions(ctx), row.U("type"), value => Set(ctx, field, row, value),
                    "The creature's kind; some spells and quest objectives only work on certain types.");
            case "family":
                return FamilyChoice(ctx, field, row, typeFlagRow, typeFlagTable);
            case "trainer_class":
                return TrainerClassChoice(ctx, field, row);
            case "IconName":
                return EditorFields.TextChoice(Cursors.Select(c => (c.Value, c.Name, c.Icon)), row.T("IconName"),
                    value => SetText(ctx, field, row, value), icon => CursorTexture(ctx, icon),
                    "The mouse cursor shown when hovering over the creature.");
            case "AIName":
                return EditorFields.TextChoice(AiNames.Select(a => (a.Value, a.Name, "")), row.T("AIName"),
                    value => SetText(ctx, field, row, value), null, "The server AI that drives the creature.");
            case "CreatureImmunitiesId":
                return Reference(field, row, ImmunityName(ctx, row.I("CreatureImmunitiesId")),
                    () => ChooseImmunity(owner, ctx, field, row), row.I("CreatureImmunitiesId") == 0 ? null : () => Set(ctx, field, row, 0),
                    "The named immunity set (schools, mechanics, dispel types and effects) this creature uses.");
            default:
                return null;
        }
    }

    static Control Reference(FieldSpec field, Row row, string shown, Action choose, Action? clear, string tooltip) =>
        EditorFields.Reference(shown, choose, clear, false, tooltip);

    static Control FamilyChoice(CreatureContext ctx, FieldSpec field, Row row, Row? typeFlagRow, TableSchema? typeFlagTable)
    {
        var options = new List<(uint, string)> { (0, "None") };
        options.AddRange(FamilyOptions(ctx).Where(o => o.Value != 0));
        var picker = EditorFields.Choice(options, row.U("family"), value => SetFamily(ctx, field, row, value, typeFlagRow, typeFlagTable),
            "The hunter pet family. Setting one also marks the creature as tameable; clearing it removes that flag.");
        if (FamilyOptions(ctx).Count == 0)
        {
            picker.TooltipText = "The client has no CreatureFamily table; only None is offered.";
        }

        return picker;
    }

    static Control TrainerClassChoice(CreatureContext ctx, FieldSpec field, Row row)
    {
        var options = new List<(uint, string)> { (0, "None") };
        options.AddRange(AllClassOptions(ctx).Where(o => o.Value != 0));
        return EditorFields.Choice(options, row.U("trainer_class"), value => SetTrainerClass(ctx, field, row, value),
            "The class this creature trains. Setting one also turns on the class-trainer NPC flag; clearing it turns the flag off.");
    }

    static void SetFamily(CreatureContext ctx, FieldSpec field, Row row, uint family, Row? typeFlagRow, TableSchema? typeFlagTable)
    {
        var edits = new List<RowEdit>();
        const ulong tameable = 0x1;
        if (typeFlagTable == null)
        {
            // AzerothCore keeps TypeFlags on the template row itself, so family and flag are one update.
            var after = row.With("family", (int)family);
            uint flags = row.U("type_flags");
            uint changed = family != 0 ? flags | (uint)tameable : flags & ~(uint)tameable;
            if (changed != flags)
            {
                after = after.With("type_flags", changed);
            }

            if (after != row)
            {
                edits.Add(InteractionEdits.Update(field.Table, row, after));
            }
        }
        else
        {
            if (row.U("family") != family)
            {
                edits.AddRange(ItemListSpec.Change(field.Table, row, row.With("family", (int)family)));
            }

            if (typeFlagRow != null)
            {
                ulong flags = typeFlagRow.UL("TypeFlags");
                ulong changed = family != 0 ? flags | tameable : flags & ~tameable;
                if (changed != flags)
                {
                    edits.Add(InteractionEdits.Update(typeFlagTable, typeFlagRow, typeFlagRow.With("TypeFlags", changed)));
                }
            }
            else if (family != 0)
            {
                ctx.Status = "Pet family saved; add a difficulty row to mark the creature tameable.";
            }
        }

        if (edits.Count > 0)
        {
            ctx.Commit(family == 0 ? "Clear pet family" : "Change pet family", edits);
        }
    }

    static void SetTrainerClass(CreatureContext ctx, FieldSpec field, Row row, uint value)
    {
        var edits = new List<RowEdit>();
        var after = row;
        if (row.U("trainer_class") != value)
        {
            after = after.With("trainer_class", value);
        }

        const ulong classTrainer = 0x20;
        ulong flags = row.UL("npcflag");
        ulong changed = value != 0 ? flags | classTrainer : flags & ~classTrainer;
        if (changed != flags)
        {
            after = after.With("npcflag", changed);
        }

        if (after != row)
        {
            edits.Add(InteractionEdits.Update(field.Table, row, after));
        }

        if (edits.Count > 0)
        {
            ctx.Commit(value == 0 ? "Clear trainer class" : "Change trainer class", edits);
        }
    }

    static void Set(CreatureContext ctx, FieldSpec field, Row row, object value)
    {
        if (row.T(field.Column) == Convert.ToString(value, CultureInfo.InvariantCulture))
        {
            return;
        }

        ctx.Try(() =>
        {
            var after = row.With(field.Column, value);
            var table = field.Table;
            bool exists = ctx.Effective.Get(table, table.KeyOf(row)) != null;
            ctx.Commit($"Change {field.Label.ToLowerInvariant()}", exists ? ItemListSpec.Change(table, row, after) : [InteractionEdits.Insert(table, after)]);
        });
    }

    static void SetText(CreatureContext ctx, FieldSpec field, Row row, string value)
    {
        if (row.T(field.Column) == value)
        {
            return;
        }

        ctx.Commit($"Change {field.Label.ToLowerInvariant()}", ItemListSpec.Change(field.Table, row, row.With(field.Column, value)));
    }

    // ---- pickers -----------------------------------------------------------------------------

    static void ChooseFaction(Node owner, CreatureContext ctx, FieldSpec field, Row row) =>
        RecordPicker.Show(owner, () => ctx.Store(), "Choose faction",
        [
            new PickerTab("Factions", term => SearchFactions(ctx, term))
        ], record => Set(ctx, field, row, record.Id), current: row.U("faction"));

    static async Task<IReadOnlyList<PickerRecord>> SearchFactions(CreatureContext ctx, string term)
    {
        var records = new List<PickerRecord>();
        if (ctx.Catalog is { } catalog)
        {
            records.AddRange(catalog.SearchFactionTemplates(term, 60).Select(hit => new PickerRecord(hit.Id, hit.Name, $"faction template · {hit.Id}")));
        }
        else
        {
            foreach (var hit in ctx.Lookup()?.SearchFactionTemplates(term, 60) ?? [])
            {
                records.Add(new PickerRecord(hit.Id, hit.Name, hit.Description.Length > 0 ? hit.Description : $"faction template · {hit.Id}"));
            }
        }

        return await Task.FromResult(records);
    }

    static void ChooseImmunity(Node owner, CreatureContext ctx, FieldSpec field, Row row)
    {
        var table = CtxImmunities(ctx);
        int current = row.I("CreatureImmunitiesId");
        var records = ctx.Effective.All(table).Select(immunity => new PickerRecord((uint)immunity.I("ID"), ImmunityLabel(immunity),
            ImmunityDetail(immunity), Tip: ImmunityTip(immunity), Value: immunity)).ToList();
        RecordPicker.Show(owner, () => ctx.Store(), "Choose immunity set",
            term =>
            {
                term = term.Trim();
                uint.TryParse(term, out uint exact);
                return Task.FromResult<IReadOnlyList<PickerRecord>>(records
                    .Where(r => term.Length == 0 || r.Id == exact || r.Label.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.Id == exact ? 0 : 1).ThenBy(r => r.Label.Length).Take(60).ToList());
            },
            record => Set(ctx, field, row, (int)record.Id), current: current == 0 ? null : (uint)current);
    }

    static TableSchema CtxImmunities(CreatureContext ctx) => ctx.Schema.Tables.Find("creature_immunities");

    static string ImmunityName(CreatureContext ctx, int id) => id == 0
        ? "None"
        : ctx.Effective.All(CtxImmunities(ctx)).FirstOrDefault(r => r.I("ID") == id) is { } row
            ? ImmunityLabel(row)
            : $"Immunity set {id}";

    static string ImmunityLabel(Row row)
    {
        string comment = row.T("Comment").ReplaceLineEndings(" ").Trim();
        return comment.Length > 0 ? comment : $"Immunity set {row.I("ID")}";
    }

    static string ImmunityDetail(Row row) => $"#{row.I("ID")} · schools 0x{row.U("SchoolMask"):X} · mechanics 0x{row.UL("MechanicsMask"):X}";

    static Tooltip ImmunityTip(Row row)
    {
        List<TooltipLine> lines =
        [
            new(ImmunityLabel(row), "", TooltipTone.Title),
            new($"Set {row.I("ID")}", "", TooltipTone.Muted),
            new($"School mask: 0x{row.U("SchoolMask"):X}", "", TooltipTone.Normal),
            new($"Dispel mask: 0x{row.U("DispelTypeMask"):X}", "", TooltipTone.Normal),
            new($"Mechanic mask: 0x{row.UL("MechanicsMask"):X}", "", TooltipTone.Normal)
        ];
        if (row.T("Effects") is { Length: > 0 } effects)
        {
            lines.Add(new($"Effects: {effects}", "", TooltipTone.Muted));
        }

        if (row.T("Auras") is { Length: > 0 } auras)
        {
            lines.Add(new($"Auras: {auras}", "", TooltipTone.Muted));
        }

        return new(lines);
    }

    // ---- option lists ------------------------------------------------------------------------

    public static IReadOnlyList<(uint Value, string Name)> ClassOptions(CreatureContext ctx) => UnitClasses;

    public static IReadOnlyList<(uint Value, string Name)> AllClassOptions(CreatureContext ctx)
    {
        var client = ctx.Catalog?.Classes() ?? ctx.Lookup()?.Classes() ?? [];
        return client.Count == 0 ? UnitClasses : client.ToList();
    }

    public static IReadOnlyList<(uint Value, string Name)> TypeOptions(CreatureContext ctx)
    {
        var client = ctx.Catalog?.CreatureTypes() ?? ctx.Lookup()?.CreatureTypes() ?? [];
        return client.Count == 0 ? FallbackTypes : client.Prepend((0u, "None")).DistinctBy(t => t.Item1).ToList();
    }

    public static IReadOnlyList<(uint Value, string Name)> FamilyOptions(CreatureContext ctx)
    {
        var client = ctx.Catalog?.CreatureFamilies() ?? ctx.Lookup()?.CreatureFamilies() ?? [];
        return client.ToList();
    }

    static string Name(IReadOnlyList<(uint Value, string Name)> options, uint value, string fallback) =>
        options.FirstOrDefault(o => o.Value == value).Name is { Length: > 0 } name ? name : $"{fallback} {value}";

    static string? FactionName(CreatureContext ctx, uint id) => id == 0
        ? "None"
        : ctx.Catalog?.FactionTemplateName(id)
            ?? (ctx.Lookup()?.FactionTemplate(id) is { Name.Length: > 0 } faction ? $"{faction.Name} ({id})" : null);

    static Texture2D? CursorTexture(CreatureContext ctx, string name) =>
        name.Length == 0 ? null : ctx.Icons.Get($"interface/cursor/{name.ToLowerInvariant()}.blp") ?? ctx.Icons.Get(ClientStyle.MissingIcon);
}
