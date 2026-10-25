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

            foreach (var option in menu.Options)
            {
                Line($"{option.OptionId}. {(option.Text.Length > 0 ? option.Text : "(no text)")}", ClientStyle.Gold, 13, block);
                // OptionNpc 0 (None) is a plain gossip option: it only shows text or opens a submenu.
                var details = new List<string> { option.OptionNpc == 0 ? "Gossip" : TcCreatureView.OptionNpcName(option.OptionNpc) };
                if (option.ActionMenuId != 0)
                {
                    details.Add($"opens menu {option.ActionMenuId}");
                }

                if (option.TrainerId is { } trainer)
                {
                    details.Add($"trainer {trainer}");
                }

                if (option.ActionPoiId != 0)
                {
                    details.Add($"point of interest {option.ActionPoiId}");
                }

                if (option.SpellId != 0)
                {
                    details.Add($"casts spell {option.SpellId}");
                }

                if (option.BoxText.Length > 0 || option.BoxMoney != 0 || option.Coded)
                {
                    details.Add($"confirm “{option.BoxText}”" + (option.BoxMoney != 0 ? $" for {ClientStyle.Money((long)Math.Min(option.BoxMoney, long.MaxValue))}" : "") + (option.Coded ? ", asks for a code" : ""));
                }

                Line("    " + string.Join(" · ", details), ClientStyle.Faded, 12, block);
            }
        }
    }
}
