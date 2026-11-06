using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Builds the pages of the GameObject properties window. The template page is fixed; the
/// type page is rebuilt from the generated catalogue for the row's current type. The raw
/// Data page is the fallback for a type this server does not know.
/// </summary>
public static class GameObjectPage
{
    static readonly GroupSpec[] TemplateGroups =
    [
        new("identity", "Identity"),
        new("scaling", "Scaling"),
        new("appearance", "Appearance and interaction"),
        new("links", "Links and scripting")
    ];

    public static EntityPage TemplatePage(WorldDialect dialect)
    {
        var tables = WorldSchemas.For(dialect).Tables;
        var template = tables.Find("gameobject_template");
        var addon = tables.Find("gameobject_template_addon");
        List<FieldSpec> fields =
        [
            new("identity", template, "name", "Name", "The name shown above the object in the world and in the content browser.", FieldKind.Text),
            new("identity", template, "displayId", "Display", "GameObjectDisplayInfo.dbc ID of the model drawn in the world and in the preview above. 0 means no model.", FieldKind.Whole),
            new("identity", template, "size", "Size", "Scale multiplier for the display model; 1 is its normal size. This does not change the interaction radius.", FieldKind.Decimal)
        ];
        if (template.Has("ContentTuningId"))
        {
            fields.Add(new("scaling", template, "ContentTuningId", "Content tuning", "ContentTuning.db2 ID used to scale the object's level and health; 0 uses the map's tuning.", FieldKind.Whole));
        }

        if (template.Has("RequiredLevel"))
        {
            fields.Add(new("scaling", template, "RequiredLevel", "Required level", "The player level required to interact with it; 0 means no level requirement.", FieldKind.Whole));
        }

        fields.Add(new("appearance", addon, "faction", "Faction", "FactionTemplate.dbc ID that decides which players can interact with it. 0 is neutral.", FieldKind.Whole, Reference: "FactionTemplate"));
        fields.Add(new("appearance", addon, "flags", "Flags", "Server behavior bits such as locked, transport or never-despawn. The raw value keeps every bit.", FieldKind.Flags, Flags: GameObjectFlags.For(dialect)));
        fields.Add(new("appearance", addon, "mingold", "Minimum money", "The lowest amount of money it can drop, in copper (100 copper = 1 silver). 0 means no money.", FieldKind.Whole, Display: Money));
        fields.Add(new("appearance", addon, "maxgold", "Maximum money", "The highest amount of money it can drop, in copper (100 copper = 1 silver). 0 means no money.", FieldKind.Whole, Display: Money));
        foreach (string artkit in new[] { "artkit0", "artkit1", "artkit2", "artkit3", "artkit4" })
        {
            if (addon.Has(artkit))
            {
                fields.Add(new("appearance", addon, artkit, $"Art kit {artkit[^1]}", "Art kit index applied to the display model from the client's GameObjectArtKit table; 0 means the default kit.", FieldKind.Whole));
            }
        }

        fields.Add(new("links", template, "AIName", "AI", "The server AI that runs it. SmartAI rules only run when this is exactly \"SmartAI\"; a blank value uses the default AI.", FieldKind.Text, AllowEmpty: true, Display: None));
        fields.Add(new("links", template, "ScriptName", "C++ script", "Name of a compiled C++ script. A script can replace the template's normal behavior; leave it blank unless the script exists on the server.", FieldKind.Text, AllowEmpty: true, Display: None));
        return new(TemplateGroups, fields.ToArray());
    }

    /// <summary>The fields of one type, in Data-index order. Unused fields are excluded unless asked for.</summary>
    public static EntityPage TypePage(WorldDialect dialect, int typeId, bool includeUnused = false)
    {
        var template = WorldSchemas.For(dialect).Tables.Find("gameobject_template");
        var type = GameObjectTypeCatalog.For(dialect).Find(typeId);
        if (type == null)
        {
            return RawPage(dialect);
        }

        var fields = type.Fields.Where(f => includeUnused || !f.Unused).Select(f => Spec(template, f)).ToArray();
        return new([new GroupSpec("type", $"{type.Name} properties", type.Hint.Length > 0 ? type.Hint : "The fields this type reads from the Data columns.")], fields);
    }

    /// <summary>Only the fields the type marks unused, for the "Show unused fields" toggle.</summary>
    public static EntityPage UnusedPage(WorldDialect dialect, int typeId)
    {
        var template = WorldSchemas.For(dialect).Tables.Find("gameobject_template");
        var type = GameObjectTypeCatalog.For(dialect).Find(typeId);
        var fields = type?.UnusedFields.Select(f => Spec(template, f)).ToArray() ?? [];
        return new([new GroupSpec("unused", "Unused fields", "These columns are not read by this type. They keep their stored values.")], fields);
    }

    /// <summary>Every Data column as a raw number: the fallback for an unknown type and the raw toggle.</summary>
    public static EntityPage RawPage(WorldDialect dialect)
    {
        var template = WorldSchemas.For(dialect).Tables.Find("gameobject_template");
        var fields = template.Columns
            .Where(c => c.Name.StartsWith("Data", StringComparison.Ordinal) && int.TryParse(c.Name[4..], out _))
            .Select(c => new FieldSpec("raw", template, c.Name, "Data " + c.Name[4..], "Raw Data column. Its meaning depends on the type.", FieldKind.Whole))
            .ToArray();
        return new([new GroupSpec("raw", "Raw Data columns", "The type has no catalogue entry; every Data column is editable here.")], fields);
    }

    static FieldSpec Spec(TableSchema template, GameObjectField field)
    {
        FieldKind kind = field.Kind switch
        {
            GameObjectFieldKind.Bool => FieldKind.Check,
            GameObjectFieldKind.Choice => FieldKind.Choice,
            _ => FieldKind.Whole
        };
        (int, string)[]? options = field.Options?.Select(o => (o.Value, o.Name)).ToArray();
        ulong max = field.HasMax ? (ulong)field.Max : 0;
        return new FieldSpec("type", template, "Data" + field.Index.ToString(CultureInfo.InvariantCulture), field.Label, field.Tip,
            kind, Options: options, Max: max, Display: Display(field), Reference: field.Reference, Scale: field.Scale);
    }

    static Func<string, string>? Display(GameObjectField field)
    {
        if (field.Scale != 1 && field.Scale > 0)
        {
            return raw => FormatScaled(raw, field.Scale);
        }

        if (field.Kind == GameObjectFieldKind.Milliseconds)
        {
            return raw => raw + " ms";
        }

        return null;
    }

    /// <summary>Show a scaled stored value (AC autoCloseTime: seconds times 65536).</summary>
    public static string FormatScaled(string raw, double scale) =>
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? (value / scale).ToString("0.###", CultureInfo.InvariantCulture)
            : raw;

    static string Money(string copper) =>
        uint.TryParse(copper, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) && value > 0
            ? $"{value / 10000}g {value / 100 % 100}s {value % 100}c"
            : "None";

    static string None(string text) => text.Length > 0 ? text : "None";
}
