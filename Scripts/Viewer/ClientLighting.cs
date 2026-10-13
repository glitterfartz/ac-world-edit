using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>
/// One Light.dbc time-of-day band: up to 16 (time, color) keyframes cycling over a 2880-tick day
/// (tick = in-game minute * 2; hour = tick/120). Packed values are 0x00RRGGBB.
/// </summary>
public sealed class LightBand
{
    public const double CycleLength = 2880;
    readonly uint[] times;
    readonly Color[] colors;

    LightBand(uint[] times, Color[] colors)
    {
        this.times = times;
        this.colors = colors;
    }

    /// <summary>
    /// Read one positional LightIntBand row. A zero-row band (unused slot) holds a single white
    /// sample so callers always get a defined color.
    /// </summary>
    public static LightBand Read(DbcTable table, int row)
    {
        int n = Math.Clamp((int)table.UAt(row, 1), 0, 16);
        if (n == 0)
        {
            return new([0], [Colors.White]);
        }

        var times = new uint[n];
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            times[i] = table.UAt(row, 2 + i);
            uint packed = table.UAt(row, 18 + i);
            colors[i] = new Color(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
        }

        return new(times, colors);
    }

    /// <summary>
    /// Interpolate the cyclic piecewise-linear curve at an arbitrary tick, wrapping the final segment
    /// back to the first sample.
    /// </summary>
    public Color At(double tick)
    {
        if (colors.Length == 1)
        {
            return colors[0];
        }

        double t = ((tick % CycleLength) + CycleLength) % CycleLength;
        for (int i = 0; i < times.Length; i++)
        {
            int j = (i + 1) % times.Length;
            double a = times[i];
            double b = j == 0 ? times[j] + CycleLength : times[j];
            if (t >= a && t < b)
            {
                float f = b > a ? (float)((t - a) / (b - a)) : 0;
                return colors[i].Lerp(colors[j], f);
            }
        }

        return colors[^1];
    }
}

/// <summary>
/// One LightParams entry's resolved skybox and relevant time-of-day bands. Only the Default
/// weather/time slot is read; dungeon/underwater/combat/death overrides and bands 8-17
/// (secondary accents and water-specific colors) are unused future fidelity work.
/// </summary>
public sealed record LightProfile(uint SkyboxId, LightBand Diffuse, LightBand Ambient, LightBand SkyTop, LightBand SkyHorizon, LightBand Fog);

/// <summary>
/// One Light.dbc region: a falloff circle (or, with FalloffEnd 0, a map-wide default) referencing a
/// Default-slot LightParams profile. Position is pre-converted to the viewer's Y-up basis.
/// </summary>
public sealed record LightRegion(uint Id, uint ContinentId, Vector3 ViewPosition, float FalloffStart, float FalloffEnd, uint ParamsId)
{
    public bool IsDefault => FalloffEnd <= 0;
}

/// <summary>
/// Client Light.dbc/LightParams.dbc/LightSkybox.dbc/LightIntBand.dbc, resolved into per-map regions
/// and per-LightParams time-of-day profiles. Position/falloff use a 1/36-yard scale with the tile-grid
/// corner as origin (no recentering); this was confirmed, not assumed, by checking that converted
/// Northrend/Eastern Kingdoms/Kalimdor region positions land on tiles the respective WDTs actually
/// mark present (artifacts/light-probe, kept local/gitignored). LightIntBand/LightFloatBand associate
/// with a LightParams row by file position (18 rows per entry), not by ID; both tables carry small ID
/// gaps from deleted client dev rows, confirmed by LightIntBand's row count equalling
/// LightParams' row count times 18 on the mounted 3.3.5a client.
/// </summary>
public sealed class ClientLighting
{
    public IReadOnlyList<LightRegion> Regions { get; }
    readonly Dictionary<uint, LightProfile> profiles;
    readonly Dictionary<uint, string> skyboxPaths;

    ClientLighting(List<LightRegion> regions, Dictionary<uint, LightProfile> profiles, Dictionary<uint, string> skyboxPaths)
    {
        Regions = regions;
        this.profiles = profiles;
        this.skyboxPaths = skyboxPaths;
    }

    public LightProfile? Profile(uint paramsId) => profiles.GetValueOrDefault(paramsId);
    public string? SkyboxPath(uint skyboxId) => skyboxId == 0 ? null : skyboxPaths.GetValueOrDefault(skyboxId);
    public IEnumerable<LightRegion> ForMap(uint mapId) => Regions.Where(r => r.ContinentId == mapId);

    /// <summary>
    /// Parse the four client tables and resolve every region's Default-slot profile/skybox. Returns
    /// null rather than a partially valid table on any malformed/missing archive data.
    /// </summary>
    public static ClientLighting? TryLoad(AssetStore store)
    {
        try
        {
            var light = new DbcTable(store.Read(@"DBFilesClient/Light.dbc"));
            var lightParams = new DbcTable(store.Read(@"DBFilesClient/LightParams.dbc"));
            var skybox = new DbcTable(store.Read(@"DBFilesClient/LightSkybox.dbc"));
            var intBand = new DbcTable(store.Read(@"DBFilesClient/LightIntBand.dbc"));
            double center = 32.0 * WorldCoordinates.TileSize;
            var regions = new List<LightRegion>();
            foreach (uint id in light.Ids)
            {
                double dbcX = light.F(id, 2), dbcY = light.F(id, 3), dbcZ = light.F(id, 4);
                var server = new Point3(center - dbcZ / 36.0, center - dbcX / 36.0, dbcY / 36.0);
                var view = WorldCoordinates.ToView(server);
                uint paramsId = light.U(id, 7);
                regions.Add(new(id, light.U(id, 1), new((float)view.X, (float)view.Y, (float)view.Z), light.F(id, 5) / 36f, light.F(id, 6) / 36f, paramsId));
            }

            Dictionary<uint, LightProfile> profiles = [];
            Dictionary<uint, string> skyboxPaths = [];
            foreach (uint paramsId in regions.Select(r => r.ParamsId).Distinct())
            {
                if (!lightParams.Contains(paramsId))
                {
                    continue;
                }

                uint skyboxId = lightParams.U(paramsId, 2);
                int baseRow = lightParams.Position(paramsId) * 18;
                profiles[paramsId] = new(
                    skyboxId,
                    LightBand.Read(intBand, baseRow + 0),
                    LightBand.Read(intBand, baseRow + 1),
                    LightBand.Read(intBand, baseRow + 2),
                    LightBand.Read(intBand, baseRow + 6),
                    LightBand.Read(intBand, baseRow + 7));
                if (skyboxId > 0 && skybox.Contains(skyboxId) && !skyboxPaths.ContainsKey(skyboxId))
                {
                    skyboxPaths[skyboxId] = BinaryAsset.ModelPath(skybox.Str(skyboxId, 1));
                }
            }

            return new(regions, profiles, skyboxPaths);
        }
        catch (Exception e)
        {
            GD.PushWarning("Client lighting unavailable: " + e.Message);
            return null;
        }
    }
}
