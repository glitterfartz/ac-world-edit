using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Everything editable about the selected creature in one window: a section picker on the left
/// (<see cref="CreatureSectionRegistry"/>) and the chosen section on the right. The frame is
/// <see cref="PropertiesWindow"/>; this class owns the creature selection and header text, the data lives in
/// <see cref="CreatureContext"/> and the pages in <see cref="CreatureSection"/> classes.
/// </summary>
public partial class CreaturePropertiesWindow : PropertiesWindow
{
    public const string DefaultSectionId = "gossip";

    public Func<AreaLookup?> Areas { get; set; } = () => null;
    /// <summary>The retail client's DB2 lookup, for names a TrinityCore database does not hold.</summary>
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    /// <summary>Open the behavior of another creature entry (quest giver cards).</summary>
    public Action<uint> BehaviorForEntryRequested { get; set; } = _ => { };

    public CreatureContext Context { get; private set; } = null!;
    CreatureSpawn? lastSelection;

    public uint? Entry => Context?.Target?.TemplateId;

    /// <summary>The shown section, for tests.</summary>
    public CreatureSection? SmokeActive => Active as CreatureSection;

    public event Action<string>? SummaryChanged;
    public event Action<uint>? QuestEditorRequested;

    protected override string DefaultSection => DefaultSectionId;
    protected override IEntityContext Shell => Context;
    protected override Vector2 TargetSize => new(980, 700);

    public CreaturePropertiesWindow() : base("CreatureProperties")
    {
        CustomMinimumSize = new(760, 520);
        Size = TargetSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        Context = new CreatureContext(Drafts, () => Store())
        {
            Connection = () => Connection(),
            Lookup = () => Lookup(),
            Areas = () => Areas(),
            FlyToEntry = (entry, name) => FlyToEntry(entry, name),
            BehaviorForEntryRequested = entry => BehaviorForEntryRequested(entry)
        };
        Context.CreatureChanged += creature =>
        {
            foreach (var section in Sections.OfType<CreatureSection>())
            {
                section.OnCreatureChanged(creature);
            }
        };
        Context.SummaryChanged += text => SummaryChanged?.Invoke(text);
        Context.QuestEditorRequested += quest => QuestEditorRequested?.Invoke(quest);
        Context.SectionRequested += ShowSection;
        BuildShell(CreatureSectionRegistry.Create(Context));
    }

    /// <summary>
    /// Follow the inspector selection (called every frame). A creature opened by entry (a quest giver
    /// card) stays until the selection actually changes.
    /// </summary>
    public void Track(CreatureSpawn? spawn)
    {
        if (spawn?.Guid == lastSelection?.Guid && spawn?.TemplateId == lastSelection?.TemplateId)
        {
            return;
        }

        lastSelection = spawn;
        Context.Track(spawn);
        if (spawn == null)
        {
            CloseWindow();
        }
    }

    /// <summary>Show the window for a creature on a section (the last one when null).</summary>
    public void Open(CreatureSpawn? spawn, string? section = null)
    {
        lastSelection = spawn;
        Context.Track(spawn);
        Show(section);
    }

    /// <summary>Show the window for a creature entry without a spawn (quest giver cards).</summary>
    public void OpenEntry(uint entry, string name, string? section = null)
    {
        Context.Track(new CreatureSpawn(0, entry, name, new WorldPose(0, 0, 0, 0, 0), 1, 1));
        Show(section);
    }

    /// <summary>Map/archive change: close and drop everything tied to the old world.</summary>
    public void Reset()
    {
        lastSelection = null;
        Context?.Reset();
        CloseWindow();
    }

    protected override (string Title, string Subtitle) Header()
    {
        var view = Context.View;
        string? name = view?.Name;
        string subname = view?.Subname ?? "";
        // This window edits the creature template, shared by every spawn of it; one spawn's own settings are in the inspector beside the world.
        string title = name != null ? "Creature template: " + name : Context.Target != null ? "Loading…" : "No creature selected";
        string subtitle = name == null ? "" : (subname.Length > 0 ? $"<{subname}>   " : "") + ScopeText.Header(PropertyScope.Template, view!.Entry, 0, Context.Snapshot?.SpawnCount ?? 0) + " · a single spawn is edited in the inspector on the right";
        return (title, subtitle);
    }

    /// <summary>The open database's section with this id.</summary>
    public CreatureSection? Section(string id) => Listed.OfType<CreatureSection>().FirstOrDefault(s => s.Id == id);
}
