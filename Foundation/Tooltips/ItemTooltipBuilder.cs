using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Builds the in-game tooltip text of an item from its template. Lines follow the client's order. The
/// text of item spells ("Use:", "Equip:" effects) and the socket bonus need client spell data and are
/// not included yet.
/// </summary>
public static class ItemTooltipBuilder
{
    static readonly string[] Slots = ["", "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands", "Finger", "Trinket", "One-Hand", "Shield", "Ranged", "Back", "Two-Hand", "Bag", "Tabard", "Chest", "Main Hand", "Off Hand", "Held In Off-hand", "Ammo", "Thrown", "Ranged", "Quiver", "Relic"];
    static readonly string[] Schools = ["", "Holy", "Fire", "Nature", "Frost", "Shadow", "Arcane"];
    static readonly string[] Bondings = ["", "Binds when picked up", "Binds when equipped", "Binds when used", "Quest Item"];

    /// <summary>The tooltip of an item, worded as <paramref name="vocabulary"/> (the 3.3.5 client by default).</summary>
    public static Tooltip Build(ItemDetails item, ItemVocabulary? vocabulary = null)
    {
        var v = vocabulary ?? ItemVocabulary.Wrath;
        List<TooltipLine> lines = [new(item.Info.Name, "", TooltipTone.Title, item.Info.Quality)];
        AddHeader(lines, item, v);
        AddCombat(lines, item);
        AddStats(lines, item, v);
        AddRequirements(lines, item, v);
        AddEquipEffects(lines, item, v);
        AddFooter(lines, item);
        return new(lines);
    }

    static void Add(List<TooltipLine> lines, string text, TooltipTone tone = TooltipTone.Normal) => lines.Add(new(text, "", tone));

    /// <summary>Heroic marker, item level, binding, uniqueness, slot and kind, and bag slots.</summary>
    static void AddHeader(List<TooltipLine> lines, ItemDetails item, ItemVocabulary v)
    {
        if ((item.Flags & 0x8) != 0)
        {
            Add(lines, "Heroic", TooltipTone.Good);
        }

        if (item.ItemLevel > 0)
        {
            Add(lines, $"Item Level {item.ItemLevel}", TooltipTone.Flavor);
        }

        if (item.Bonding is > 0 and < 5)
        {
            Add(lines, Bondings[item.Bonding]);
        }

        if (item.MaxCount == 1)
        {
            Add(lines, "Unique");
        }

        bool weapon = item.Class == 2, armor = item.Class == 4;
        string slot = item.InventoryType < Slots.Length ? Slots[item.InventoryType] : "";
        string kind = weapon ? Pick(v.WeaponKinds, item.Subclass) : armor && item.InventoryType != 16 ? Pick(v.ArmorKinds, item.Subclass) : "";
        if (slot.Length > 0 || kind.Length > 0)
        {
            lines.Add(new(slot, kind));
        }
        else if (item.Class < v.Classes.Length && item.Class != 15 && v.Classes[item.Class].Length > 0)
        {
            Add(lines, v.Classes[item.Class]);
        }

        if (item.ContainerSlots > 0)
        {
            Add(lines, $"{item.ContainerSlots} Slot {(item.Class == 11 ? "Quiver" : "Bag")}");
        }
    }

    /// <summary>Weapon damage, armor and block.</summary>
    static void AddCombat(List<TooltipLine> lines, ItemDetails item)
    {
        AddDamage(item, lines);
        if (item.Armor > 0)
        {
            Add(lines, $"{item.Armor} Armor");
        }

        if (item.Block > 0)
        {
            Add(lines, $"{item.Block} Block");
        }
    }

    /// <summary>Plain stats, resistances, sockets and durability.</summary>
    static void AddStats(List<TooltipLine> lines, ItemDetails item, ItemVocabulary v)
    {
        foreach (var (type, value) in item.Stats.Where(s => v.Plain.ContainsKey(s.Type) && s.Value != 0))
        {
            Add(lines, $"{Signed(value)} {v.Plain[type]}");
        }

        for (int i = 0; i < item.Resistances.Length && i < 6; i++)
        {
            if (item.Resistances[i] != 0)
            {
                Add(lines, $"{Signed(item.Resistances[i])} {Schools[i + 1]} Resistance");
            }
        }

        foreach (uint color in item.Sockets.Where(c => c != 0))
        {
            Add(lines, $"{((color & 1) != 0 ? "Meta" : (color & 2) != 0 ? "Red" : (color & 4) != 0 ? "Yellow" : (color & 8) != 0 ? "Blue" : "Prismatic")} Socket", TooltipTone.Muted);
        }

        if (item.MaxDurability > 0)
        {
            Add(lines, $"Durability {item.MaxDurability} / {item.MaxDurability}");
        }
    }

    /// <summary>Class and race restrictions and the required level.</summary>
    static void AddRequirements(List<TooltipLine> lines, ItemDetails item, ItemVocabulary v)
    {
        AddRestriction(lines, item.AllowableClass, v.AllClasses, v.ClassNames, "Classes");
        AddRestriction(lines, item.AllowableRace, v.AllRaces, v.RaceNames, "Races");
        if (item.RequiredLevel > 1)
        {
            Add(lines, $"Requires Level {item.RequiredLevel}");
        }
    }

    /// <summary>"Equip:" effects from the vocabulary.</summary>
    static void AddEquipEffects(List<TooltipLine> lines, ItemDetails item, ItemVocabulary v)
    {
        foreach (var (type, value) in item.Stats.Where(s => v.Equip.ContainsKey(s.Type) && s.Value != 0))
        {
            Add(lines, v.EquipPrefix + string.Format(CultureInfo.InvariantCulture, v.Equip[type], Math.Abs(value)), TooltipTone.Good);
        }
    }

    /// <summary>Flavour text and sell price.</summary>
    static void AddFooter(List<TooltipLine> lines, ItemDetails item)
    {
        if (item.Description.Length > 0)
        {
            Add(lines, $"\"{item.Description}\"", TooltipTone.Flavor);
        }

        if (item.SellPrice > 0)
        {
            Add(lines, $"Sell Price: {Coins(item.SellPrice)}", TooltipTone.Gold);
        }
    }

    static void AddDamage(ItemDetails item, List<TooltipLine> lines)
    {
        var rolls = item.Damage.Where(d => d.Max > 0).ToArray();
        if (item.Class != 2 || rolls.Length == 0)
        {
            return;
        }

        string School(uint type) => type > 0 && type < Schools.Length ? Schools[type] + " " : "";
        string speed = item.Delay > 0 ? "Speed " + (item.Delay / 1000f).ToString("0.00", CultureInfo.InvariantCulture) : "";
        lines.Add(new($"{Range(rolls[0].Min, rolls[0].Max)} {School(rolls[0].Type)}Damage", speed));
        foreach (var extra in rolls.Skip(1))
        {
            lines.Add(new($"+{Range(extra.Min, extra.Max)} {School(extra.Type)}Damage"));
        }

        if (item.Delay > 0)
        {
            float perSecond = rolls.Sum(d => (d.Min + d.Max) / 2) / (item.Delay / 1000f);
            lines.Add(new($"({perSecond.ToString("0.0", CultureInfo.InvariantCulture)} damage per second)"));
        }
    }

    static void AddRestriction(List<TooltipLine> lines, int mask, int all, string[] names, string label)
    {
        if (mask <= 0 || (mask & all) == all)
        {
            return;
        }

        var allowed = names.Where((name, bit) => name.Length > 0 && (mask & (1 << bit)) != 0).ToArray();
        if (allowed.Length > 0)
        {
            lines.Add(new($"{label}: {string.Join(", ", allowed)}"));
        }
    }

    static string Pick(string[] names, uint index) => index < names.Length ? names[index] : "";

    static string Range(float min, float max) => $"{min.ToString("0.#", CultureInfo.InvariantCulture)} - {max.ToString("0.#", CultureInfo.InvariantCulture)}";

    static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Copper as "1g 20s 5c", leaving out zero parts.</summary>
    public static string Coins(long copper)
    {
        List<string> parts = [];
        if (copper >= 10000)
        {
            parts.Add($"{copper / 10000}g");
        }

        if (copper % 10000 >= 100)
        {
            parts.Add($"{copper % 10000 / 100}s");
        }

        if (copper % 100 > 0 || parts.Count == 0)
        {
            parts.Add($"{copper % 100}c");
        }

        return string.Join(" ", parts);
    }
}
