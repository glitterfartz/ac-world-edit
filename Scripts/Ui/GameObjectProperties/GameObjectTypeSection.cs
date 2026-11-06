using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The chosen type's Data fields, in server order, with a typed editor per field. Fields the
/// server marks unused are hidden behind a toggle; columns outside the type are available as
/// raw numbers. An unknown type falls back to the raw Data page instead of refusing to open.
/// </summary>
public sealed partial class GameObjectTypeSection(GameObjectContext ctx) : GameObjectSection(ctx)
{
    public override string Id => "type";
    public override string Title => "Type properties";
    public override string IconPath => "Interface/Icons/INV_Misc_Gear_01.blp";
    public override string Tip => "The Data fields this object type reads, with each field's effect, allowed values, range and references.";
    public override string Badge => Ctx.Template != null ? Ctx.TypeCatalog.Name(Ctx.Type) : "";

    public override void Refresh()
    {
        Clear();
        if (Ctx.Template is not { } template || Ctx.Entry is not uint entry)
        {
            AddChild(Placeholder());
            return;
        }

        var type = Ctx.TypeCatalog.Find(Ctx.Type);
        AddChild(ClientStyle.Text(type != null ? $"{type.Name} (type {Ctx.Type})" : $"Unknown type {Ctx.Type}", 16, ClientStyle.Header));
        AddChild(ClientStyle.Text("Change the type on the Template page.", 11, ClientStyle.Faded));
        if (type == null)
        {
            AddChild(ClientStyle.Text("This server has no field catalogue for this type; the raw Data columns are shown.", 12, ClientStyle.Warning));
            new PageRenderer(Ctx).Render(this, GameObjectPage.RawPage(Ctx.Dialect), Ctx.Effective, entry, Ctx.Editable, Reference);
            return;
        }

        new PageRenderer(Ctx).Render(this, GameObjectPage.TypePage(Ctx.Dialect, Ctx.Type), Ctx.Effective, entry, Ctx.Editable, Reference);

        var unused = type.UnusedFields.ToList();
        if (unused.Count > 0)
        {
            int nonZero = unused.Count(f => NonZero(template, f));
            var toggle = new CheckBox
            {
                Text = $"Show unused fields ({unused.Count})",
                TooltipText = "Shows the Data columns this type does not read. They keep their stored values."
            };
            var host = new VBoxContainer { Visible = false };
            toggle.Toggled += on =>
            {
                host.Visible = on;
                if (on && host.GetChildCount() == 0)
                {
                    new PageRenderer(Ctx).Render(host, GameObjectPage.UnusedPage(Ctx.Dialect, Ctx.Type), Ctx.Effective, entry, Ctx.Editable, Reference);
                }
            };
            AddChild(toggle);
            AddChild(host);
            if (nonZero > 0)
            {
                AddChild(ClientStyle.Text($"{nonZero} unused field{(nonZero == 1 ? " has" : "s have")} stored values; the toggle shows them.", 11, ClientStyle.Warning));
            }
        }

        var rawToggle = new CheckBox
        {
            Text = "Show raw Data columns",
            TooltipText = "Shows every Data0..N column as a raw number, including the ones this type uses."
        };
        var rawHost = new VBoxContainer { Visible = false };
        rawToggle.Toggled += on =>
        {
            rawHost.Visible = on;
            if (on && rawHost.GetChildCount() == 0)
            {
                new PageRenderer(Ctx).Render(rawHost, GameObjectPage.RawPage(Ctx.Dialect), Ctx.Effective, entry, Ctx.Editable, Reference);
            }
        };
        AddChild(rawToggle);
        AddChild(rawHost);
    }

    Control? Reference(FieldSpec field, Row row) => GameObjectReferenceEditor.Build(this, field, row, Ctx);

    static bool NonZero(Row row, GameObjectField field)
    {
        string column = "Data" + field.Index.ToString(CultureInfo.InvariantCulture);
        if (!row.Values.ContainsKey(column))
        {
            return false;
        }

        string value = row.T(column);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) && number != 0;
    }
}
