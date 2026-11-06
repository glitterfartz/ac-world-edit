using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Every spawn of this creature as a paginated table with a per-row Fly to button. Read-only:
/// a spawn's own respawn, movement and visibility are edited in the inspector beside the world.
/// </summary>
public sealed partial class SpawnsSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override string Id => "spawns";
    public override string Title => "Spawns";
    public override bool Supports(WorldDialect dialect) => true;
    public override string IconPath => "Interface/Icons/INV_Misc_Map_01.blp";
    public override string Tip => "Every spawn of this creature; fly to one to edit its respawn, movement and visibility in the world.";
    public override string Badge => Ctx.Snapshot is { SpawnCount: > 0 } snapshot ? snapshot.SpawnCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Snapshot != null;

    IReadOnlyList<SpawnListRow>? spawns;
    int page;
    uint forEntry;
    readonly HashSet<uint> requested = [];

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        spawns = null;
        page = 0;
        requested.Clear();
    }

    public override void OnShown(object? argument) => page = 0;

    public override void Refresh()
    {
        Clear();
        if (Ctx.Snapshot is not { } snapshot || Ctx.Target is null)
        {
            AddChild(Placeholder());
            return;
        }

        if (forEntry != snapshot.Entry)
        {
            forEntry = snapshot.Entry;
            spawns = null;
            page = 0;
        }

        LoadSpawns(snapshot.Entry);
        if (spawns == null)
        {
            AddChild(ClientStyle.Text("Loading spawns…", 15, ClientStyle.Faded));
            return;
        }

        AddChild(ClientStyle.Text("Respawn, movement and visibility are per spawn; fly to a spawn to edit them in the world.", 12, ClientStyle.Faded));
        SpawnsTable.Render(this, spawns, snapshot.SpawnCount, page, next => { page = next; Ctx.Redraw(); }, Fly, Report);
    }

    async void LoadSpawns(uint entry)
    {
        if (spawns != null || !requested.Add(entry))
        {
            return;
        }

        try
        {
            var found = await DBSource.Open(Ctx.Connection(), Ctx.Lookup()).FindSpawnsAsync(entry, CancellationToken.None);
            if (forEntry == entry)
            {
                spawns = found.Select(s => new SpawnListRow(s.Guid, s.Map, s.X, s.Y, s.Z)).ToList();
            }
        }
        catch (Exception error)
        {
            requested.Remove(entry);
            if (forEntry == entry)
            {
                Report(DbErrors.Safe(error));
                return;
            }
        }

        if (forEntry == entry)
        {
            Ctx.Redraw();
        }
    }

    async Task<string> Fly(SpawnListRow row) => await Ctx.FlyToSpawn(row.Guid, row.Map, row.X, row.Y, row.Z);

    void Report(string message)
    {
        Ctx.Status = message;
        Ctx.Redraw();
    }
}
