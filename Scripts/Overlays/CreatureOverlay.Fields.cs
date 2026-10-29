using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The selected creature's spawn fields: respawn, movement, visibility, the overrides of its template and its
/// appearance. A spawn's own addon row replaces its template's completely, so the appearance fields start from
/// the template's values, wait until those are read, and the row exists only while it differs from them.
/// </summary>
public partial class CreatureOverlay
{
    SpawnInspector spawnFields = null!;
    readonly Dictionary<uint, IReadOnlyDictionary<string, string>> templateValues = [];
    readonly HashSet<uint> templateRequests = [];
    readonly HashSet<uint> templateAddonRequests = [];
    TemplateAddons? templateAddons;

    void BuildSpawnFields(Container card)
    {
        spawnFields = new SpawnInspector();
        spawnFields.Edited += EditSpawnField;
        spawnFields.UseTemplateRequested += UseTemplateAppearance;
        card.AddChild(spawnFields);
    }

    /// <summary>Show the selected creature's row (the draft applied), or hide the fields when there is none yet.</summary>
    void RefreshSpawnFields(bool force = false)
    {
        if (spawnFields == null)
        {
            return;
        }

        if (!selectedGuid.HasValue || poses == null || !poses.Available || group.Members.Count > 0 || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            spawnFields.Show(WorldDialect.AzerothCore, SpawnKind.Creature, null, null, null, null, false);
            return;
        }

        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        uint entry = marker.Spawn.TemplateId;
        ulong guid = selectedGuid.Value;
        var (baseline, state) = AppearanceBaseline(dialect, entry, guid);
        var own = poses.AddonOf(sourceKey, guid);
        spawnFields.Show(dialect, SpawnKind.Creature, poses.RowOf(sourceKey, guid), own ?? baseline,
            state == SpawnInspector.AppearanceState.Loading ? state : own != null ? SpawnInspector.AppearanceState.Own : SpawnInspector.AppearanceState.Inherited,
            templateValues.GetValueOrDefault(entry), true, force);
        LoadTemplateValues(entry, dialect);
        LoadTemplateAddon(entry);
    }

    /// <summary>What the spawn shows without an addon row of its own, and whether the template's addon has been read yet.</summary>
    (Row Baseline, SpawnInspector.AppearanceState State) AppearanceBaseline(WorldDialect dialect, uint entry, ulong guid)
    {
        bool known = templateAddons?.IsLoaded(sourceKey, entry) == true;
        var template = known ? templateAddons!.RowOf(sourceKey, entry) : null;
        return (SpawnEdits.AddonBaseline(WorldSchemas.For(dialect), SpawnKind.Creature, template, guid), known ? SpawnInspector.AppearanceState.Inherited : SpawnInspector.AppearanceState.Loading);
    }

    /// <summary>Read what the template uses for the columns a spawn can override, once per template.</summary>
    async void LoadTemplateValues(uint entry, WorldDialect dialect)
    {
        if (templateValues.ContainsKey(entry) || !templateRequests.Add(entry))
        {
            return;
        }

        try
        {
            templateValues[entry] = await DBSource.Open(Connection()).ReadTemplateColumnsAsync(SpawnKind.Creature, entry, SpawnFields.TemplateColumns(dialect, SpawnKind.Creature), default);
        }
        catch (Exception)
        {
            templateValues[entry] = new Dictionary<string, string>();
        }

        if (IsInsideTree())
        {
            RefreshSpawnFields();
        }
    }

    /// <summary>
    /// Read the template's addon row (or learn it has none) so the spawn's appearance has a baseline to start from.
    /// Until it arrives the appearance fields are read-only: starting from a blank row would drop the template's values.
    /// </summary>
    async void LoadTemplateAddon(uint entry)
    {
        string key = sourceKey;
        if (templateAddons == null || templateAddons.IsLoaded(key, entry) || !templateAddonRequests.Add(entry))
        {
            return;
        }

        try
        {
            templateAddons.Load(key, entry, await DBSource.Open(Connection()).ReadTemplateAddonAsync(entry, default));
        }
        catch (Exception)
        {
            templateAddonRequests.Remove(entry);
            Say("Could not read the template's appearance; this spawn's appearance stays read-only for now.");
            return;
        }

        if (IsInsideTree())
        {
            RefreshSpawnFields();
        }
    }

    /// <summary>Apply a typed value as one undoable command; a refused value says why and the field shows the row again.</summary>
    void EditSpawnField(SpawnField field, string text)
    {
        if (poses == null || !selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            return;
        }

        ulong guid = selectedGuid.Value;
        string error = "";
        Func<Row, Row> change = row =>
        {
            var (changed, problem) = SpawnFields.Apply(field, row, text);
            error = problem;
            return changed ?? row;
        };
        string label = "Set " + field.Label.ToLowerInvariant();
        string said;
        if (field.Addon)
        {
            var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
            var (baseline, state) = AppearanceBaseline(dialect, marker.Spawn.TemplateId, guid);
            var result = state == SpawnInspector.AppearanceState.Loading
                ? new AddonEdit(false, "The template's appearance is still loading; try again.")
                : poses.EditAddon(this, sourceKey, guid, label, baseline, change);
            said = !result.Ok ? result.Message : result.Removed ? "Uses the template's appearance again." : "Saved.";
        }
        else
        {
            var (ok, message) = poses.Edit(this, sourceKey, guid, label, change);
            said = ok ? "Saved." : message;
        }

        Say(error.Length > 0 ? error : said);
        RefreshSpawnFields(true);
    }

    /// <summary>
    /// Make the spawn follow its template's appearance again. The spawn's own row goes away when nothing else
    /// in it differs from the template's; a row that holds a waypoint path or an animation kit stays, and the
    /// status says what it holds.
    /// </summary>
    void UseTemplateAppearance()
    {
        if (poses == null || !selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker) || templateAddons?.IsLoaded(sourceKey, marker.Spawn.TemplateId) != true)
        {
            return;
        }

        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        var result = poses.UseTemplateAppearance(this, WorldSchemas.For(dialect), dialect, sourceKey, selectedGuid.Value, templateAddons.RowOf(sourceKey, marker.Spawn.TemplateId));
        Say(!result.Ok ? result.Message : result.Removed ? "Uses the template's appearance again." : result.Message.Length > 0 ? result.Message : "Saved.");
        RefreshSpawnFields(true);
    }
}
