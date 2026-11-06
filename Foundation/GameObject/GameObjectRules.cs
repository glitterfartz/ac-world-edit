using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Server-side checks for one GameObject template: the type exists, the Data fields are in the
/// catalogue's range, the addon money is ordered and the quest item list is not over the
/// server's cap. An unknown type is a warning, never an error, so a newer server type still opens.
/// </summary>
public static class GameObjectRules
{
    public static IEnumerable<InteractionIssue> Check(WorldDialect dialect, RowSet rows, uint entry)
    {
        var tables = WorldSchemas.For(dialect).Tables;
        var templateTable = tables.Find("gameobject_template");
        string key = entry.ToString(CultureInfo.InvariantCulture);
        if (rows.Get(templateTable, key) is not { } template)
        {
            yield return new(true, $"GameObject template {entry} does not exist.");
            yield break;
        }

        int type = (int)template.U("type");
        var catalog = GameObjectTypeCatalog.For(dialect);
        var spec = catalog.Find(type);
        if (spec == null)
        {
            yield return new(false, $"Type {type} has no field catalogue; the raw Data columns are shown.");
        }
        else
        {
            foreach (var field in spec.Fields)
            {
                string column = "Data" + field.Index.ToString(CultureInfo.InvariantCulture);
                if (!templateTable.Has(column))
                {
                    continue;
                }

                long value = Value(template, templateTable, column);
                if (field.Min != int.MinValue && value < field.Min)
                {
                    yield return new(true, $"{spec.Name}, {field.Label} must be at least {field.Min}.");
                }

                if (field.HasMax && value > field.Max)
                {
                    yield return new(true, $"{spec.Name}, {field.Label} must be at most {field.Max}.");
                }

                if (field.Kind == GameObjectFieldKind.Choice && field.Options != null &&
                    field.Options.All(o => o.Value != (int)value))
                {
                    yield return new(true, $"{spec.Name}, {field.Label} must be one of the listed values.");
                }
            }
        }

        var addonTable = tables.Find("gameobject_template_addon");
        if (rows.Get(addonTable, key) is { } addon && addon.U("mingold") > addon.U("maxgold"))
        {
            yield return new(true, "The minimum money cannot be more than the maximum money.");
        }

        var itemTable = tables.Find("gameobject_questitem");
        var items = rows.All(itemTable).Where(r => r.U("GameObjectEntry") == entry).ToList();
        if (items.Count > 6)
        {
            yield return new(true, "A GameObject can have at most six quest items.");
        }

        foreach (var item in items)
        {
            if (item.U("Idx") > 5)
            {
                yield return new(true, "A quest item index must be between 0 and 5.");
            }

            if (item.U("ItemId") == 0)
            {
                yield return new(true, $"Quest item {item.U("Idx")} has no item.");
            }
        }

        if (template.T("ScriptName").Length > 0)
        {
            yield return new(false, $"The template has the C++ script {template.T("ScriptName")}; it can replace the template's data.");
        }
    }

    static long Value(Row row, TableSchema table, string column)
    {
        var kind = table.Columns.First(c => c.Name == column).Kind;
        return kind switch
        {
            ColumnKind.Int => row.I(column),
            ColumnKind.UInt => row.U(column),
            ColumnKind.Long => row.L(column),
            ColumnKind.ULong => (long)row.UL(column),
            _ => 0
        };
    }
}
