using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The GameObject template window: a section picker on the left, the chosen section on the
/// right. It edits `gameobject_template` and its addon/quest rows; a single spawn's respawn,
/// phase, rotation and pose stay in the inspector beside the world.
/// </summary>
public partial class GameObjectPropertiesWindow : PropertiesWindow
{
    public const string DefaultSectionId = "template";

    /// <summary>The retail client's DB2 lookup, for names the world database does not hold.</summary>
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    /// <summary>Off-screen model thumbnails for the template page, owned by the viewer.</summary>
    public IThumbnailSource? Thumbnails { get; set; }
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    /// <summary>Teleport to one spawn of this template (its GUID, map and server position); returns a status line.</summary>
    public Func<ulong, uint, double, double, double, Task<string>> FlyToSpawn { get; set; } = (_, _, _, _, _) => Task.FromResult("");

    public GameObjectContext Context { get; private set; } = null!;

    public uint? Entry => Context?.Entry;

    /// <summary>The shown section, for tests.</summary>
    public GameObjectSection? SmokeActive => Active as GameObjectSection;
    public bool SmokeLoaded => Context?.Snapshot != null;

    protected override string DefaultSection => DefaultSectionId;
    protected override IEntityContext Shell => Context;
    protected override Vector2 TargetSize => new(980, 700);

    public GameObjectPropertiesWindow() : base("GameObjectProperties")
    {
        CustomMinimumSize = new(760, 520);
        Size = TargetSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        Context = new GameObjectContext(Drafts)
        {
            Connection = () => Connection(),
            Store = () => Store(),
            Lookup = () => Lookup(),
            Thumbnails = () => Thumbnails,
            FlyToSpawn = (guid, map, x, y, z) => FlyToSpawn(guid, map, x, y, z)
        };
        BuildShell(GameObjectSectionRegistry.Create(Context));
    }

    /// <summary>Follow the inspector selection (called every frame while the window is open).</summary>
    public void Track(GameObjectSpawn? spawn)
    {
        bool followed = Context.FollowsSelection;
        Context.Track(spawn);
        if (spawn == null && followed)
        {
            CloseWindow();
        }
    }

    /// <summary>Show the window for a GameObject spawn.</summary>
    public void Open(GameObjectSpawn? spawn, string? section = null)
    {
        Context.Track(spawn);
        Show(section);
    }

    /// <summary>Show the window for a template entry without a spawn (content browser, quest link).</summary>
    public void OpenEntry(uint entry, string name, string? section = null)
    {
        Context.TrackEntry(entry, name);
        Show(section);
    }

    /// <summary>Map/archive change: close and drop everything tied to the old world.</summary>
    public void Reset()
    {
        Context?.Reset();
        CloseWindow();
    }

    protected override (string Title, string Subtitle) Header()
    {
        string name = Context.Name;
        string title = Context.Entry == null ? "No GameObject selected"
            : name.Length > 0 ? "GameObject template: " + name
            : "Loading…";
        string subtitle = Context.Entry is uint entry && Context.Snapshot != null && name.Length > 0
            ? $"{Context.TypeCatalog.Name(Context.Type)}   {ScopeText.Header(PropertyScope.Template, entry, 0, Context.SpawnCount)} · a single spawn is edited in the inspector on the right"
            : Context.Entry != null ? "Loading…" : "";
        return (title, subtitle);
    }

    /// <summary>The open database's section with this id.</summary>
    public GameObjectSection? Section(string id) => Listed.OfType<GameObjectSection>().FirstOrDefault(s => s.Id == id);
}
