using Godot;

namespace ACWorldEdit;

/// <summary>
/// How many spawns use this template and a bounded sample of them. The page is read-only: a
/// spawn's own respawn, phase, rotation and pose are edited in the inspector beside the world.
/// </summary>
public sealed partial class GameObjectUsedBySection(GameObjectContext ctx) : GameObjectSection(ctx)
{
    public override string Id => "usedby";
    public override string Title => "Used by";
    public override string IconPath => "Interface/Icons/INV_Misc_Map_01.blp";
    public override string Tip => "The spawns that use this template; select one in the world to edit its own respawn, phase, rotation and pose.";
    public override string Badge => Ctx.SpawnCount > 0 ? Ctx.SpawnCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    public override void Refresh()
    {
        Clear();
        if (Ctx.Snapshot is not { } snapshot)
        {
            AddChild(Placeholder());
            return;
        }

        AddChild(ClientStyle.Text($"Used by {snapshot.SpawnCount} spawn{(snapshot.SpawnCount == 1 ? "" : "s")}.", 16, ClientStyle.Header));
        AddChild(ClientStyle.Text("Respawn, phase, rotation and pose are per spawn; select a spawn in the world to edit them.", 12, ClientStyle.Faded));
        if (snapshot.SpawnCount == 0)
        {
            AddChild(ClientStyle.Text("No spawns use this template yet.", 13, ClientStyle.Faded));
            return;
        }

        AddChild(ClientStyle.Text("Sample", 14, ClientStyle.Header));
        foreach (var spawn in snapshot.Spawns)
        {
            AddChild(ClientStyle.Text(
                $"GUID {spawn.Guid} · map {spawn.Map} · ({spawn.X:0.##}, {spawn.Y:0.##}, {spawn.Z:0.##})", 13));
        }

        if (snapshot.SpawnCount > snapshot.Spawns.Count)
        {
            AddChild(ClientStyle.Text($"First {snapshot.Spawns.Count} shown; the rest are in the gameobject table.", 11, ClientStyle.Faded));
        }
    }
}
