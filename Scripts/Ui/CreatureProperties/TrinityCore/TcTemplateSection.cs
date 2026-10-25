using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore creature_template plus the creature_template_difficulty row picked on the page, drawn
/// from <see cref="TcCreaturePage"/> by the shared <see cref="PageRenderer"/>. With a draft open the
/// fields are editable; a difficulty the creature has no row for cannot be added yet.
/// </summary>
public sealed partial class TcTemplateSection(CreatureContext ctx) : TcSection(ctx)
{
    readonly PageRenderer renderer = new(ctx);

    public override string Id => "template";
    public override string Title => "Template";

    public override void OnCreatureChanged(CreatureSpawn? creature) => renderer.Reset();

    protected override void Populate(TcCreatureView v)
    {
        if (Ctx.TcTemplateEditable)
        {
            Line($"The template applies to every spawn of entry {v.Entry}.", ClientStyle.Warning, 11);
        }

        renderer.Render(this, Ctx.Schema.TemplatePage, Ctx.Effective, v.Entry, Ctx.TcTemplateEditable);
    }
}
