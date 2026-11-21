using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.ClientData;

namespace ACWorldEdit;

/// <summary>
/// The world scene for one opened client: terrain rendering and streaming, models, placed scenery,
/// sky/lighting and the sun-shadow pass. A game client constructs one and calls <see cref="Update"/>
/// each frame; the editor keeps its own wiring order and only uses the constructed systems.
/// <para>Build it under a node that is already in the scene tree (the cascade pass takes that node's
/// <c>World3D</c>) and give it a camera that is in the tree too. <see cref="DisposeAsync"/> waits for
/// the streaming and model workers, so close the client files only after it returns.</para>
/// </summary>
public sealed class WorldView : IDisposable, IAsyncDisposable
{
    /// <summary>The client's star dome model, drawn like a LightSkybox when the client has one.</summary>
    const string StarsModelPath = "Environments/Stars/stars.m2";

    bool disposed;
    Task? release;
    ClientServices? client;

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

    /// <summary>The map's DBC/DB2 id, or null when it could not be resolved from the client catalog.</summary>
    public uint? MapId { get; private set; }

    /// <summary>Construct a world and open the client's models and catalogs on it.</summary>
    public static async Task<WorldView> CreateAsync(Node3D parent, Camera3D camera, ClientServices client,
        ITileRegionSink? regions = null, ShadowQuality shadows = ShadowQuality.Off)
    {
        var view = new WorldView(parent, camera, regions, shadows);
        await view.SetClientAsync(client);
        return view;
    }

    /// <summary>
    /// Adopt a client's files, catalogs, celestial textures and star dome, or release them with
    /// <c>null</c>. Changes the world, so open a map again afterwards.
    /// </summary>
    public async Task SetClientAsync(ClientServices? client)
    {
        ReplaceWorld();
        this.client = client;
        Files = client?.Files;
        Renderer.WrathLighting = client?.Files.Kind == ClientKind.Wrath335;
        Sky.Retail = client?.Files.Kind == ClientKind.Retail;
        Sky.SetCelestialTextures(
            Celestial(client, "Textures/sunCenter.blp"),
            Celestial(client, "Textures/moon.blp"),
            Celestial(client, "Textures/moon02.blp"),
            Celestial(client, "Textures/sunGlare.blp"),
            Celestial(client, "Textures/moonGlare.blp"));
        await Models.SetStore(client?.Files, client?.Creatures, client?.GameObjects);
        Sky.SetStarsModel(client?.Files is { } files && files.Exists(StarsModelPath) ? StarsModelPath : null);
    }

    static Texture2D? Celestial(ClientServices? client, string path) =>
        client?.Files is { } files ? CelestialTextures.Load(files, path) : null;

    /// <summary>
    /// Stream a map, replacing whatever was open: the old window is released and its reads are
    /// invalidated first, because both continents share the same tile grid. The map id selects the
    /// lighting table's regions; it is resolved from the client's map catalog when omitted.
    /// </summary>
    public void OpenMap(WorldMap map, uint? mapId = null)
    {
        ReplaceWorld();
        Map = map;
        MapId = mapId ?? (client?.Maps.FirstOrDefault(m => m.Directory == map.Name) is { } info ? (uint)info.Id : null);
        Streamer.CurrentWorld = map;
        Objects.SetGlobals(map.GlobalObjects);
        Sky.SetMap(client?.Lighting, MapId, map.Tiles.Count == 0);
        // Retail carries placed fog volumes in a separate file beside the WDT. Reading it here is a
        // map-switch stall (one small file); classic maps have none.
        Sky.SetFogVolumes(client is { Files.Kind: ClientKind.Retail } retail
            ? FogVolumes.ApplyConditions(FogVolumes.Load(retail.Files, map.Name), retail.Lighting?.FogVolumePresence)
            : []);
    }

    /// <summary>Drop the streaming window, placements and lighting before adopting another world.</summary>
    void ReplaceWorld()
    {
        Map = null;
        MapId = null;
        Streamer.Reset();
        Objects.Clear();
        Sky.SetMap(null, null, false);
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

    /// <summary>
    /// Cancel streaming and free the scene now; the worker reads finish in the background. Prefer
    /// <see cref="DisposeAsync"/> when the client files are about to close.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ReplaceWorld();
        this.client = null;
        Files = null;
        Sky.SetStarsModel(null);
        Sky.SetCelestialTextures(null, null, null, null, null);
        Streamer.Shutdown();
        // Proxies outlive the sky and shadows; stop models from unregistering into a disposed pass.
        Models.Shadows = null;
        Renderer.Shadows = null;
        Shadows.Dispose();
        Root.QueueFree();
        release = ReleaseAsync();
    }

    /// <summary>
    /// Dispose and wait for the streaming and model workers, then release the decoded model and
    /// terrain caches. Await this before disposing the client's files.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(release!);
    }

    async Task ReleaseAsync()
    {
        await Streamer.WaitForPendingAsync();
        await Models.Stop();
        await Models.SetStore(null);
    }
}
