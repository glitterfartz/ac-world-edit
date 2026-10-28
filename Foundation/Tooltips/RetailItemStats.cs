namespace ACWorldEdit.Foundation;

/// <summary>Which of the client's weapon damage-per-second tables an item reads (ItemTemplate::GetDPS).</summary>
public enum ItemDpsTable { Ammo, TwoHand, TwoHandCaster, OneHand, OneHandCaster }

/// <summary>Per-armor-type factors: cloth, leather, mail, plate (ItemArmorTotal values, ArmorLocation modifiers).</summary>
public readonly record struct ArmorFactors(float Cloth, float Leather, float Mail, float Plate)
{
    /// <summary>The factor of an armor subclass (1 cloth … 4 plate); anything else is 1.</summary>
    public float For(uint subclass) => subclass switch { 1 => Cloth, 2 => Leather, 3 => Mail, 4 => Plate, _ => 1f };
}

/// <summary>RandPropPoints of one item level: the stat budget by slot group (0 chest-like … 4 relic) for each quality.</summary>
public sealed record RandomPropertyPoints(float[] Epic, float[] Superior, float[] Good);

/// <summary>One era of item level squishing: its items' levels are mapped through <see cref="CurveId"/> once <see cref="Patch"/> is current.</summary>
public readonly record struct ItemSquishEra(uint Id, int Patch, uint CurveId, int Flags);

/// <summary>
/// The client tables the retail item math reads. Every lookup returns null for a level or slot the table
/// does not have, which the calculators treat as zero, as the server does.
/// </summary>
public interface IRetailItemTables
{
    float[]? ArmorQuality(uint itemLevel);
    ArmorFactors? ArmorTotal(uint itemLevel);
    ArmorFactors? ArmorLocation(uint inventoryType);
    float[]? ShieldArmor(uint itemLevel);
    float[]? Dps(ItemDpsTable table, uint itemLevel);
    RandomPropertyPoints? RandomPoints(uint itemLevel);

    /// <summary>The curve's control points in order and its interpolation type, or null for an unknown curve.</summary>
    (int Type, IReadOnlyList<(float X, float Y)> Points)? Curve(uint curveId);

    /// <summary>Squish eras ordered by ID.</summary>
    IReadOnlyList<ItemSquishEra> SquishEras { get; }
}

/// <summary>
/// What a retail item template does not store: effective item level, stat values, armor, weapon damage
/// and durability, computed the way TrinityCore does from the client's tables. Ported from
/// ItemTemplate::GetArmor / GetDPS / GetDamage, Item::GetItemLevel / GetItemStatValue,
/// GetRandomPropertyPoints, DB2Manager::GetCurveValueAt and ObjectMgr's FillMaxDurability (master).
/// Bonus lists, scaling stat distributions and the socket stat cost (a gametable the client does not
/// ship as DB2) are not applied, so the result is the item as sold or looted without bonuses.
/// </summary>
public static class RetailItemStats
{
    const uint ClassWeapon = 2, ClassArmor = 4, SubclassShield = 6, SubclassWand = 19, SubclassBow = 2, SubclassGun = 3, SubclassCrossbow = 18;
    const uint Heirloom = 7, Rare = 3, Artifact = 6;
    const uint Chest = 5, Robe = 20;
    const int CasterWeaponFlag = 0x200;

    /// <summary>The interpolation TrinityCore picks for a curve (DetermineCurveType) and applies at <paramref name="x"/> (GetCurveValueAt).</summary>
    public static float CurveValue(int type, IReadOnlyList<(float X, float Y)> points, float x)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        if (points.Count == 1)
        {
            return points[0].Y;
        }

        // 0 linear, 1 cosine/Catmull-Rom, 2 Bezier by point count, 3 cosine, 4 constant, 5 step.
        int mode = type switch
        {
            1 => points.Count < 4 ? 3 : 1,
            2 => points.Count switch { 2 => 3, 3 => 6, 4 => 7, _ => 2 },
            3 => 3,
            4 => 4,
            5 => 5,
            _ => 0
        };
        int FindIndex(int start)
        {
            while (start < points.Count && points[start].X <= x)
            {
                start++;
            }

            return start;
        }

        switch (mode)
        {
            case 0:
            case 3:
            {
                int i = FindIndex(0);
                if (i == 0) return points[0].Y;
                if (i >= points.Count) return points[^1].Y;
                float xDiff = points[i].X - points[i - 1].X;
                if (xDiff == 0) return points[i].Y;
                float t = (x - points[i - 1].X) / xDiff;
                float dy = points[i].Y - points[i - 1].Y;
                return mode == 0
                    ? t * dy + points[i - 1].Y
                    : dy * (1.0f - MathF.Cos(t * MathF.PI)) * 0.5f + points[i - 1].Y;
            }
            case 1:
            {
                int i = FindIndex(1);
                if (i == 1) return points[1].Y;
                if (i >= points.Count - 1) return points[^2].Y;
                float xDiff = points[i].X - points[i - 1].X;
                if (xDiff == 0) return points[i].Y;
                float mu = (x - points[i - 1].X) / xDiff;
                float a0 = -0.5f * points[i - 2].Y + 1.5f * points[i - 1].Y - 1.5f * points[i].Y + 0.5f * points[i + 1].Y;
                float a1 = points[i - 2].Y - 2.5f * points[i - 1].Y + 2.0f * points[i].Y - 0.5f * points[i + 1].Y;
                float a2 = -0.5f * points[i - 2].Y + 0.5f * points[i].Y;
                return a0 * mu * mu * mu + a1 * mu * mu + a2 * mu + points[i - 1].Y;
            }
            case 6:
            {
                float xDiff = points[2].X - points[0].X;
                if (xDiff == 0) return points[1].Y;
                float mu = (x - points[0].X) / xDiff;
                return (1.0f - mu) * (1.0f - mu) * points[0].Y + (1.0f - mu) * 2.0f * mu * points[1].Y + mu * mu * points[2].Y;
            }
            case 7:
            {
                float xDiff = points[3].X - points[0].X;
                if (xDiff == 0) return points[1].Y;
                float mu = (x - points[0].X) / xDiff;
                return (1.0f - mu) * (1.0f - mu) * (1.0f - mu) * points[0].Y + 3.0f * mu * (1.0f - mu) * (1.0f - mu) * points[1].Y
                    + 3.0f * mu * mu * (1.0f - mu) * points[2].Y + mu * mu * mu * points[3].Y;
            }
            case 2:
            {
                float xDiff = points[^1].X - points[0].X;
                if (xDiff == 0) return points[^1].Y;
                var tmp = points.Select(p => p.Y).ToArray();
                float mu = (x - points[0].X) / xDiff;
                for (int i = tmp.Length - 1; i > 0; i--)
                {
                    for (int k = 0; k < i; k++)
                    {
                        tmp[k] = tmp[k] + mu * (tmp[k + 1] - tmp[k]);
                    }
                }

                return tmp[0];
            }
            case 4:
            {
                int i = FindIndex(0);
                return i == 0 ? points[0].Y : i >= points.Count ? points[^1].Y : 0;
            }
            default:
            {
                int i = FindIndex(0);
                return i == 0 ? points[0].Y : i >= points.Count ? points[^1].Y : points[i - 1].Y;
            }
        }
    }

    static float Curve(IRetailItemTables tables, uint curveId, float x) =>
        tables.Curve(curveId) is { } curve ? CurveValue(curve.Type, curve.Points, x) : 0;

    /// <summary>
    /// The item level the server shows for an unbonused item: the template's level carried through every
    /// squish era after its own up to the client's patch (Item::GetItemLevel). <paramref name="clientPatch"/>
    /// is the build as major*10000 + minor*100 + bugfix (12.1.5 is 120105).
    /// </summary>
    public static uint EffectiveItemLevel(uint baseLevel, uint squishEraId, IRetailItemTables tables, int clientPatch)
    {
        int level = (int)baseLevel;
        foreach (var era in tables.SquishEras.Where(e => e.Id > squishEraId))
        {
            if ((era.Flags & 0x1) != 0)
            {
                continue;
            }

            if (era.Patch > clientPatch)
            {
                break;
            }

            if (era.CurveId != 0)
            {
                level = (int)MathF.Round(Curve(tables, era.CurveId, level), MidpointRounding.AwayFromZero);
            }
        }

        return (uint)Math.Max(level, 0);
    }

    /// <summary>Heirlooms read as rare; artifact quality and above has no budget (quality index into the 7-entry tables).</summary>
    static uint? QualityIndex(uint quality)
    {
        uint q = quality != Heirloom ? quality : Rare;
        return q > Artifact ? null : q;
    }

    /// <summary>ItemTemplate::GetArmor.</summary>
    public static uint Armor(uint itemClass, uint subclass, uint inventoryType, uint quality, uint itemLevel, IRetailItemTables tables)
    {
        if (QualityIndex(quality) is not { } q)
        {
            return 0;
        }

        if (itemClass != ClassArmor || subclass != SubclassShield)
        {
            if (tables.ArmorQuality(itemLevel) is not { } byQuality || tables.ArmorTotal(itemLevel) is not { } total
                || tables.ArmorLocation(inventoryType == Robe ? Chest : inventoryType) is not { } location || subclass is < 1 or > 4 || q >= byQuality.Length)
            {
                return 0;
            }

            return (uint)(byQuality[q] * total.For(subclass) * location.For(subclass) + 0.5f);
        }

        return tables.ShieldArmor(itemLevel) is { } shield && q < shield.Length ? (uint)(shield[q] + 0.5f) : 0;
    }

    /// <summary>ItemTemplate::GetDPS: weapon damage per second at an item level.</summary>
    public static float Dps(uint itemClass, uint subclass, uint inventoryType, uint quality, uint itemLevel, int flags2, IRetailItemTables tables)
    {
        if (itemClass != ClassWeapon || QualityIndex(quality) is not { } q)
        {
            return 0;
        }

        bool caster = (flags2 & CasterWeaponFlag) != 0;
        ItemDpsTable? table = inventoryType switch
        {
            24 => ItemDpsTable.Ammo,
            17 => caster ? ItemDpsTable.TwoHandCaster : ItemDpsTable.TwoHand,
            15 or 25 or 26 => subclass == SubclassWand ? ItemDpsTable.OneHandCaster
                : subclass is SubclassBow or SubclassGun or SubclassCrossbow ? (caster ? ItemDpsTable.TwoHandCaster : ItemDpsTable.TwoHand) : null,
            13 or 21 or 22 => caster ? ItemDpsTable.OneHandCaster : ItemDpsTable.OneHand,
            _ => null
        };
        return table is { } t && tables.Dps(t, itemLevel) is { } dps && q < dps.Length ? dps[q] : 0;
    }

    /// <summary>ItemTemplate::GetDamage: minimum and maximum weapon damage from the DPS, the speed (milliseconds) and the variance.</summary>
    public static (float Min, float Max) Damage(float dps, uint delay, float variance)
    {
        if (dps <= 0)
        {
            return (0, 0);
        }

        float average = dps * delay * 0.001f;
        return ((variance * -0.5f + 1.0f) * average, MathF.Floor(average * (variance * 0.5f + 1.0f) + 0.5f));
    }

    /// <summary>GetRandomPropertyPoints: the stat budget of an item level for the item's slot and quality.</summary>
    public static float RandomPoints(uint itemLevel, uint quality, uint inventoryType, uint subclass, IRetailItemTables tables)
    {
        int index = inventoryType switch
        {
            1 or 4 or 5 or 7 or 15 or 17 or 20 or 25 => 0,
            26 => subclass == SubclassWand ? 3 : 0,
            13 or 21 or 22 => 3,
            3 or 6 or 8 or 10 or 12 => 1,
            2 or 9 or 11 or 14 or 16 or 23 => 2,
            28 => 4,
            _ => -1
        };
        if (index < 0 || tables.RandomPoints(itemLevel) is not { } points)
        {
            return 0;
        }

        float[] budget = quality switch
        {
            2 => points.Good,
            3 or 7 => points.Superior,
            4 or 5 or 6 => points.Epic,
            _ => []
        };
        return index < budget.Length ? budget[index] : 0;
    }

    /// <summary>
    /// Item::GetItemStatValue: <paramref name="percent"/> (StatPercentEditor, in ten-thousandths) of the
    /// slot's stat budget, truncated like the server's int conversion.
    /// </summary>
    public static int StatValue(int percent, uint itemLevel, uint quality, uint inventoryType, uint subclass, IRetailItemTables tables) =>
        (int)(percent * RandomPoints(itemLevel, quality, inventoryType, subclass, tables) * 0.0001f);

    /// <summary>ObjectMgr FillMaxDurability: armor and weapons only, from the quality, slot or weapon kind and a penalty below level 28.</summary>
    public static uint MaxDurability(uint itemClass, uint subclass, uint inventoryType, uint quality, uint itemLevel)
    {
        if (itemClass is not (ClassArmor or ClassWeapon) || quality >= QualityMultipliers.Length)
        {
            return 0;
        }

        float penalty = itemLevel <= 28 ? 0.966f - (28u - itemLevel) / 54.0f : 1.0f;
        if (itemClass == ClassArmor)
        {
            return inventoryType >= ArmorMultipliers.Length
                ? 0
                : 5 * (uint)MathF.Round(25.0f * QualityMultipliers[quality] * ArmorMultipliers[inventoryType] * penalty, MidpointRounding.AwayFromZero);
        }

        return subclass >= WeaponMultipliers.Length
            ? 0
            : 5 * (uint)MathF.Round(18.0f * QualityMultipliers[quality] * WeaponMultipliers[subclass] * penalty, MidpointRounding.AwayFromZero);
    }

    static readonly float[] QualityMultipliers = [0.92f, 0.92f, 0.92f, 1.11f, 1.32f, 1.61f, 0.0f, 0.0f];

    // By inventory type: head, shoulders, chest/robe, waist, legs, feet, wrists, hands and shields wear out.
    static readonly float[] ArmorMultipliers =
    [
        0.00f, 0.60f, 0.00f, 0.60f, 0.00f, 1.00f, 0.33f, 0.72f, 0.48f, 0.33f, 0.33f, 0.00f, 0.00f, 0.00f, 0.72f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 1.00f
    ];

    // By weapon subclass: axe … fishing pole.
    static readonly float[] WeaponMultipliers =
    [
        0.91f, 1.00f, 1.00f, 1.00f, 0.91f, 1.00f, 1.00f, 0.91f, 1.00f, 1.00f, 1.00f, 0.00f, 0.00f, 0.66f, 0.00f, 0.66f, 0.00f, 0.00f, 1.00f, 0.66f, 0.66f
    ];
}
