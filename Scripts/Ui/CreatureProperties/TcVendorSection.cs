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
        foreach (var item in v.Vendor)
        {
            var block = Block();
            string name = item.Type == 2 ? $"Currency {item.Item}" : item.Info?.Name is { Length: > 0 } known ? $"{known} ({item.Item})" : $"Item {item.Item}";
            Line(name, item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Body, 14, block);
            var details = new List<string> { $"position {item.Slot}" };
            details.Add(item.MaxCount > 0 ? $"stock {item.MaxCount}, restocks every {item.IncrTime} s" : "unlimited stock");
            if (item.ExtendedCost != 0)
            {
                details.Add($"extended cost {item.ExtendedCost}");
            }

            if (item.BonusListIds.Length > 0)
            {
                details.Add($"bonus lists {item.BonusListIds}");
            }

            if (item.PlayerConditionId != 0)
            {
                details.Add($"player condition {item.PlayerConditionId}");
            }

            Line(string.Join(" · ", details), ClientStyle.Faded, 12, block);
        }
    }
}
