using DBCD;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>
/// <see cref="IRetailItemTables"/> over the client's DB2 tables. <paramref name="table"/> loads a table
/// (serialised by the caller); rows are read-only once loaded, so they are read without a lock.
/// </summary>
public sealed class RetailItemTables(Func<string, IDBCDStorage> table) : IRetailItemTables
{
    readonly object gate = new();
    Dictionary<uint, (int Type, List<(float X, float Y)> Points)>? curves;
    IReadOnlyList<ItemSquishEra>? eras;

    float[]? Floats(string name, uint id, string field) => table(name).TryGetValue((int)id, out var row) ? row.FieldAs<float[]>(field) : null;

    public float[]? ArmorQuality(uint itemLevel) => Floats("ItemArmorQuality", itemLevel, "Qualitymod");

    public ArmorFactors? ArmorTotal(uint itemLevel) => table("ItemArmorTotal").TryGetValue((int)itemLevel, out var row)
        ? new(row.FieldAs<float>("Cloth"), row.FieldAs<float>("Leather"), row.FieldAs<float>("Mail"), row.FieldAs<float>("Plate"))
        : null;

    public ArmorFactors? ArmorLocation(uint inventoryType) => table("ArmorLocation").TryGetValue((int)inventoryType, out var row)
        ? new(row.FieldAs<float>("Clothmodifier"), row.FieldAs<float>("Leathermodifier"), row.FieldAs<float>("Chainmodifier"), row.FieldAs<float>("Platemodifier"))
        : null;

    public float[]? ShieldArmor(uint itemLevel) => Floats("ItemArmorShield", itemLevel, "Quality");

    public float[]? Dps(ItemDpsTable which, uint itemLevel) => Floats(which switch
    {
        ItemDpsTable.Ammo => "ItemDamageAmmo",
        ItemDpsTable.TwoHand => "ItemDamageTwoHand",
        ItemDpsTable.TwoHandCaster => "ItemDamageTwoHandCaster",
        ItemDpsTable.OneHandCaster => "ItemDamageOneHandCaster",
        _ => "ItemDamageOneHand"
    }, itemLevel, "Quality");

    public RandomPropertyPoints? RandomPoints(uint itemLevel) => table("RandPropPoints").TryGetValue((int)itemLevel, out var row)
        ? new(row.FieldAs<float[]>("EpicF"), row.FieldAs<float[]>("SuperiorF"), row.FieldAs<float[]>("GoodF"))
        : null;

    public (int Type, IReadOnlyList<(float X, float Y)> Points)? Curve(uint curveId)
    {
        lock (gate)
        {
            curves ??= ReadCurves();
            return curves.TryGetValue(curveId, out var curve) ? (curve.Type, curve.Points) : null;
        }
    }

    /// <summary>Every curve's points ordered by OrderIndex, as the server loads them (only points of known curves).</summary>
    Dictionary<uint, (int Type, List<(float X, float Y)> Points)> ReadCurves()
    {
        var known = table("Curve");
        var byCurve = new Dictionary<uint, List<(int Order, float X, float Y)>>();
        foreach (var row in table("CurvePoint").Values)
        {
            uint curve = row.FieldAs<uint>("CurveID");
            if (!known.ContainsKey((int)curve))
            {
                continue;
            }

            float[] pos = row.FieldAs<float[]>("Pos");
            if (!byCurve.TryGetValue(curve, out var list))
            {
                byCurve[curve] = list = [];
            }

            list.Add((row.FieldAs<int>("OrderIndex"), pos[0], pos[1]));
        }

        return byCurve.ToDictionary(c => c.Key, c => (known[(int)c.Key].FieldAs<int>("Type"), c.Value.OrderBy(p => p.Order).Select(p => (p.X, p.Y)).ToList()));
    }

    public IReadOnlyList<ItemSquishEra> SquishEras
    {
        get
        {
            lock (gate)
            {
                return eras ??= table("ItemSquishEra").Values.OrderBy(row => row.ID)
                    .Select(row => new ItemSquishEra((uint)row.ID, row.FieldAs<int>("Patch"), row.FieldAs<uint>("CurveID"), row.FieldAs<int>("Flags"))).ToList();
            }
        }
    }
}
