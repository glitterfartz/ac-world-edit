namespace ACWorldEdit.Foundation;

// Editing a spawn's own appearance (its addon row) and falling back to the template's.
public sealed partial class SpawnPoses
{
    /// <summary>
    /// Change a spawn's appearance (its addon row) as one undoable command. A spawn's own addon row replaces
    /// the template's completely, so the row should exist only while it differs from <paramref name="baseline"/>,
    /// what the spawn shows without one (the template's addon, or nothing). The change starts from the spawn's
    /// row, or from the baseline when it has none, and the result picks exactly one edit:
    /// no row and nothing different records nothing; no row and something different inserts one;
    /// a row that is now the same as the baseline is deleted; a row that is still different is updated.
    /// </summary>
    public AddonEdit EditAddon(object? sender, string source, ulong guid, string label, Row baseline, Func<Row, Row> change)
    {
        if (drafts.Draft == null)
        {
            return new(false, drafts.Error ?? "Editing is disabled.");
        }

        var table = AddonTable(source);
        var existing = AddonOf(source, guid);
        try
        {
            var after = change(existing ?? baseline);
            bool same = SpawnEdits.SameAddon(table, after, baseline);
            RowEdit? edit = existing == null
                ? same ? null : InteractionEdits.Insert(table, after)
                : same ? InteractionEdits.Delete(table, existing) : after == existing ? null : InteractionEdits.Update(table, existing, after);
            if (edit == null)
            {
                return new(true, "");
            }

            var (ok, message) = Commit(sender, source, label, [edit]);
            return new(ok, message, ok && edit.After == null);
        }
        catch (InvalidDataException error)
        {
            return new(false, "Not saved: " + error.Message);
        }
    }

    /// <summary>
    /// Make the spawn show its template's appearance again: set every appearance column to the template's and
    /// let <see cref="EditAddon"/> decide whether the row is still needed. A row that holds something the
    /// appearance fields do not (a waypoint path, an animation kit) stays, and the message says what it holds.
    /// </summary>
    public AddonEdit UseTemplateAppearance(object? sender, IWorldSchema schema, WorldDialect dialect, string source, ulong guid, Row? templateAddon)
    {
        if (AddonOf(source, guid) is not { } existing)
        {
            return new(true, "");
        }

        var baseline = SpawnEdits.AddonBaseline(schema, kind, templateAddon, guid);
        var result = EditAddon(sender, source, guid, "Use the template's appearance", baseline, row => SpawnFields.CopyAppearance(dialect, kind, row, baseline));
        if (!result.Ok || result.Removed || AddonOf(source, guid) is not { } kept)
        {
            return result;
        }

        var table = AddonTable(source);
        var other = table.Columns.FirstOrDefault(c => !table.Key.Contains(c.Name) && !SpawnFields.AddonFor(dialect, kind).Any(f => f.Column == c.Name) && kept[c.Name] != baseline[c.Name]);
        string what = other.Name is "PathId" or "path_id" ? $"waypoint path {kept[other.Name]}" : other.Name != null ? $"its own {other.Name}" : "other settings";
        return result with { Message = $"Kept its own row: it holds {what}." };
    }

    /// <summary>
    /// Give a spawn its own appearance, starting as a copy of its template's (a spawn with its own addon row
    /// ignores the template's, so starting blank would hide the template's mount, auras and emote). Does
    /// nothing when the spawn already has its own.
    /// </summary>
    public (bool Ok, string Message) OverrideAddon(object? sender, IWorldSchema schema, string source, ulong guid, Row? templateAddon)
    {
        if (AddonOf(source, guid) != null)
        {
            return (true, "");
        }

        var copy = templateAddon != null ? SpawnEdits.AddonFromTemplate(schema, templateAddon, guid) : SpawnEdits.NewAddon(schema, kind, guid);
        return Commit(sender, source, "Give this spawn its own appearance", [InteractionEdits.Insert(AddonTable(source), copy)]);
    }

    /// <summary>Remove the spawn's own appearance, so it shows its template's again. Does nothing when it has none.</summary>
    public (bool Ok, string Message) RemoveAddon(object? sender, string source, ulong guid) =>
        AddonOf(source, guid) is { } row ? Commit(sender, source, "Use the template's appearance", [InteractionEdits.Delete(AddonTable(source), row)]) : (true, "");
}

/// <summary>The outcome of an appearance edit; <see cref="Removed"/> says the spawn's own row is gone.</summary>
public sealed record AddonEdit(bool Ok, string Message, bool Removed = false);
