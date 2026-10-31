using System.Security.Cryptography;
using System.Text;
using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Renders model thumbnails off-screen and caches them as PNGs. Each request attaches the model through
/// the shared <see cref="ModelLibrary"/> inside a <see cref="SubViewport"/> with its own world, light and
/// transparent background, frames the camera on the model's bounds once it is built, renders one frame
/// and saves it as <c>{Directory}/{sha1 of the model key}.png</c>. The key covers the model and its
/// textures, so a display that hotfixes change gets a new file instead of a stale one.
///
/// The library decodes the nearest unbuilt models first, measured from the main camera; the off-screen
/// world is separate, so its root follows the main camera and thumbnails decode ahead of the world.
/// A few models are attached at once and captured one by one. Headless runs (no renderer) still load
/// the models but never call back, since there is no image to read.
/// </summary>
public sealed partial class ModelThumbnails : Node, IThumbnailSource
{
    public const int Size = 128;
    const int Parallel = 3;
    const double Timeout = 30;
    const float FieldOfView = 30;
    /// <summary>Part of every file name; bump it when framing or lighting changes so old PNGs are not reused.</summary>
    const int Version = 1;

    /// <summary>The model library the viewer draws with; null until a client is loaded.</summary>
    public Func<ModelLibrary?> Models { get; set; } = () => null;
    public Func<Camera3D?> MainCamera { get; set; } = () => null;

    /// <summary>The cache folder (an absolute path) for the loaded client profile.</summary>
    public string Directory { get; set; } = "";

    sealed class Job(string hash, ModelKey key)
    {
        public readonly string Hash = hash;
        public readonly ModelKey Key = key;
        public readonly List<Action<Texture2D>> Ready = [];
        public Node3D? Holder;
        public ModelInstance? Instance;
        public double Age;
    }

    readonly Dictionary<string, Texture2D?> known = [];
    readonly Dictionary<uint, ModelKey?> creatureKeys = [];
    readonly Dictionary<uint, ModelKey?> objectKeys = [];
    readonly List<Job> waiting = [];
    readonly List<Job> active = [];
    (Job Job, int Frames)? capture;
    SubViewport viewport = null!;
    Node3D root = null!;
    Camera3D camera = null!;
    DirectionalLight3D light = null!;
    readonly bool headless = DisplayServer.GetName() == "headless";

    /// <summary>Thumbnails rendered and saved since startup, for the smoke check.</summary>
    public int Rendered { get; private set; }

    public override void _Ready()
    {
        viewport = new SubViewport
        {
            Size = new Vector2I(Size, Size),
            OwnWorld3D = true,
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.75f, 0.75f, 0.8f),
                AmbientLightEnergy = 0.7f
            }
        });
        root = new Node3D { Name = "Thumbnails" };
        viewport.AddChild(root);
        camera = new Camera3D { Fov = FieldOfView, Current = true };
        root.AddChild(camera);
        light = new DirectionalLight3D { LightEnergy = 1.1f };
        root.AddChild(light);
    }

    public Texture2D? Creature(uint displayId, Action<Texture2D> ready)
    {
        var key = KeyOf(creatureKeys, displayId, () => Models()?.Catalog is { } catalog ? ModelKey.ForCreature(catalog.Resolve(displayId)) : null);
        return key == null ? null : Request(key, ready);
    }

    public Texture2D? GameObject(uint displayId, Action<Texture2D> ready)
    {
        // World models (buildings, transports) are slow to decode and frame badly at this size.
        var key = KeyOf(objectKeys, displayId, () => Models()?.GameObjects?.Resolve(displayId) is { } path && !path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase) ? new ModelKey(path) : null);
        return key == null ? null : Request(key, ready);
    }

    public void CancelWaiting() => waiting.Clear();

    /// <summary>Forget everything about the previous client: its keys, textures and attached models.</summary>
    public void Reset(string directory)
    {
        Directory = directory;
        waiting.Clear();
        foreach (var job in active)
        {
            job.Holder?.QueueFree();
        }

        active.Clear();
        capture = null;
        known.Clear();
        creatureKeys.Clear();
        objectKeys.Clear();
    }

    static ModelKey? KeyOf(Dictionary<uint, ModelKey?> keys, uint displayId, Func<ModelKey?> resolve)
    {
        if (displayId == 0)
        {
            return null;
        }

        if (!keys.TryGetValue(displayId, out var key))
        {
            try
            {
                key = resolve();
            }
            catch (Exception)
            {
                key = null;
            }

            keys[displayId] = key;
        }

        return key;
    }

    Texture2D? Request(ModelKey key, Action<Texture2D> ready)
    {
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{Version}|{key}"))).ToLowerInvariant();
        if (known.TryGetValue(hash, out var texture))
        {
            return texture;
        }

        string file = FileOf(hash);
        if (file != "" && File.Exists(file) && Image.LoadFromFile(file) is { } image && !image.IsEmpty())
        {
            return known[hash] = ImageTexture.CreateFromImage(image);
        }

        var job = active.Concat(waiting).FirstOrDefault(j => j.Hash == hash);
        if (job == null)
        {
            job = new Job(hash, key);
            waiting.Add(job);
        }

        job.Ready.Add(ready);
        return null;
    }

    string FileOf(string hash) => Directory == "" ? "" : Path.Combine(Directory, hash + ".png");

    public override void _Process(double delta)
    {
        var models = Models();
        if (models == null || (waiting.Count == 0 && active.Count == 0))
        {
            return;
        }

        if (MainCamera() is { } main)
        {
            root.GlobalPosition = main.GlobalPosition;
        }

        if (capture is { } pending)
        {
            if (pending.Frames > 0)
            {
                capture = pending with { Frames = pending.Frames - 1 };
                return;
            }

            Finish(pending.Job, viewport.GetTexture().GetImage());
            capture = null;
        }

        while (active.Count < Parallel && waiting.Count > 0)
        {
            var job = waiting[0];
            waiting.RemoveAt(0);
            job.Holder = new Node3D { Visible = false };
            root.AddChild(job.Holder);
            job.Instance = models.Attach(job.Holder, job.Key);
            active.Add(job);
        }

        foreach (var job in active.ToArray())
        {
            job.Age += delta;
            if (job.Instance == null || !IsInstanceValid(job.Instance) || models.HasFailed(job.Key) || job.Age > Timeout)
            {
                Finish(job, null);
            }
            else if (job.Instance.ModelReady && capture == null)
            {
                Frame(job);
            }
        }
    }

    /// <summary>Show only this job's model, aim the camera at its bounds from the front-right and above, and render one frame.</summary>
    void Frame(Job job)
    {
        var bounds = job.Instance!.Bounds;
        if (headless || bounds.Size.LengthSquared() < 1e-6f)
        {
            Finish(job, null);
            return;
        }

        foreach (var other in active)
        {
            other.Holder!.Visible = other == job;
        }

        // Fit the taller of the height and the footprint's diagonal; the full 3D diagonal leaves the model small.
        var center = bounds.GetCenter();
        float radius = Math.Max(bounds.Size.Y, new Vector2(bounds.Size.X, bounds.Size.Z).Length()) / 2;
        float distance = radius / Mathf.Tan(Mathf.DegToRad(FieldOfView / 2)) * 1.1f;
        // Models face -Z (Godot's forward) once loaded; look from in front, a little to their left and above.
        var direction = new Vector3(0.6f, 0.35f, -1).Normalized();
        camera.Near = Math.Max(0.01f, distance - radius * 2);
        camera.Far = distance + radius * 2;
        camera.LookAtFromPosition(root.GlobalPosition + center + direction * distance, root.GlobalPosition + center, Vector3.Up);
        light.LookAtFromPosition(root.GlobalPosition + center + new Vector3(0.3f, 1.5f, -1), root.GlobalPosition + center, Vector3.Up);
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        // The viewport texture holds the previously drawn frame, so read it a couple of frames later.
        capture = (job, 2);
    }

    void Finish(Job job, Image? image)
    {
        active.Remove(job);
        job.Holder?.QueueFree();
        if (image == null || image.IsEmpty())
        {
            known[job.Hash] = null;
            return;
        }

        string file = FileOf(job.Hash);
        if (file != "")
        {
            System.IO.Directory.CreateDirectory(Directory);
            image.SavePng(file);
        }

        var texture = ImageTexture.CreateFromImage(image);
        known[job.Hash] = texture;
        Rendered++;
        foreach (var ready in job.Ready)
        {
            ready(texture);
        }
    }
}
