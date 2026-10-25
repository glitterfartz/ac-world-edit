using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Every TrinityCore gossip menu of the creature: its own menus (creature_template_gossip, which can
/// hold several) and the submenus their options lead to, with each menu's texts and options.
/// </summary>
public sealed partial class TcGossipSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "gossip";
    public override string Title => "Gossip";
    public override string Badge => Ctx.TcView is { } v && v.Menus.Length > 0 ? v.Menus.Count(m => m.Root).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcView is { Menus.Length: > 0 };

    protected override void Populate(TcCreatureView v)
    {
        if (v.Menus.Length == 0)
        {
            Line("This creature has no gossip menu.", ClientStyle.Faded);
            return;
        }

        foreach (var menu in v.Menus)
        {
            Heading($"Menu {menu.MenuId}" + (menu.Root ? "" : " · submenu"));
            var block = Block();
            if (menu.Texts.Length == 0)
            {
                Line("No gossip_menu text: the client shows a default greeting.", ClientStyle.Faded, 12, block);
            }

            foreach (var text in menu.Texts)
            {
                if (text.Missing)
                {
                    Line($"Text {text.TextId} is missing from npc_text.", ClientStyle.Error, 12, block);
                    continue;
                }

                foreach (var variant in text.Variants)
                {
                    string weight = text.Variants.Length > 1 ? $"  ({Number(variant.Probability)}%)" : "";
                    Line($"“{variant.Text ?? $"Broadcast text {variant.BroadcastId} not found"}”{weight}", variant.Text == null ? ClientStyle.Error : ClientStyle.Body, 13, block);
                }

                Line($"npc_text {text.TextId}", ClientStyle.Faded, 11, block);
            }

            new PageRenderer(Ctx).RenderItems(block, Ctx.Schema.GossipPage.Options, Ctx.Effective, menu.MenuId, false,
                row =>
                {
                    var option = menu.Options.First(o => o.OptionId == row.U("OptionID"));
                    string trainer = option.TrainerId is { } id ? $" · trainer {id}" : "";
                    return ClientStyle.Text($"{option.OptionId}. {(option.Text.Length > 0 ? option.Text : "(no text)")}{trainer}", 13, ClientStyle.Gold);
                }, (field, row) =>
                {
                    var option = menu.Options.First(o => o.OptionId == row.U("OptionID"));
                    return field.Column switch
                    {
                        "OptionText" => ClientStyle.Text(option.Text, 13),
                        "BoxText" => ClientStyle.Text(option.BoxText, 13),
                        _ => null
                    };
                }, columns: 2);
        }
    }
}
