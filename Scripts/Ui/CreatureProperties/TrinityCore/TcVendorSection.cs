using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>TrinityCore npc_vendor rows, named from the client's ItemSparse table when it is loaded.</summary>
public sealed partial class TcVendorSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "vendor";
    public override string Title => "Vendor";
    public override string Badge => Ctx.TcView is { Vendor.Length: > 0 } v ? v.Vendor.Length.ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcView is { Vendor.Length: > 0 };

    protected override void Populate(TcCreatureView v)
    {
        if (v.Vendor.Length == 0)
        {
            Line("This creature sells nothing.", ClientStyle.Faded);
            return;
        }

        Heading($"{v.Vendor.Length} item(s) for sale");
        new PageRenderer(Ctx).RenderItems(this, Ctx.Schema.VendorPage, Ctx.Effective, v.Entry, false, row =>
        {
            var info = row.U("type") == 1 && row.I("item") > 0 ? Ctx.Snapshot!.Lookup.Item((uint)row.I("item")) : null;
            string name = row.U("type") == 2 ? $"Currency {row.I("item")}" : info?.Name is { Length: > 0 } known ? $"{known} ({row.I("item")})" : $"Item {row.I("item")}";
            return ClientStyle.Text(name, 14, row.U("type") == 1 && info != null ? ClientStyle.Quality(info.Quality) : ClientStyle.Body);
        });
    }
}
