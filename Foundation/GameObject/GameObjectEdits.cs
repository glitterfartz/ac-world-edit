using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>The row arithmetic behind the GameObject pages: type changes and the optional addon row.</summary>
public static class GameObjectEdits
{
    /// <summary>
    /// Change the type and reset every Data column to the new type's defaults, as one command.
    /// Columns the new type does not read become 0; columns it reads get the catalogue default.
    /// </summary>
    public static RowEdit ChangeType(WorldDialect dialect, Row template, int type)
    {
        var table = WorldSchemas.For(dialect).Tables.Find("gameobject_template");
        var spec = GameObjectTypeCatalog.For(dialect).Find(type);
        Row after = template.With("type", (uint)type);
        foreach (var (name, _, _) in table.Columns.Where(c => c.Name.StartsWith("Data", StringComparison.Ordinal) && int.TryParse(c.Name[4..], out _)))
        {
            int index = int.Parse(name[4..], CultureInfo.InvariantCulture);
            int value = spec?.Field(index)?.Default ?? 0;
            after = after.With(name, value);
        }

        return InteractionEdits.Update(table, template, after);
    }

    /// <summary>A complete addon row with the dialect's defaults; the first edit inserts it.</summary>
    public static Row NewAddon(WorldDialect dialect, uint entry)
    {
        var table = WorldSchemas.For(dialect).Tables.Find("gameobject_template_addon");
        var row = new Row(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, "0")));
        return row.With("entry", entry);
    }

    /// <summary>A quest item row at the given index.</summary>
    public static Row NewQuestItem(WorldDialect dialect, uint entry, uint index, uint item) =>
        Row.Of(("GameObjectEntry", entry), ("Idx", index), ("ItemId", item));
}
