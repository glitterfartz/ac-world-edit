using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The selected GameObject's spawn fields: respawn, state, visibility and its script.</summary>
public partial class GameObjectOverlay
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

    /// <summary>Show the selected GameObject's row (the draft applied), or hide the fields when there is none yet.</summary>
    void RefreshSpawnFields(bool force = false)
    {
        if (spawnFields == null)
        {
            return;
        }

        if (!selected.HasValue || poses == null || !poses.Available || !lookup.TryGetValue(selected.Value, out var spawn))
        {
            spawnFields.Show(WorldDialect.AzerothCore, SpawnKind.GameObject, null, null, false);
            return;
        }

        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        uint entry = spawn.TemplateId;
        spawnFields.Show(dialect, SpawnKind.GameObject, poses.RowOf(sourceKey, selected.Value), templateValues.GetValueOrDefault(entry), true, force);
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
            templateValues[entry] = await DBSource.Open(Connection()).ReadTemplateColumnsAsync(SpawnKind.GameObject, entry, SpawnFields.TemplateColumns(dialect, SpawnKind.GameObject), default);
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
        if (poses == null || !selected.HasValue)
        {
            return;
        }

        ulong guid = selected.Value;
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
