using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The selected creature's spawn fields: respawn, movement, visibility and the overrides of its template.</summary>
public partial class CreatureOverlay
{
    SpawnInspector spawnFields = null!;
    readonly Dictionary<uint, IReadOnlyDictionary<string, string>> templateValues = [];
    readonly HashSet<uint> templateRequests = [];

    void BuildSpawnFields(Container card)
    {
        spawnFields = new SpawnInspector();
        spawnFields.Edited += EditSpawnField;
        card.AddChild(spawnFields);
    }

    /// <summary>Show the selected creature's row (the draft applied), or hide the fields when there is none yet.</summary>
    void RefreshSpawnFields(bool force = false)
    {
        if (spawnFields == null)
        {
            return;
        }

        if (!selectedGuid.HasValue || poses == null || !poses.Available || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            spawnFields.Show(WorldDialect.AzerothCore, SpawnKind.Creature, null, null, false);
            return;
        }

        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        uint entry = marker.Spawn.TemplateId;
        spawnFields.Show(dialect, SpawnKind.Creature, poses.RowOf(sourceKey, selectedGuid.Value), templateValues.GetValueOrDefault(entry), true, force);
        LoadTemplateValues(entry, dialect);
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

    /// <summary>Apply a typed value as one undoable command; a refused value says why and the field shows the row again.</summary>
    void EditSpawnField(SpawnField field, string text)
    {
        if (poses == null || !selectedGuid.HasValue)
        {
            return;
        }

        ulong guid = selectedGuid.Value;
        string error = "";
        var (ok, message) = poses.Edit(this, sourceKey, guid, "Set " + field.Label.ToLowerInvariant(), row =>
        {
            var (changed, problem) = SpawnFields.Apply(field, row, text);
            error = problem;
            return changed ?? row;
        });
        Say(error.Length > 0 ? error : ok ? "Saved." : message);
        RefreshSpawnFields(true);
    }
}
