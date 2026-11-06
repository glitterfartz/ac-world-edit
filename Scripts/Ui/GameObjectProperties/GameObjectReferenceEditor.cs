using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The specialized editors of the GameObject type page: a reference picker where a search
/// exists, and a scaled number for fields the server stores scaled (AzerothCore's
/// autoCloseTime stores seconds times 65536). A field with neither returns null, so the
/// standard <see cref="PageFields"/> editor is used.
/// </summary>
public static class GameObjectReferenceEditor
{
    public static Control? Build(Node owner, FieldSpec field, Row row, GameObjectContext ctx)
    {
        bool scaled = Math.Abs(field.Scale - 1) > 0.000001;
        Func<string, Task<IReadOnlyList<(uint Id, string Name)>>>? search = field.Reference is { Length: > 0 } reference
            ? Search(ctx, reference)
            : null;
        if (!scaled && search == null)
        {
            return null;
        }

        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        var edit = new LineEdit
        {
            Text = scaled ? GameObjectPage.FormatScaled(row.T(field.Column), field.Scale) : row.T(field.Column),
            CustomMinimumSize = new(110, 0),
            TooltipText = field.Tip
        };
        edit.TextSubmitted += text => ctx.Try(() => Set(ctx, field, row, text));
        box.AddChild(edit);
        if (search != null)
        {
            var choose = new Button { Text = "Choose…", TooltipText = $"Search for the {field.Label.ToLowerInvariant()}." };
            choose.Pressed += () => Pick(owner, ctx, field, row, search);
            box.AddChild(choose);
        }

        return box;
    }

    static void Pick(Node owner, GameObjectContext ctx, FieldSpec field, Row row, Func<string, Task<IReadOnlyList<(uint Id, string Name)>>> search)
    {
        uint? current = uint.TryParse(row.T(field.Column), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : null;
        PickerPopup.Show(owner, $"Choose {field.Label}", search, (id, _) => Set(ctx, field, row, id.ToString(CultureInfo.InvariantCulture)), current: current);
    }

    static void Set(GameObjectContext ctx, FieldSpec field, Row row, string text)
    {
        object value;
        if (Math.Abs(field.Scale - 1) > 0.000001)
        {
            double shown = double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            value = checked((long)Math.Round(shown * field.Scale));
        }
        else
        {
            value = Parse(field.Table, field.Column, text.Trim());
        }

        var after = row.With(field.Column, value);
        var table = field.Table;
        var edit = ctx.Effective.Get(table, table.KeyOf(row)) == null
            ? InteractionEdits.Insert(table, after)
            : InteractionEdits.Update(table, row, after);
        ctx.Commit($"Change {field.Label.ToLowerInvariant()}", [edit]);
    }

    static object Parse(TableSchema table, string column, string text) => table.Columns.First(c => c.Name == column).Kind switch
    {
        ColumnKind.Int => int.Parse(text, CultureInfo.InvariantCulture),
        ColumnKind.UInt => uint.Parse(text, CultureInfo.InvariantCulture),
        ColumnKind.Long => long.Parse(text, CultureInfo.InvariantCulture),
        ColumnKind.ULong => ulong.Parse(text, CultureInfo.InvariantCulture),
        ColumnKind.Float => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
        _ => text
    };

    /// <summary>The picker search for a reference tag, or null when there is no searchable source.</summary>
    static Func<string, Task<IReadOnlyList<(uint Id, string Name)>>>? Search(GameObjectContext ctx, string reference) => reference switch
    {
        "FactionTemplate" => ctx.Catalog is { } catalog
            ? term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(
                catalog.All("FactionTemplate")
                    .Where(f => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || f.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
                    .OrderBy(f => f.Id)
                    .Take(50)
                    .ToList())
            : null,
        "BroadcastText" => term => DBSource.Open(ctx.Connection(), ctx.Lookup()).SearchBroadcastAsync(term, CancellationToken.None),
        "Spell" => term => DBSource.Open(ctx.Connection(), ctx.Lookup()).SearchAsync(SmartParamKind.Spell, term, CancellationToken.None),
        "Gossip" => term => DBSource.Open(ctx.Connection()).SearchMenusAsync(term, CancellationToken.None),
        "GameObject" => term => SearchGameObjects(ctx, term),
        "Quest" => term => SearchQuests(ctx, term),
        "Creature" => term => DBSource.Open(ctx.Connection(), ctx.Lookup()).SearchAsync(SmartParamKind.Creature, term, CancellationToken.None),
        _ => null
    };

    static async Task<IReadOnlyList<(uint Id, string Name)>> SearchGameObjects(GameObjectContext ctx, string term) =>
        (await DBSource.Open(ctx.Connection(), ctx.Lookup()).SearchTemplatesAsync(SpawnKind.GameObject, term, null, CancellationToken.None))
            .Select(h => (h.Entry, h.Name)).ToList();

    static async Task<IReadOnlyList<(uint Id, string Name)>> SearchQuests(GameObjectContext ctx, string term) =>
        (await DBSource.Open(ctx.Connection(), ctx.Lookup()).SearchQuestsAsync(term, CancellationToken.None))
            .Select(q => (q.Id, q.Title)).ToList();
}
