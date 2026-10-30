using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// One page of the creature properties window, listed in its left-hand picker. A section is a
/// content control that draws from the shared <see cref="CreatureContext"/>. To add a feature to the
/// creature window, derive from this class and add one line to <see cref="CreatureSectionRegistry"/>.
/// </summary>
public abstract partial class CreatureSection : EntitySection
{
    protected CreatureContext Ctx { get; }

    protected CreatureSection(CreatureContext ctx)
    {
        Ctx = ctx;
    }

    /// <summary>The creature changed (or was cleared): drop state tied to the previous one.</summary>
    public virtual void OnCreatureChanged(CreatureSpawn? creature)
    {
    }

    /// <summary>The line shown while the creature's rows load, or when no creature is selected.</summary>
    protected Label Placeholder() => ClientStyle.Text(Ctx.Target != null ? "Loading…" : "Select a creature in the world.", 15, ClientStyle.Faded);

    /// <summary>Move a panel the context shares (behavior, quest) into <paramref name="parent"/>; it can only live in one place.</summary>
    protected static void Host(Control panel, Control parent)
    {
        if (panel.GetParent() != parent)
        {
            panel.GetParent()?.RemoveChild(panel);
            parent.AddChild(panel);
        }
    }

    /// <summary>
    /// One search box's term and last results, kept across redraws. <see cref="Reset"/> (creature
    /// changed) also drops results of a search still running.
    /// </summary>
    protected sealed class SearchState<T>
    {
        public string Term = "";
        public IReadOnlyList<T> Results = [];
        public int Generation;
        public bool Searching;

        public void Reset()
        {
            Generation++;
            Term = "";
            Results = [];
            Searching = false;
        }
    }

    /// <summary>
    /// A search box and button: the term is trimmed to 80 characters and <paramref name="run"/> fills
    /// <see cref="SearchState{T}.Results"/>, reporting "n {noun}(s) found." on the status line. The
    /// button is disabled while searching and, when <paramref name="needsClient"/>, without a client lookup.
    /// </summary>
    protected void SearchBox<T>(Control parent, SearchState<T> state, string name, string placeholder, string noun, bool needsClient, Func<string, Task<IReadOnlyList<T>>> run)
    {
        var box = new HBoxContainer();
        parent.AddChild(box);
        var term = new LineEdit { Name = name, Text = state.Term, PlaceholderText = placeholder, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var search = new Button { Text = "Search", Disabled = state.Searching || (needsClient && Ctx.Lookup() == null) };
        term.TextChanged += text => state.Term = text;
        async void Search()
        {
            if (state.Searching || (needsClient && Ctx.Lookup() == null)) return;
            string text = term.Text.Trim();
            if (text.Length == 0) return;
            text = text[..Math.Min(80, text.Length)];
            state.Term = text;
            int generation = ++state.Generation;
            state.Searching = true;
            Ctx.Status = $"Searching {noun}s…";
            Ctx.Redraw();
            try
            {
                var found = await run(text);
                if (generation != state.Generation) return;
                state.Results = found;
                Ctx.Status = $"{found.Count} {noun}(s) found.";
            }
            catch (Exception error)
            {
                if (generation != state.Generation) return;
                Ctx.Status = DbErrors.Safe(error);
            }
            if (generation != state.Generation) return;
            state.Searching = false;
            Ctx.Redraw();
        }
        search.Pressed += Search;
        term.TextSubmitted += _ => Search();
        box.AddChild(term);
        box.AddChild(search);
    }

    protected ClientIcons Icons => Ctx.Icons;

    protected static string Shown(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A client icon (a missing one shows the question mark) in front of <paramref name="content"/>, with an optional game tooltip on hover.</summary>
    protected HBoxContainer IconRow(string iconPath, Control content, int size = 16, Tooltip? tooltip = null)
    {
        var row = new TooltipRow();
        if (tooltip != null)
        {
            row.Source = () => tooltip;
            row.TooltipText = "Details";
        }

        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = Icons.Get(iconPath) ?? Icons.Get(ClientStyle.MissingIcon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new(size, size)
        });
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(content);
        return row;
    }

    /// <summary>"!" for a quest the NPC gives, "?" for one it accepts, both for both, in a fixed-width slot so titles line up.</summary>
    protected Control QuestIcons(bool gives, bool takes)
    {
        var slot = new HBoxContainer
        {
            CustomMinimumSize = new(34, 0)
        };
        slot.AddThemeConstantOverride("separation", 2);
        foreach (var path in new[] { gives ? ClientStyle.AvailableQuestIcon : null, takes ? ClientStyle.ActiveQuestIcon : null }.OfType<string>())
        {
            slot.AddChild(new TextureRect
            {
                Texture = Icons.Get(path) ?? Icons.Get(ClientStyle.MissingIcon),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new(16, 16)
            });
        }

        return slot;
    }

    protected readonly Dictionary<string, SearchState<(string Label, Action Pick)>> pickers = [];

    /// <summary>
    /// A <see cref="SearchBox{T}"/> whose results are "+ label" buttons that survive redraws, each with
    /// an optional icon and game tooltip looked up by its label. The searches are bounded and read-only.
    /// </summary>
    protected void SearchPicker(Control parent, string kind, string placeholder, Func<string, Task<IReadOnlyList<(string Label, Action Pick)>>> run, Func<string, string?>? iconFor = null, Func<string, Tooltip?>? tooltipFor = null, bool needsClient = false)
    {
        if (!pickers.TryGetValue(kind, out var state))
        {
            pickers[kind] = state = new();
        }

        SearchBox(parent, state, "Search_" + kind, placeholder, kind, needsClient, run);
        foreach (var (label, pick) in state.Results)
        {
            var result = new TooltipButton
            {
                Text = "+ " + label,
                Alignment = HorizontalAlignment.Left
            };
            if (tooltipFor?.Invoke(label) is { } tip)
            {
                result.Source = () => tip;
                result.TooltipText = label;
            }

            result.Pressed += pick;
            if (iconFor?.Invoke(label) is { } path)
            {
                result.Icon = Icons.Get(path) ?? Icons.Get(ClientStyle.MissingIcon);
                result.ExpandIcon = true;
                result.AddThemeConstantOverride("icon_max_width", 24);
            }

            parent.AddChild(result);
        }
    }

    /// <summary>
    /// What choosing a service option does here: the matching page opens (the returned text is empty) or the
    /// text says which client window it would open. <paramref name="window"/> names that window.
    /// </summary>
    protected string OpenService(GossipService service, string window)
    {
        switch (service)
        {
            case GossipService.Quests:
                Ctx.ShowSection(PickerPages.Quests.Id);
                return "";
            case GossipService.Vendor:
                Ctx.ShowSection(PickerPages.Vendor.Id);
                return "";
            case GossipService.Trainer:
                Ctx.ShowSection(PickerPages.Trainer.Id);
                return "";
            case GossipService.Taxi:
                return "Opens the flight map.";
            case GossipService.Chat:
                return "Closes the gossip window (no submenu).";
            default:
                return $"Opens the client's {window.ToLowerInvariant()} window.";
        }
    }

    /// <summary>One line of a picked text: placeholders resolved, line breaks flattened, long text cut.</summary>
    protected static string Preview(string text, int length = 70)
    {
        text = InteractionText.Render(text).ReplaceLineEndings(" ");
        return text.Length <= length ? text : text[..(length - 1)] + "…";
    }

    /// <summary>Pick an existing gossip menu by ID or greeting text; <paramref name="chosen"/> gets its ID.</summary>
    protected void ChooseMenu(Action<uint> chosen, uint? current = null) => Ctx.Try(() =>
    {
        var db = DBSource.Open(Ctx.Connection(), Ctx.Lookup());
        PickerPopup.Show(this, "Choose gossip menu", async term =>
        {
            var found = await db.SearchMenusAsync(term, CancellationToken.None);
            return found.Select(m => (m.Id, $"{m.Id}: {Preview(m.Name)}")).ToList();
        }, (id, _) => chosen(id), current: current);
    });

    /// <summary>Pick an existing broadcast text by ID or text; <paramref name="chosen"/> gets its ID.</summary>
    protected void ChooseBroadcast(Action<uint> chosen, uint? current = null) => Ctx.Try(() =>
    {
        var db = DBSource.Open(Ctx.Connection(), Ctx.Lookup());
        PickerPopup.Show(this, "Choose broadcast text", async term =>
        {
            var found = await db.SearchBroadcastAsync(term, CancellationToken.None);
            return found.Select(b => (b.Id, $"{b.Id}: {Preview(b.Name)}")).ToList();
        }, (id, _) => chosen(id), current: current);
    });

    protected virtual void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}

/// <summary>A picker entry of the creature window: the key, caption and client icon both dialects' sections share.</summary>
public sealed record PickerPage(string Id, string Title, string IconPath);

/// <summary>
/// The pages that exist in both dialects. An AzerothCore section and its TrinityCore counterpart read
/// their id, title and icon from the same entry, so the two pickers cannot drift apart.
/// </summary>
public static class PickerPages
{
    public static readonly PickerPage Stats = new("stats", "Class / Level / Stats", "Interface/Icons/INV_Sword_04.blp");
    public static readonly PickerPage Gossip = new("gossip", "Gossip", ClientStyle.GossipIcon(0));
    public static readonly PickerPage Quests = new("quests", "Quests", ClientStyle.AvailableQuestIcon);
    public static readonly PickerPage Template = new("template", "Template", "Interface/Icons/INV_Misc_Book_09.blp");
    public static readonly PickerPage Trainer = new("trainer", "Trainer", ClientStyle.GossipIcon(3));
    public static readonly PickerPage Vendor = new("vendor", "Vendor", ClientStyle.GossipIcon(1));
}

/// <summary>
/// The sections of the creature properties window, in picker order (alphabetical by title). Adding a
/// feature to the window is one section class plus one line here; the window, context and tests need
/// no other change. The window lists the sections that support the open database's dialect
/// (<see cref="CreatureSection.Supports"/>): the AzerothCore pages, then the TrinityCore ones; Behavior
/// and Speech serve both.
/// </summary>
public static class CreatureSectionRegistry
{
    public static IReadOnlyList<Func<CreatureContext, CreatureSection>> Factories { get; } =
    [
        ctx => new BehaviorSection(ctx),
        ctx => new AcStatsSection(ctx),
        ctx => new AcGossipSection(ctx),
        ctx => new QuestsSection(ctx),
        ctx => new SpeechSection(ctx),
        ctx => new AcTemplateSection(ctx),
        ctx => new AcTrainerSection(ctx),
        ctx => new AcVendorSection(ctx),
        ctx => new TcStatsSection(ctx),
        ctx => new TcGossipSection(ctx),
        ctx => new TcTemplateSection(ctx),
        ctx => new TcTrainerSection(ctx),
        ctx => new TcVendorSection(ctx),
        ctx => new AppearanceSection(ctx)
    ];

    public static List<CreatureSection> Create(CreatureContext ctx) => Factories.Select(factory => factory(ctx)).ToList();
}
