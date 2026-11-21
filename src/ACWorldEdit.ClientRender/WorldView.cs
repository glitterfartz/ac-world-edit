using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.ClientData;

namespace ACWorldEdit;

/// <summary>
/// The world scene for one opened client: terrain rendering and streaming, models, placed scenery,
/// sky/lighting and the sun-shadow pass. A game client constructs one and calls <see cref="Update"/>
/// each frame; the editor keeps its own wiring order and only uses the constructed systems.
/// <para>Add <see cref="Root"/> to a node already in the scene tree, and give it a camera that is in
/// the tree too (the cascade pass reads the camera's world).</para>
/// </summary>
public sealed class WorldView : IDisposable
{
    bool disposed;

    public WorldView(Node3D parent, Camera3D camera, ITileRegionSink? regions = null, ShadowQuality shadows = ShadowQuality.Off,
        Func<bool>? cameraFocused = null, Action<string>? setMessage = null,
        Action<TileKey>? tileScheduled = null, Action<TileKey>? tilePublished = null)
    {
        Camera = camera;
        Root = new Node3D { Name = "World" };
        parent.AddChild(Root);

        // Placements hang under their own node so the world scene is not the caller's direct child.
        var objectRoot = new Node3D { Name = "Objects" };
        Root.AddChild(objectRoot);

        Renderer = new TerrainRenderer();
        Models = new ModelLibrary();
        Objects = new WorldObjectScene(objectRoot, Models);
        Sky = new SkyLighting(Models) { LightPlacements = () => Objects.Placements() };
        Root.AddChild(Sky.Root);

        // The cascade pass owns its viewports; both scenery owners register casters there.
        Casters = new ShadowCasterScene();
        Renderer.Shadows = Casters;
        Models.Shadows = Casters;
        Shadows = new SunShadows(Casters, Root.GetWorld3D());
        Root.AddChild(Shadows.Root);
        Shadows.SetQuality(shadows);

        // Proxies live on their own layers; the normal view must never draw one.
        camera.CullMask = ShadowCasterScene.MainCameraMask;
        Streamer = new TerrainStreamer(Root, Renderer, Objects, camera, regions, cameraFocused, setMessage, tileScheduled, tilePublished);
    }

    public Node3D Root { get; }
    public Camera3D Camera { get; }
    public TerrainRenderer Renderer { get; }
    public ModelLibrary Models { get; }
    public WorldObjectScene Objects { get; }
    public SkyLighting Sky { get; }
    public ShadowCasterScene Casters { get; }
    public SunShadows Shadows { get; }
    public TerrainStreamer Streamer { get; }
    public IClientFiles? Files { get; private set; }
    public WorldMap? Map { get; private set; }

    /// <summary>Construct a world and open the client's models and catalogs on it.</summary>
    public static async Task<WorldView> CreateAsync(Node3D parent, Camera3D camera, ClientServices client,
        ITileRegionSink? regions = null, ShadowQuality shadows = ShadowQuality.Off)
    {
        var view = new WorldView(parent, camera, regions, shadows);
        await view.SetClientAsync(client);
        return view;
    }

    /// <summary>Adopt a client's files and catalogs, or release them with <c>null</c>.</summary>
    public async Task SetClientAsync(ClientServices? client)
    {
        Files = client?.Files;
        Renderer.WrathLighting = client?.Files.Kind == ClientKind.Wrath335;
        Sky.Retail = client?.Files.Kind == ClientKind.Retail;
        await Models.SetStore(client?.Files, client?.Creatures, client?.GameObjects);
    }

    /// <summary>Start streaming a map (the WDT's global placements are adopted here).</summary>
    public void OpenMap(WorldMap map)
    {
        Map = map;
        Streamer.CurrentWorld = map;
        Objects.SetGlobals(map.GlobalObjects);
    }

    public void SetTimeOfDay(double hours) => Sky.SetTimeOfDay(hours);

    /// <summary>
    /// The shell's proven per-frame order: stream, bind models, publish the sky, then draw the
    /// cascades after the sky's sun values and the new caster registrations.
    /// </summary>
    public void Update(double delta)
    {
        if (Map != null && Files != null)
        {
            Streamer.UpdateTerrainStreaming(Map, Files);
        }

        Models.Update(Camera);
        Sky.Update(Camera, delta);
        Shadows.Update(Camera, Sky);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Streamer.Shutdown();
        Shadows.Dispose();
        _ = Models.Stop();
        Root.QueueFree();
    }
}
