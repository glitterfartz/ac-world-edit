using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore creature_template plus one creature_template_difficulty row, chosen in a picker that
/// lists the difficulties the creature has (stats, loot and gold live per difficulty). With a draft
/// open both rows are editable; a difficulty the creature has no row for cannot be added yet.
/// </summary>
public sealed partial class TcTemplateSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "template";
    public override string Title => "Template";
    int? difficulty;
    GridContainer grid = null!;

    bool Editing => Ctx.TcTemplateEditable;

    public override void OnCreatureChanged(CreatureSpawn? creature) => difficulty = null;

    protected override void Populate(TcCreatureView v)
    {
        var t = v.Template;
        var schema = Ctx.Schema.Creature;
        Heading("Creature");
        if (Editing)
        {
            Line($"The template applies to every spawn of entry {v.Entry}.", ClientStyle.Warning, 11);
        }

        var flags = TcCreatureView.DescribeNpcFlags(v.NpcFlags);
        var table = TcTables.CreatureTemplate;
        NewGrid();
        Field("Name", t.T("name"), () => Text(table, t, "name", "name", false));
        Field("Subname", t.T("subname"), () => Text(table, t, "subname", "subname", true));
        Field("Cursor icon", t.T("IconName"), () => Text(table, t, "IconName", "cursor icon", true));
        Field("Classification", ClassificationName(schema, t.U("Classification")), () => Choice(table, t, "Classification", "classification", schema.Classifications));
        Field("Faction", t.T("faction"), () => Whole(table, t, "faction", "faction"));
        Field("Class", t.T("unit_class"), () => Whole(table, t, "unit_class", "class"));
        Field("Creature type", t.T("type"), () => Whole(table, t, "type", "creature type"));
        Field("Family", t.T("family"), () => Whole(table, t, "family", "family"));
        Field("Trainer class", t.T("trainer_class"), () => Whole(table, t, "trainer_class", "trainer class"));
        Field("Unit flags", Hex(t.U("unit_flags")), () => RawFlags(table, t, "unit_flags", "unit flags"));
        Field("Unit flags 2", Hex(t.U("unit_flags2")), () => RawFlags(table, t, "unit_flags2", "unit flags 2"));
        Field("Unit flags 3", Hex(t.U("unit_flags3")), () => RawFlags(table, t, "unit_flags3", "unit flags 3"));
        Field("Extra flags", Hex(t.U("flags_extra")), () => RawFlags(table, t, "flags_extra", "extra flags"));
        Field("Walk speed", Number(t.F("speed_walk")), () => Decimal(table, t, "speed_walk", "walk speed"));
        Field("Run speed", Number(t.F("speed_run")), () => Decimal(table, t, "speed_run", "run speed"));
        Field("Scale", Number(t.F("scale")), () => Decimal(table, t, "scale", "scale"));
        Field("Movement type", t.T("MovementType"), () => Whole(table, t, "MovementType", "movement type"));
        Field("Vehicle", t.T("VehicleId"), () => Whole(table, t, "VehicleId", "vehicle"));
        Field("Required expansion", t.T("RequiredExpansion"), () => Whole(table, t, "RequiredExpansion", "required expansion"));
        Field("AI", t.T("AIName").Length > 0 ? t.T("AIName") : "None", () => Text(table, t, "AIName", "AI", true));
        Field("C++ script", t.T("ScriptName").Length > 0 ? t.T("ScriptName") : "None", () => Text(table, t, "ScriptName", "C++ script", true));

        AddChild(ClientStyle.Tag("NPC flags"));
        if (Editing)
        {
            var services = new GridContainer
            {
                Columns = 3
            };
            AddChild(services);
            foreach (var (bit, name) in schema.NpcFlags)
            {
                var check = new CheckBox
                {
                    Text = name,
                    ButtonPressed = (v.NpcFlags & bit) != 0
                };
                check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {name} flag", table, t, t.With("npcflag", on ? v.NpcFlags | bit : v.NpcFlags & ~bit));
                services.AddChild(check);
            }

            AddChild(RawBox(table, t, "npcflag", "NPC flags", v.NpcFlags));
        }
        else
        {
            Line(flags.Count == 0 ? "None" : string.Join(", ", flags));
        }

        AddChild(new HSeparator());
        Heading("Difficulty");
        if (v.Difficulties.Length == 0)
        {
            Line("This creature has no creature_template_difficulty rows.", ClientStyle.Warning);
            return;
        }

        var picker = new OptionButton
        {
            Name = "DifficultyPicker",
            TooltipText = "Stats, loot and gold are stored per difficulty. Choose which row to show."
        };
        foreach (var d in v.Difficulties)
        {
            picker.AddItem(DifficultyName(d.DifficultyId), d.DifficultyId);
        }

        var shown = v.Difficulties.FirstOrDefault(d => d.DifficultyId == difficulty) ?? v.Difficulties[0];
        difficulty = shown.DifficultyId;
        picker.Select(Array.IndexOf(v.Difficulties, shown));
        picker.ItemSelected += index =>
        {
            difficulty = v.Difficulties[(int)index].DifficultyId;
            Refresh();
        };
        AddChild(picker);
        var r = shown.Row;
        var dt = TcTables.CreatureDifficulty;
        NewGrid();
        Field("Content tuning", r.T("ContentTuningID"), () => Whole(dt, r, "ContentTuningID", "content tuning"));
        Field("Health scaling expansion", r.T("HealthScalingExpansion"), () => Whole(dt, r, "HealthScalingExpansion", "health scaling expansion"));
        Field("Health modifier", Number(r.F("HealthModifier")), () => Decimal(dt, r, "HealthModifier", "health modifier"));
        Field("Mana modifier", Number(r.F("ManaModifier")), () => Decimal(dt, r, "ManaModifier", "mana modifier"));
        Field("Armor modifier", Number(r.F("ArmorModifier")), () => Decimal(dt, r, "ArmorModifier", "armor modifier"));
        Field("Damage modifier", Number(r.F("DamageModifier")), () => Decimal(dt, r, "DamageModifier", "damage modifier"));
        Field("Creature difficulty", r.T("CreatureDifficultyID"), () => Whole(dt, r, "CreatureDifficultyID", "creature difficulty"));
        Field("Loot", r.T("LootID"), () => Whole(dt, r, "LootID", "loot"));
        Field("Pickpocket loot", r.T("PickPocketLootID"), () => Whole(dt, r, "PickPocketLootID", "pickpocket loot"));
        Field("Skinning loot", r.T("SkinLootID"), () => Whole(dt, r, "SkinLootID", "skinning loot"));
        Field("Gold min (copper)", Editing ? r.T("GoldMin") : GoldText(r.U("GoldMin")), () => Whole(dt, r, "GoldMin", "minimum gold"));
        Field("Gold max (copper)", Editing ? r.T("GoldMax") : GoldText(r.U("GoldMax")), () => Whole(dt, r, "GoldMax", "maximum gold"));
        Field("Type flags", Hex(r.U("TypeFlags")), () => RawFlags(dt, r, "TypeFlags", "type flags"));
        Field("Type flags 2", Hex(r.U("TypeFlags2")), () => RawFlags(dt, r, "TypeFlags2", "type flags 2"));
        Field("Type flags 3", Hex(r.U("TypeFlags3")), () => RawFlags(dt, r, "TypeFlags3", "type flags 3"));
        for (int i = 1; i <= 8; i++)
        {
            string column = $"StaticFlags{i}";
            Field($"Static flags {i}", Hex(r.U(column)), () => RawFlags(dt, r, column, $"static flags {i}"));
        }
    }

    void NewGrid()
    {
        grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 2);
        AddChild(grid);
    }

    void Field(string caption, string shown, Func<Control> editor)
    {
        grid.AddChild(ClientStyle.Tag(caption));
        if (Editing)
        {
            grid.AddChild(editor());
            return;
        }

        var label = ClientStyle.Text(shown, 13, ClientStyle.Body);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddChild(label);
    }

    void Commit(string label, TableSchema table, Row before, Row after) => Ctx.Commit(label, [Update(table, before, after)]);

    void Reject(string message)
    {
        Ctx.Status = $"Not saved: {message}";
        Ctx.Redraw();
    }

    LineEdit Text(TableSchema table, Row row, string column, string label, bool allowEmpty)
    {
        int max = table.Columns.First(c => c.Name == column).MaxLength;
        var edit = new LineEdit
        {
            Text = row.T(column),
            MaxLength = max,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(220, 0)
        };
        edit.TextSubmitted += text =>
        {
            if (!allowEmpty && text.Trim().Length == 0)
            {
                Reject($"the {label} cannot be empty.");
                return;
            }

            Commit($"Change {label}", table, row, row.With(column, text.Trim()));
        };
        return edit;
    }

    /// <summary>A whole-number box for an unsigned or signed column of the table.</summary>
    LineEdit Whole(TableSchema table, Row row, string column, string label)
    {
        bool signed = table.Columns.First(c => c.Name == column).Kind == ColumnKind.Int;
        var edit = new LineEdit
        {
            Text = row.T(column),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Ctx.Try(() => Commit($"Change {label}", table, row, row.With(column, signed ? int.Parse(text.Trim(), CultureInfo.InvariantCulture) : ParseUInt(text))));
        return edit;
    }

    LineEdit Decimal(TableSchema table, Row row, string column, string label)
    {
        var edit = new LineEdit
        {
            Text = Number(row.F(column)),
            CustomMinimumSize = new(110, 0)
        };
        edit.TextSubmitted += text => Ctx.Try(() =>
        {
            float value = float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!float.IsFinite(value) || value < 0)
            {
                Reject($"the {label} must be zero or more.");
                return;
            }

            Commit($"Change {label}", table, row, row.With(column, value));
        });
        return edit;
    }

    LineEdit RawFlags(TableSchema table, Row row, string column, string label)
    {
        var edit = new LineEdit
        {
            Text = Hex(row.U(column)),
            CustomMinimumSize = new(110, 0),
            TooltipText = "The combined number stored in the database. Decimal or 0x hex."
        };
        edit.TextSubmitted += text => Ctx.Try(() => Commit($"Change {label}", table, row, row.With(column, ParseUInt(text))));
        return edit;
    }

    /// <summary>The 64-bit value box of npcflag (decimal or 0x hex).</summary>
    Control RawBox(TableSchema table, Row row, string column, string label, ulong value)
    {
        var box = new HBoxContainer();
        box.AddChild(ClientStyle.Tag("Raw value"));
        var edit = new LineEdit
        {
            Text = value == 0 ? "0" : "0x" + value.ToString("X", CultureInfo.InvariantCulture),
            CustomMinimumSize = new(160, 0)
        };
        edit.TextSubmitted += text => Ctx.Try(() =>
        {
            string trimmed = text.Trim();
            ulong parsed = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : ulong.Parse(trimmed, CultureInfo.InvariantCulture);
            Commit($"Change {label}", table, row, row.With(column, parsed));
        });
        box.AddChild(edit);
        return box;
    }

    OptionButton Choice(TableSchema table, Row row, string column, string label, (int Value, string Name)[] options)
    {
        int current = (int)row.U(column);
        var picker = new OptionButton();
        foreach (var (value, name) in options)
        {
            picker.AddItem(name, value);
        }

        int index = picker.GetItemIndex(current);
        if (index < 0)
        {
            picker.AddItem($"Unknown ({current})", current);
            index = picker.ItemCount - 1;
        }

        picker.Selected = index;
        picker.ItemSelected += i => Commit($"Change {label}", table, row, row.With(column, (uint)picker.GetItemId((int)i)));
        return picker;
    }

    static string ClassificationName(CreatureSchema schema, uint value) => schema.Classifications.FirstOrDefault(c => c.Value == value).Name ?? $"Unknown ({value})";

    static string GoldText(uint copper) => copper == 0 ? "None" : ClientStyle.Money(copper);

    /// <summary>The difficulty ID with the names the scene sidebar uses for the common ones.</summary>
    static string DifficultyName(int id) => id switch
    {
        0 => "Open world (0)",
        1 => "Normal dungeon (1)",
        2 => "Heroic dungeon (2)",
        14 => "Normal raid (14)",
        15 => "Heroic raid (15)",
        16 => "Mythic raid (16)",
        23 => "Mythic dungeon (23)",
        _ => $"Difficulty {id}"
    };
}
