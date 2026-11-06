using System.Globalization;

namespace ACWorldEdit.Foundation;

public enum SpawnFieldKind
{
    /// <summary>A whole number.</summary>
    Number,
    /// <summary>A number with decimals.</summary>
    Decimal,
    /// <summary>Free text.</summary>
    Text,
    /// <summary>One of a few named values.</summary>
    Choice,
    /// <summary>A bit mask, typed in decimal or as 0x… hex.</summary>
    Flags
}

/// <summary>How a spawn column says "use the template's value".</summary>
public enum SpawnInherit
{
    /// <summary>The column always holds its own value.</summary>
    None,
    /// <summary>NULL inherits (TrinityCore's nullable flag, phase and string columns).</summary>
    Null,
    /// <summary>0 inherits (AzerothCore's flag columns), so an AzerothCore spawn cannot override a flag to 0.</summary>
    Zero
}

/// <summary>
/// One editable column of a spawn row, described once for the inspector: what it is called, where it
/// sits, how it is typed and whether it overrides the template. The TrinityCore and AzerothCore lists
/// differ because the tables do.
/// </summary>
public sealed record SpawnField(string Column, string Label, string Section, SpawnFieldKind Kind, string Tip = "",
    SpawnInherit Inherit = SpawnInherit.None, string? TemplateColumn = null, (uint Value, string Label)[]? Choices = null,
    double Min = 0, double Max = double.MaxValue, bool BlankMeansZero = false, bool Addon = false, long Mask = 0, int Shift = 0, bool BlankIsNull = false)
{
    public bool Inheritable => Inherit != SpawnInherit.None;

    /// <summary>The bits of a packed column this field owns (AzerothCore keeps stand and sheath state in the low bytes of `bytes1` and `bytes2`), or the whole column when there is no mask.</summary>
    public long Part(Row row) => Mask == 0 ? long.Parse(row[Column], CultureInfo.InvariantCulture) : (long)((ulong.Parse(row[Column], CultureInfo.InvariantCulture) >> Shift) & (ulong)Mask);

    /// <summary>The column value with this field's bits set to <paramref name="part"/>, the rest unchanged.</summary>
    public ulong Merge(Row row, long part) => (ulong.Parse(row[Column], CultureInfo.InvariantCulture) & ~((ulong)Mask << Shift)) | (((ulong)part & (ulong)Mask) << Shift);

    /// <summary>What an empty field says when it has no template value behind it.</summary>
    public string EmptyHint => TemplateColumn != null ? "template" : Column == "StringId" ? "none" : "default";

    /// <summary>True when the spawn sets its own value instead of using the template's.</summary>
    public bool IsOverridden(Row row) => Inherit switch
    {
        SpawnInherit.Null => !row.IsNull(Column),
        SpawnInherit.Zero => row[Column] != "0",
        _ => false
    };

    /// <summary>The text shown in the field: empty for an inherited value, so the template's value can show through.</summary>
    public string Show(Row row)
    {
        if (Inheritable && !IsOverridden(row))
        {
            return "";
        }

        if (row.IsNull(Column))
        {
            return "";
        }

        string value = Mask != 0 ? Part(row).ToString(CultureInfo.InvariantCulture) : row[Column];
        return Kind == SpawnFieldKind.Choice && Choices?.FirstOrDefault(c => c.Value.ToString(CultureInfo.InvariantCulture) == value) is { Label: { } label } ? label : value;
    }

    /// <summary>The value for "use the template": NULL, or 0.</summary>
    public object? InheritedValue => Inherit == SpawnInherit.Null ? null : 0;

    /// <summary>Read the typed text into the value the column holds; the error says what to fix.</summary>
    public (bool Ok, object? Value, string Error) Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0 && BlankMeansZero)
        {
            return (true, 0L, "");
        }

        if (text.Length == 0 && Inheritable)
        {
            return (true, InheritedValue, "");
        }

        switch (Kind)
        {
            case SpawnFieldKind.Text:
                return (true, text.Length == 0 && BlankIsNull ? null : text, "");
            case SpawnFieldKind.Choice:
                var choice = Choices?.FirstOrDefault(c => string.Equals(c.Label, text, StringComparison.OrdinalIgnoreCase) || c.Value.ToString(CultureInfo.InvariantCulture) == text);
                return choice is { Label: not null } ? (true, choice.Value.Value, "") : (false, null, $"{Label}: choose {string.Join(", ", Choices?.Select(c => c.Label) ?? [])}.");
            case SpawnFieldKind.Decimal:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) && number >= Min && number <= Max
                    ? (true, (float)number, "")
                    : (false, null, $"{Label}: enter a number{Range()} (use . for decimals).");
            default:
                bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                bool parsed = hex
                    ? long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long whole)
                    : long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole);
                return parsed && whole >= Min && whole <= Max
                    ? (true, whole, "")
                    : (false, null, $"{Label}: enter a whole number{Range()}.");
        }
    }

    string Range() => Max < 1e15 ? $" from {Min:0.##} to {Max:0.##}" : Min > 0 ? $" of at least {Min:0.##}" : "";
}

/// <summary>The inspector's field lists, and the way a typed value changes a spawn row.</summary>
public static class SpawnFields
{
    public const string SpawnSection = "Spawn", VisibilitySection = "Visibility", OverridesSection = "Overrides";

    static readonly (uint, string)[] Movement = [(0, "Stands still"), (1, "Wanders"), (2, "Follows a path")];
    static readonly (uint, string)[] ObjectState = [(0, "Active (open)"), (1, "Ready (closed)"), (2, "Alternative active")];

    public static IReadOnlyList<SpawnField> For(WorldDialect dialect, SpawnKind kind)
    {
        bool tc = dialect == WorldDialect.TrinityCore;
        var fields = new List<SpawnField>();
        if (kind == SpawnKind.Creature)
        {
            fields.Add(new("spawntimesecs", "Respawn after (s)", SpawnSection, SpawnFieldKind.Number, "How long, in seconds, after it is killed before this spawn returns.", Max: uint.MaxValue));
            fields.Add(new("MovementType", "Movement", SpawnSection, SpawnFieldKind.Choice, "What this spawn does when not fighting: stand still, wander within a radius, or follow a waypoint path.", Choices: Movement));
            fields.Add(new("wander_distance", "Wander radius (yd)", SpawnSection, SpawnFieldKind.Decimal, "How far from its spawn point it may wander, in yards. A value above 0 switches it to random movement; 0 keeps it standing still.", Max: 1000));
            fields.Add(tc
                ? new("curHealthPct", "Health (%)", SpawnSection, SpawnFieldKind.Number, "Starting health as a percentage of the template's maximum health. 100 is full health.", SpawnInherit.Null, Max: 100)
                : new("curhealth", "Health", SpawnSection, SpawnFieldKind.Number, "Starting health in hit points. Only matters for creatures whose template turns off health regeneration.", Max: uint.MaxValue));
        }
        else
        {
            fields.Add(new("spawntimesecs", "Respawn after (s)", SpawnSection, SpawnFieldKind.Number, "How long, in seconds, after it is used before this object returns. A negative value makes it start despawned.", Min: int.MinValue, Max: int.MaxValue));
            fields.Add(new("state", "State", SpawnSection, SpawnFieldKind.Choice, "The state this door, chest or gate starts in: ready (closed) or active (open).", Choices: ObjectState));
            fields.Add(new("animprogress", "Animation progress", SpawnSection, SpawnFieldKind.Number, "How far through its opening animation it starts, from 0 to 255. 255 is fully open or visible.", Max: 255));
        }

        if (tc)
        {
            fields.Add(new("spawnDifficulties", "Difficulties", VisibilitySection, SpawnFieldKind.Text, "Comma-separated difficulty IDs where this spawn appears. 0 is the open world."));
            fields.Add(new("PhaseId", "Phase", VisibilitySection, SpawnFieldKind.Number, "The phase this spawn belongs to. 0 or empty puts it in every phase.", Max: uint.MaxValue, BlankMeansZero: true));
            fields.Add(new("PhaseGroup", "Phase group", VisibilitySection, SpawnFieldKind.Number, "A group of phases that are shown together. Use a phase or a group, not both; 0 or empty means none.", Max: uint.MaxValue, BlankMeansZero: true));
            fields.Add(new("phaseUseFlags", "Phase flags", VisibilitySection, SpawnFieldKind.Flags, "How the phase behaves: 1 always visible, 2 inverse (visible outside the phase).", Max: 3));
        }
        else
        {
            fields.Add(new("spawnMask", "Difficulty mask", VisibilitySection, SpawnFieldKind.Flags, "Bit mask of the difficulties this spawn appears in: 1 is the open world, 2 normal dungeon, 4 heroic dungeon and so on.", Max: 255));
            fields.Add(new("phaseMask", "Phase mask", VisibilitySection, SpawnFieldKind.Flags, "Bit mask of the phases this spawn is visible in. 1 is the default phase.", Max: uint.MaxValue));
        }

        if (kind == SpawnKind.Creature)
        {
            fields.Add(new("equipment_id", "Equipment set", OverridesSection, SpawnFieldKind.Number, "Which weapon and armor set it wears: 0 none, -1 a random set from the template, or a set ID.", Min: -1, Max: 127));
            if (tc)
            {
                fields.Add(new("modelid", "Model", OverridesSection, SpawnFieldKind.Number, "CreatureDisplayInfo ID of the model this spawn uses. 0 uses the template's model.", Max: uint.MaxValue));
                fields.Add(new("npcflag", "NPC flags", OverridesSection, SpawnFieldKind.Flags, "The right-click services of this spawn. Empty uses the template's value.", SpawnInherit.Null, "npcflag", Max: long.MaxValue));
                fields.Add(new("unit_flags", "Unit flags", OverridesSection, SpawnFieldKind.Flags, "State and combat switches for this spawn. Empty uses the template's value.", SpawnInherit.Null, "unit_flags", Max: uint.MaxValue));
                fields.Add(new("unit_flags2", "Unit flags 2", OverridesSection, SpawnFieldKind.Flags, "Additional state switches for this spawn. Empty uses the template's value.", SpawnInherit.Null, "unit_flags2", Max: uint.MaxValue));
                fields.Add(new("unit_flags3", "Unit flags 3", OverridesSection, SpawnFieldKind.Flags, "Further state switches for this spawn. Empty uses the template's value.", SpawnInherit.Null, "unit_flags3", Max: uint.MaxValue));
            }
            else
            {
                const string zero = "0 uses the template's value; a flag cannot be overridden to 0.";
                fields.Add(new("npcflag", "NPC flags", OverridesSection, SpawnFieldKind.Flags, "The right-click services of this spawn. " + zero, SpawnInherit.Zero, "npcflag", Max: uint.MaxValue));
                fields.Add(new("unit_flags", "Unit flags", OverridesSection, SpawnFieldKind.Flags, "State and combat switches for this spawn. " + zero, SpawnInherit.Zero, "unit_flags", Max: uint.MaxValue));
                fields.Add(new("dynamicflags", "Dynamic flags", OverridesSection, SpawnFieldKind.Flags, "Runtime display flags such as lootable or tapped, normally set by the server. " + zero, SpawnInherit.Zero, "dynamicflags", Max: uint.MaxValue));
            }
        }

        fields.Add(new("ScriptName", "Script", OverridesSection, SpawnFieldKind.Text, "Name of a compiled C++ script bound to this one spawn. Empty uses the template's script.", TemplateColumn: "ScriptName"));
        if (tc)
        {
            fields.Add(new("StringId", "String ID", OverridesSection, SpawnFieldKind.Text, "A name scripts use to find this spawn. Empty means none.", SpawnInherit.Null));
        }

        return fields;
    }

    static readonly (uint, string)[] StandStates = [(0, "Standing"), (1, "Sitting"), (2, "Sitting on a chair"), (3, "Sleeping"), (4, "Sitting on a low chair"), (5, "Sitting on a medium chair"), (6, "Sitting on a high chair"), (7, "Dead"), (8, "Kneeling"), (9, "Submerged")];
    static readonly (uint, string)[] SheathStates = [(0, "Hands free"), (1, "Melee weapon drawn"), (2, "Ranged weapon drawn")];
    public const string AppearanceSection = "Appearance";

    /// <summary>
    /// The fields of the spawn's addon row (`creature_addon`, `gameobject_addon`): how it stands, what it
    /// wears, rides and casts. A spawn without an addon row shows defaults and gets a row when one is set.
    /// </summary>
    public static IReadOnlyList<SpawnField> AddonFor(WorldDialect dialect, SpawnKind kind)
    {
        bool tc = dialect == WorldDialect.TrinityCore;
        if (kind == SpawnKind.GameObject)
        {
            return
            [
                new("invisibilityType", "Invisibility type", AppearanceSection, SpawnFieldKind.Number, "The kind of invisibility it starts with. 0 is visible to everyone; other types hide it from players without the matching detection.", Max: 31, Addon: true),
                new("invisibilityValue", "Invisibility value", AppearanceSection, SpawnFieldKind.Number, "How strong the invisibility is. Higher values need stronger detection to see through.", Max: uint.MaxValue, Addon: true)
            ];
        }

        var fields = new List<SpawnField>
        {
            new("mount", "Mount model", AppearanceSection, SpawnFieldKind.Number, "CreatureDisplayInfo ID of the mount it rides. 0 means it is not mounted.", Max: uint.MaxValue, Addon: true)
        };
        if (tc)
        {
            fields.Add(new("MountCreatureID", "Mount creature", AppearanceSection, SpawnFieldKind.Number, "The creature entry whose model it rides. 0 means it is not mounted.", Max: uint.MaxValue, Addon: true));
            fields.Add(new("StandState", "Stance", AppearanceSection, SpawnFieldKind.Choice, "How the creature stands while idle: standing, sitting, sleeping, kneeling and so on.", Choices: StandStates, Addon: true));
            fields.Add(new("SheathState", "Weapons", AppearanceSection, SpawnFieldKind.Choice, "Which weapons the creature has drawn while idle.", Choices: SheathStates, Addon: true));
        }
        else
        {
            fields.Add(new("bytes1", "Stance", AppearanceSection, SpawnFieldKind.Choice, "How the creature stands while idle (the low byte of bytes1).", Choices: StandStates, Addon: true, Mask: 0xFF));
            fields.Add(new("bytes2", "Weapons", AppearanceSection, SpawnFieldKind.Choice, "Which weapons the creature has drawn while idle (the low byte of bytes2).", Choices: SheathStates, Addon: true, Mask: 0xFF));
        }

        fields.Add(new("emote", "Emote", AppearanceSection, SpawnFieldKind.Number, "The emote it plays while idle, such as waving or dancing. 0 means none.", Max: uint.MaxValue, Addon: true));
        fields.Add(new("auras", "Auras", AppearanceSection, SpawnFieldKind.Text, "Aura spell IDs applied when it spawns, separated by spaces. Empty means no auras.", Addon: true, BlankIsNull: tc));
        return fields;
    }


    /// <summary>
    /// The row with every appearance field set to the template's value, and every other column (a waypoint path,
    /// an animation kit) left alone. A packed AzerothCore field copies only its own bits.
    /// </summary>
    public static Row CopyAppearance(WorldDialect dialect, SpawnKind kind, Row row, Row template)
    {
        foreach (var field in AddonFor(dialect, kind))
        {
            row = field.Mask != 0 ? row.With(field.Column, field.Merge(row, field.Part(template))) : row.With(field.Column, template.IsNull(field.Column) ? null : template[field.Column]);
        }

        return row;
    }
    /// <summary>
    /// The radius the server lets a creature wander within, or null when it stands still or follows a path.
    /// Only a random-movement creature wanders; the radius is measured from the spawn point.
    /// </summary>
    public static float? WanderRadius(Row? row) =>
        row != null && row.U("MovementType") == 1 && row.F("wander_distance") is > 0 and var radius ? radius : null;

    /// <summary>The spawn fields followed by the addon fields: everything the inspector shows.</summary>
    public static IReadOnlyList<SpawnField> All(WorldDialect dialect, SpawnKind kind) => [.. For(dialect, kind), .. AddonFor(dialect, kind)];

    /// <summary>The template columns to read so the inspector can show what an empty override would use.</summary>
    public static IReadOnlyList<string> TemplateColumns(WorldDialect dialect, SpawnKind kind) => For(dialect, kind).Where(f => f.TemplateColumn != null).Select(f => f.TemplateColumn!).ToArray();

    /// <summary>
    /// The row with one typed value applied. A creature's movement and wander radius move together the
    /// way the server's `.npc set movetype` and `.npc set spawndist` commands move them: a radius makes
    /// it wander, no radius makes it stand still, and asking it to wander without a radius gives it 5 yd.
    /// </summary>
    public static (Row? Row, string Error) Apply(SpawnField field, Row row, string text)
    {
        var (ok, value, error) = field.Parse(text);
        if (!ok)
        {
            return (null, error);
        }

        var changed = field.Mask != 0 ? row.With(field.Column, field.Merge(row, Convert.ToInt64(value, CultureInfo.InvariantCulture))) : row.With(field.Column, value);
        if (field.Column == "wander_distance")
        {
            double radius = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            uint movement = row.U("MovementType");
            changed = radius > 0 ? (movement == 0 ? changed.With("MovementType", 1u) : changed) : movement == 1 ? changed.With("MovementType", 0u) : changed;
        }
        else if (field.Column == "MovementType")
        {
            uint movement = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            if (movement == 1 && row.F("wander_distance") == 0)
            {
                changed = changed.With("wander_distance", 5f);
            }
            else if (movement == 0)
            {
                changed = changed.With("wander_distance", 0f);
            }
        }

        return (changed, "");
    }
}
