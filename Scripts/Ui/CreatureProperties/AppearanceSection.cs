using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The template's own appearance (`creature_template_addon`): mount, stance, weapons, emote and auras that every
/// spawn of the creature shows, unless the spawn has an addon row of its own, which replaces this one completely.
/// A single spawn's appearance is edited in the inspector beside the world, not here.
/// </summary>
public sealed partial class AppearanceSection : CreatureSection
{
    readonly Label use = ClientStyle.Text("", 14, ClientStyle.Body);
    readonly SpawnInspector fields = new(appearanceOnly: true);
    TemplateAddons? addons;
    TemplateUse? templateUse;
    uint useFor;
    bool built;
    readonly HashSet<uint> requested = [];
    readonly HashSet<uint> useRequested = [];

    public AppearanceSection(CreatureContext ctx) : base(ctx)
    {
    }

    public override bool Supports(WorldDialect dialect) => true;

    public override string Id => "appearance";
    public override string Title => "Appearance";
    public override string IconPath => "Interface/Icons/INV_Misc_Cape_01.blp";
    public override string Tip => "The mount, stance, weapons, idle emote and auras that every spawn of this creature shows unless the spawn has its own appearance.";

    void Build()
    {
        built = true;
        use.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(use);
        AddChild(fields);
        fields.Choices = _ => SpawnFieldPickers.Emotes(Ctx.Dialect, Ctx.Catalog, Ctx.Lookup());
        fields.FlagBits = field => SpawnFieldPickers.Flags(Ctx.Dialect, field);
        fields.Describe = SpawnFieldPickers.Describe;
        fields.Choose = (field, picked) =>
        {
            if (field.Column is not ("mount" or "modelid" or "MountCreatureID"))
            {
                return false;
            }

            SpawnFieldPickers.Choose(fields, field, picked, () => Ctx.Connection(), () => Ctx.Store(), () => Ctx.Lookup(), Ctx.Thumbnails());
            return true;
        };
        fields.ChooseAura = (field, picked) => SpawnFieldPickers.ChooseAura(fields, field, picked, () => Ctx.Connection(), () => Ctx.Store(), () => Ctx.Lookup());
        fields.Edited += Edit;
    }

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        templateUse = null;
    }

    public override void Refresh()
    {
        if (!built)
        {
            Build();
        }

        var snapshot = Ctx.Snapshot;
        if (snapshot == null || !Ctx.Editable)
        {
            use.Text = snapshot == null ? "Select a creature in the world." : "Editing is disabled.";
            fields.Show(Ctx.Dialect, SpawnKind.Creature, null, null, null, null, false);
            return;
        }

        addons ??= new TemplateAddons(Ctx.Drafts, ContentDraftFiles.Sources.DialectOf);
        uint entry = snapshot.Entry;
        string source = Ctx.SourceKey;
        LoadAddon(entry, source);
        LoadUse(entry);
        bool loaded = addons.IsLoaded(source, entry);
        var row = addons.RowOf(source, entry) ?? SpawnEdits.NewTemplateAddon(Ctx.Schema, entry);
        string spawns(int n) => n == 1 ? "1 spawn" : $"{n} spawns";
        use.Text = !loaded ? "Loading the template's appearance…"
            : templateUse == null ? "Counting the spawns that use it…"
            : $"Shown by every spawn of this creature unless the spawn has its own appearance. Used by {spawns(templateUse.Spawns)}; {templateUse.WithOwnAppearance} {(templateUse.WithOwnAppearance == 1 ? "has" : "have")} their own and {(templateUse.WithOwnAppearance == 1 ? "doesn't" : "don't")} follow this page.";
        fields.Show(Ctx.Dialect, SpawnKind.Creature, row, row, null, null, loaded);
    }

    async void LoadAddon(uint entry, string source)
    {
        if (addons == null || addons.IsLoaded(source, entry) || !requested.Add(entry))
        {
            return;
        }

        try
        {
            addons.Load(source, entry, await DBSource.Open(Ctx.Connection()).ReadTemplateAddonAsync(entry, default));
        }
        catch (Exception)
        {
            requested.Remove(entry);
            Ctx.Status = "Could not read the template's appearance.";
        }

        Ctx.Redraw();
    }

    async void LoadUse(uint entry)
    {
        if ((templateUse != null && useFor == entry) || !useRequested.Add(entry))
        {
            return;
        }

        try
        {
            templateUse = await DBSource.Open(Ctx.Connection()).CountTemplateUseAsync(entry, default);
            useFor = entry;
        }
        catch (Exception)
        {
            templateUse = new(0, 0);
        }

        useRequested.Remove(entry);
        Ctx.Redraw();
    }

    /// <summary>Apply a typed value to the template's addon row as one undoable command.</summary>
    void Edit(SpawnField field, string text)
    {
        if (addons == null || Ctx.Snapshot is not { } snapshot)
        {
            return;
        }

        string error = "";
        var result = addons.Edit(Ctx, Ctx.SourceKey, snapshot.Entry, "Set template " + field.Label.ToLowerInvariant(), SpawnEdits.NewTemplateAddon(Ctx.Schema, snapshot.Entry), row =>
        {
            var (changed, problem) = SpawnFields.Apply(field, row, text);
            error = problem;
            return changed ?? row;
        });
        Ctx.Status = error.Length > 0 ? error : result.Ok ? "Saved." : result.Message;
        Ctx.Redraw();
    }

    /// <summary>The fields and note the user sees, for tests.</summary>
    public SpawnInspector SmokeFields => fields;

    public string SmokeUse => use.Text;
}
