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
public abstract partial class AcInteractionSection(CreatureContext ctx) : CreatureSection(ctx)
{
    protected virtual VBoxContainer body => this;
    protected bool editable => Ctx.Editable;
    protected string status { get => Ctx.Status; set => Ctx.Status = value; }
    protected CreatureSpawn? target => Ctx.Target;
    protected InteractionSnapshot? snapshot => Ctx.Snapshot;
    protected RowSet effective => Ctx.Effective;
    protected InteractionCatalog? catalog => Ctx.Catalog;
    protected ClientIcons icons => Icons;
    protected InteractionDraft? draft => Ctx.Draft;
    protected Row TemplateRow => Ctx.TemplateRow;

    protected Func<WorldConnection> Connection => Ctx.Connection;
    protected void Render() => Ctx.Redraw();
    protected bool Commit(string label, IEnumerable<RowEdit> edits) => Ctx.Commit(label, edits);
    protected uint NextId(TableSchema table, string column, uint databaseMax) => Ctx.NextId(table, column, databaseMax);
    protected void Try(Action action) => Ctx.Try(action);

    /// <summary>Fill the section for a loaded creature (called by <see cref="Refresh"/>).</summary>
    protected abstract void Populate(AcCreatureView v);

    public override void Refresh()
    {
        Clear();
        if (Ctx.AcView is { } v)
        {
            Populate(v);
        }
        else
        {
            body.AddChild(Placeholder());
        }
    }

    /// <summary>The shared search picker drawn into this page's body.</summary>
    protected void SearchPicker(string kind, string placeholder, Func<string, Task<IReadOnlyList<(string Label, Action Pick)>>> run, Func<string, string?>? iconFor = null, Func<string, Tooltip?>? tooltipFor = null, string tip = "") =>
        SearchPicker(body, kind, placeholder, run, iconFor, tooltipFor, false, tip);
}
