using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>
/// Godot Color view of the pure LightBand curve (Scripts/Assets/World/LightBand.cs).
/// </summary>
public sealed partial class LightBand
{
    public Color At(double tick)
    {
        var (r, g, b) = Rgb(tick);
        return new Color(r, g, b);
    }
}

/// <summary>
/// One LightParams entry's resolved skybox and relevant time-of-day bands. Only the Default
/// weather/time slot is read; dungeon/underwater/combat/death overrides and bands 8-17
/// (secondary accents and water-specific colors) are unused future fidelity work.
/// </summary>
public sealed record LightProfile(uint SkyboxId, LightBand Diffuse, LightBand Ambient, LightBand SkyTop, LightBand SkyHorizon, LightBand Fog, (uint Time, float End, float Scaler)[]? FogDistance = null)
{
    /// <summary>
    /// Retail fog distance at a tick: the client fades linearly from End * Scaler to End, so End is where the
    /// fog is complete. Null for Wrath profiles and for samples without a usable distance.
    /// </summary>
    public float? FogEndAt(double tick)
    {
        if (FogDistance is not { Length: > 0 } samples)
        {
            return null;
        }

        if (samples.Length == 1)
        {
            return samples[0].End > 0 ? samples[0].End : null;
        }

        int next = Array.FindIndex(samples, s => s.Time > tick);
        var (before, after) = next switch
        {
            < 0 => (samples[^1], samples[0]),
            0 => (samples[^1], samples[0]),
            _ => (samples[next - 1], samples[next])
        };
        double span = (after.Time + (after.Time <= before.Time ? LightBand.CycleLength : 0)) - before.Time;
        double at = tick < before.Time ? tick + LightBand.CycleLength : tick;
        float t = span <= 0 ? 0f : (float)Math.Clamp((at - before.Time) / span, 0, 1);
        float end = before.End + (after.End - before.End) * t;
        return end > 0 ? end : null;
    }
}

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
    /// Retail Light, LightParams, LightSkybox and LightData DB2 tables resolved the same way as the
    /// Wrath ones. Light positions are server coordinates in yards (no 1/36 scale); only the first
    /// (default weather) LightParams slot is read. LightData rows carry one (time, color) sample each
    /// and are grouped per LightParams. Returns null on any failure.
    /// </summary>
    public static ClientLighting? TryLoadRetail(Db2Tables tables)
    {
        try
        {
            var samples = new Dictionary<uint, List<DBCD.DBCDRow>>();
            foreach (var row in tables.Table("LightData").Values)
            {
                uint id = Convert.ToUInt32(row["LightParamID"]);
                if (!samples.TryGetValue(id, out var list))
                {
                    samples[id] = list = [];
                }

                list.Add(row);
            }

            Dictionary<uint, uint> paramSkybox = [];
            foreach (var row in tables.Table("LightParams").Values)
            {
                paramSkybox[(uint)row.ID] = Convert.ToUInt32(row["LightSkyboxID"]);
            }

            Dictionary<uint, string> skyboxPaths = [];
            foreach (var row in tables.Table("LightSkybox").Values)
            {
                uint fileId = Convert.ToUInt32(row["SkyboxFileDataID"]);
                if (fileId != 0)
                {
                    skyboxPaths[(uint)row.ID] = AssetRef.FromFileId(fileId);
                }
            }

            var regions = new List<LightRegion>();
            foreach (var row in tables.Table("Light").Values)
            {
                var coords = ((Array)row["GameCoords"]).Cast<object>().Select(Convert.ToDouble).ToArray();
                uint paramsId = Convert.ToUInt32(((Array)row["LightParamsID"]).GetValue(0));
                regions.Add(new((uint)row.ID, Convert.ToUInt32(row["ContinentID"]), ViewSpace.ToView(coords[0], coords[1], coords[2]),
                    Convert.ToSingle(row["GameFalloffStart"]), Convert.ToSingle(row["GameFalloffEnd"]), paramsId));
            }

            LightBand Band(List<DBCD.DBCDRow> rows, string column) =>
                LightBand.FromSamples(rows.Select(r => (Convert.ToUInt32(r["Time"]), unchecked((uint)Convert.ToInt64(r[column])))));

            Dictionary<uint, LightProfile> profiles = [];
            foreach (uint paramsId in regions.Select(r => r.ParamsId).Distinct())
            {
                if (!samples.TryGetValue(paramsId, out var rows))
                {
                    continue;
                }

                profiles[paramsId] = new(
                    paramSkybox.GetValueOrDefault(paramsId),
                    Band(rows, "DirectColor"),
                    Band(rows, "AmbientColor"),
                    Band(rows, "SkyTopColor"),
                    Band(rows, "SkySmogColor"),
                    Band(rows, "SkyFogColor"),
                    [.. rows.Select(r => (Convert.ToUInt32(r["Time"]), Convert.ToSingle(r["FogEnd"]), Convert.ToSingle(r["FogScaler"]))).OrderBy(s => s.Item1)]);
            }

            return new(regions, profiles, skyboxPaths);
        }
        catch (Exception e)
        {
            GD.PushWarning("Retail lighting unavailable: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Parse the four client tables and resolve every region's Default-slot profile/skybox. Returns
    /// null rather than a partially valid table on any malformed/missing archive data.
    /// </summary>
    public static ClientLighting? TryLoad(IClientFiles store)
    {
        try
        {
            var light = new DbcTable(store.Read(@"DBFilesClient/Light.dbc"));
            var lightParams = new DbcTable(store.Read(@"DBFilesClient/LightParams.dbc"));
            var skybox = new DbcTable(store.Read(@"DBFilesClient/LightSkybox.dbc"));
            var intBand = new DbcTable(store.Read(@"DBFilesClient/LightIntBand.dbc"));
            var regions = new List<LightRegion>();
            foreach (uint id in light.Ids)
            {
                double dbcX = light.F(id, 2), dbcY = light.F(id, 3), dbcZ = light.F(id, 4);
                var view = LightCoordinates.ToView(dbcX, dbcY, dbcZ);
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
