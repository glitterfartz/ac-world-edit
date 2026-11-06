using Godot;

namespace ACWorldEdit;

/// <summary>
/// Every spawn of this GameObject template as a paginated table with a per-row Fly to button.
/// Read-only: a spawn's own respawn, phase, rotation and pose are edited in the inspector beside
/// the world.
/// </summary>
public sealed partial class GameObjectSpawnsSection(GameObjectContext ctx) : GameObjectSection(ctx)
{
    public override string Id => "spawns";
    public override string Title => "Spawns";
    public override string IconPath => "Interface/Icons/INV_Misc_Map_01.blp";
    public override string Tip => "Every spawn of this GameObject template; fly to one to edit its respawn, phase, rotation and pose in the world.";
    public override string Badge => Ctx.SpawnCount > 0 ? Ctx.SpawnCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Template != null;

    int page;
    uint forEntry;

    public override void OnShown(object? argument) => page = 0;

    public override void Refresh()
    {
        Clear();
        if (Ctx.Snapshot is not { } snapshot || Ctx.Entry is not uint entry)
        {
            AddChild(Placeholder());
            return;
        }

        if (forEntry != entry)
        {
            forEntry = entry;
            page = 0;
        }

        AddChild(ClientStyle.Text("Respawn, phase, rotation and pose are per spawn; fly to a spawn to edit them in the world.", 12, ClientStyle.Faded));
        SpawnsTable.Render(this, snapshot.Spawns.Select(s => new SpawnListRow(s.Guid, s.Map, s.X, s.Y, s.Z)).ToList(),
            snapshot.SpawnCount, page, next => { page = next; Ctx.Redraw(); }, Fly, Report);
    }

    async Task<string> Fly(SpawnListRow row) => await Ctx.FlyToSpawn(row.Guid, row.Map, row.X, row.Y, row.Z);

    void Report(string message)
    {
        Ctx.Status = message;
        Ctx.Redraw();
    }
}
