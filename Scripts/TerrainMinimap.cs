using Godot;
using ACWorldEdit.Assets;
namespace ACWorldEdit;
public partial class TerrainMinimap : Control
{
    public WorldMap? World;
    public HashSet<TileKey> Loaded = [];
    public TileKey Current;
    public Vector3 CameraPosition;
    public event Action<TileKey>? Jump;
    const float Cell = 2.5f;
    public override void _Draw()
    {
        DrawStyleBox(new StyleBoxFlat { BgColor = new Color(0.04f, 0.07f, 0.1f, 0.9f), CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 }, new Rect2(0, 0, 180, 180));
        if (World == null) return;
        foreach (var key in World.Tiles) DrawRect(new Rect2(10 + key.X * Cell, 10 + key.Y * Cell, Cell - 0.3f, Cell - 0.3f), Loaded.Contains(key) ? new Color("5bd8c2") : new Color("3e586b"));
        Vector2 pos = new(10 + (CameraPosition.X / TerrainTile.Size + 32) * Cell, 10 + (CameraPosition.Z / TerrainTile.Size + 32) * Cell);
        DrawCircle(pos, 3, new Color("ffffff"));
        DrawRect(new Rect2(10 + Current.X * Cell - 1, 10 + Current.Y * Cell - 1, Cell + 2, Cell + 2), Colors.White, false, 1);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse)
        { var key = new TileKey((int)Math.Floor((mouse.Position.X - 10) / Cell), (int)Math.Floor((mouse.Position.Y - 10) / Cell)); if (World?.Tiles.Contains(key) == true) Jump?.Invoke(key); }
    }
}
