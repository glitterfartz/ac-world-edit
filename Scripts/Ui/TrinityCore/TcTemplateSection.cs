using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore creature_template plus the creature_template_difficulty row picked on the page, drawn
/// from <see cref="TcCreaturePage"/> by the shared <see cref="PageRenderer"/>. With a draft open the
/// fields are editable and a missing difficulty row can be added.
/// </summary>
public sealed partial class TcTemplateSection(CreatureContext ctx) : TcCreatureSection(ctx)
{
    readonly PageRenderer renderer = new(ctx);

    public override string Id => PickerPages.Template.Id;
    public override string Title => PickerPages.Template.Title;
    public override string IconPath => PickerPages.Template.IconPath;

    public override void OnCreatureChanged(CreatureSpawn? creature) => renderer.Reset();

    protected override void Populate(TcCreatureView v)
    {
        if (Ctx.Editable)
        {
            Line($"The template applies to every spawn of entry {v.Entry}.", ClientStyle.Warning, 11);
        }

        renderer.Render(this, Ctx.Schema.TemplatePage, Ctx.Effective, v.Entry, Ctx.Editable);
    }
}
