using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// Shared plumbing of the sections that draw the NPC interaction rows (gossip, quests, vendor,
/// trainer, info): names for the context state, icon rows and the search-and-pick control.
/// </summary>
public abstract partial class InteractionSection(CreatureContext ctx) : CreatureSection(ctx)
{
    protected virtual VBoxContainer body => this;
    protected bool editable => Ctx.Editable;
    protected string status { get => Ctx.Status; set => Ctx.Status = value; }
    protected CreatureSpawn? target => Ctx.Target;
    protected InteractionSnapshot? snapshot => Ctx.Snapshot;
    protected RowSet effective => Ctx.Effective;
    protected InteractionCatalog? catalog => Ctx.Catalog;
    protected ClientIcons icons => Ctx.Icons;
    protected InteractionDraft? draft => Ctx.Draft;
    protected Row TemplateRow => Ctx.TemplateRow;
    protected readonly Dictionary<string, (string Term, List<(string Label, Action Pick)> Results)> searches = [];

    protected Func<DatabaseSettings> Connection => Ctx.Connection;
    protected void Render() => Ctx.Redraw();
    protected bool Commit(string label, IEnumerable<RowEdit> edits) => Ctx.Commit(label, edits);
    protected uint NextId(TableSchema table, string column, uint databaseMax) => Ctx.NextId(table, column, databaseMax);
    protected void Try(Action action) => Ctx.Try(action);

    /// <summary>Fill the section for a loaded creature (called by <see cref="Refresh"/>).</summary>
    protected abstract void Populate(NpcInteraction v);

    public override void Refresh()
    {
        Clear();
        if (Ctx.View is { } v)
        {
            Populate(v);
        }
        else
        {
            body.AddChild(ClientStyle.Text(Ctx.Target != null ? "Loading…" : "Select a creature in the world.", 15, ClientStyle.Faded));
        }
    }

    /// <summary>A field label that shows the explanation on hover.</summary>
    protected static Label Caption(string name, string tip)
    {
        var label = ClientStyle.Tag(name);
        label.TooltipText = tip;
        label.MouseFilter = MouseFilterEnum.Stop;
        return label;
    }

    protected static string Shown(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

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
            Texture = icons.Get(iconPath) ?? icons.Get(ClientStyle.MissingIcon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new(size, size)
        });
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(content);
        return row;
    }

    /// <summary>
    /// Search box with result buttons that survive re-renders; searches are bounded and read-only.
    /// </summary>
    protected void SearchPicker(string kind, string placeholder, Func<string, Task<IReadOnlyList<(string Label, Action Pick)>>> run, Func<string, string?>? iconFor = null, Func<string, Tooltip?>? tooltipFor = null)
    {
        var current = searches.GetValueOrDefault(kind, ("", []));
        var row = new HBoxContainer();
        body.AddChild(row);
        var term = new LineEdit
        {
            Text = current.Term,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Name = "Search_" + kind
        };
        var go = new Button
        {
            Text = "Search"
        };
        async void Search()
        {
            string text = term.Text;
            status = "Searching…";
            Render();
            try
            {
                var results = await run(text);
                searches[kind] = (text, results.ToList());
                status = $"{results.Count} result(s) for \"{text}\".";
            }
            catch (Exception error)
            {
                status = CreatureSource.SafeError(error);
            }

            Render();
        }

        term.TextSubmitted += _ => Search();
        go.Pressed += Search;
        row.AddChild(term);
        row.AddChild(go);
        foreach (var (label, pick) in current.Results)
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
                result.Icon = icons.Get(path) ?? icons.Get(ClientStyle.MissingIcon);
                result.ExpandIcon = true;
                result.AddThemeConstantOverride("icon_max_width", 24);
            }

            body.AddChild(result);
        }
    }
}
