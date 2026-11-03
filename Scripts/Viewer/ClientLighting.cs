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
/// The four LightParams water alphas: shallow and deep for rivers/lakes and for the ocean (the
/// depth gradient runs from the shallow to the deep value).
/// </summary>
public readonly record struct WaterAlphas(float RiverShallow, float RiverDeep, float OceanShallow, float OceanDeep)
{
    /// <summary>
    /// The client's values when a map has no lights at all (DayNight::SetColors).
    /// </summary>
    public static WaterAlphas Default { get; } = new(0.5f, 1f, 0.75f, 1f);
}

/// <summary>
/// The four LightIntBand water colors: shallow ("close") and deep ("far") for the ocean and for
/// rivers/lakes.
/// </summary>
public sealed record WaterColors(LightBand OceanShallow, LightBand OceanDeep, LightBand RiverShallow, LightBand RiverDeep);

/// <summary>
/// The three sky bands between the zenith and the horizon (LightIntBand 3 to 5) and whether the light's
/// sky takes the sunrise/sunset highlight. Together with SkyTop, SkyHorizon and Fog they are the six
/// colors of the sky dome's rings.
/// </summary>
public sealed record SkyBands(LightBand Middle, LightBand Band1, LightBand Band2, bool Highlight);

/// <summary>
/// The retail light data's fog terms that the 3.3.5a tables do not have: the density the fog's
/// extinction is scaled by (FogDensity, 4.5 on the classic lights), the color the fog turns toward with
/// distance (EndFogColor over EndFogColorDistance yards), and the sun scattering color and strength (zero on the
/// classic lights).
/// </summary>
public sealed record RetailFog(LightFloatBand Density, LightBand EndColor, LightFloatBand EndColorDistance, FogHeightLayer Height, LightBand SunColor, LightFloatBand SunStrength);

/// <summary>
/// Retail's height fog: below the plane at FogHeight the fog takes the layer's density and colors, above it the
/// main ones, with a cubic (Coefficients, highest power first) over the distance above the plane (scaled by
/// Scaler) shaping the transition. All-zero coefficients, as on the classic lights, leave the main fog everywhere.
/// </summary>
public sealed record FogHeightLayer(LightFloatBand Plane, LightFloatBand Scaler, LightFloatBand Density, LightFloatBand[] Coefficients, LightBand Color, LightBand EndColor);

/// <summary>
/// The light's cloud terms: the density that thresholds the cloud noise (LightFloatBand 3, retail CloudDensity) and
/// the three colors the clouds are lit with (LightIntBand 10 to 12, retail CloudSunColor, CloudEmissiveColor and
/// CloudLayer1AmbientColor): sunlit, shaded and base.
/// </summary>
/// <summary>
/// A light's sun shafts (LightShaft rows linked through LightParamsLightShaft to the light's time-of-day samples): the
/// shaft color, its radius (32 or 64) and its intensity. Absent on most lights.
/// </summary>
public sealed record ShaftBands(LightBand Color, LightFloatBand Radius, LightFloatBand Intensity);

public sealed record CloudBands(LightFloatBand Density, LightBand Sun, LightBand Shade, LightBand Base);

/// <summary>
/// One LightParams entry's resolved skybox and relevant time-of-day bands. Only the Default
/// weather/time slot is read; dungeon/underwater/combat/death overrides and bands 8-13 (shadow
/// opacity and sun/cloud colors) are unused future fidelity work. Water is null when the client
/// tables carry no water bands.
/// </summary>
public sealed record LightProfile(uint SkyboxId, LightBand Diffuse, LightBand Ambient, LightBand SkyTop, LightBand SkyHorizon, LightBand Fog, FogCurve? FogDistance = null, WaterColors? Water = null, WaterAlphas? WaterAlpha = null, LightBand? SunColor = null, SkyBands? Sky = null, RetailFog? RetailFog = null, CloudBands? Clouds = null, ShaftBands? Shafts = null)
{
    /// <summary>
    /// Fog end (yards) and start multiplier at a tick; null when the light defines no usable distance.
    /// </summary>
    public (float End, float StartMul)? FogAt(double tick) => FogDistance?.At(tick);
}

/// <summary>
/// One Light.dbc region: a falloff sphere referencing a Default-slot LightParams profile, or the map's
/// default light, which the client identifies by its coordinates being exactly (0, 0, 0) (DayNight::LightLoad), not by
/// its falloff. Position is pre-converted to the viewer's Y-up basis.
/// </summary>
public sealed record LightRegion(uint Id, uint ContinentId, Vector3 ViewPosition, float FalloffStart, float FalloffEnd, uint ParamsId, bool IsDefault = false);

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
            Dictionary<uint, WaterAlphas> paramWater = [];
            Dictionary<uint, bool> paramHighlight = [];
            foreach (var row in tables.Table("LightParams").Values)
            {
                paramSkybox[(uint)row.ID] = Convert.ToUInt32(row["LightSkyboxID"]);
                paramHighlight[(uint)row.ID] = Convert.ToUInt32(row["HighlightSky"]) != 0;
                paramWater[(uint)row.ID] = new(Convert.ToSingle(row["WaterShallowAlpha"]), Convert.ToSingle(row["WaterDeepAlpha"]),
                    Convert.ToSingle(row["OceanShallowAlpha"]), Convert.ToSingle(row["OceanDeepAlpha"]));
            }

            // Each sample of a light can name a LightShaft row; they are optional and only newer builds have them.
            Dictionary<uint, DBCD.DBCDRow> shaftRows = [];
            Dictionary<uint, uint> shaftOfSample = [];
            try
            {
                foreach (var row in tables.Table("LightShaft").Values)
                {
                    shaftRows[(uint)row.ID] = row;
                }

                foreach (var row in tables.Table("LightParamsLightShaft").Values)
                {
                    shaftOfSample[Convert.ToUInt32(row["LightDataID"])] = Convert.ToUInt32(row["LightShaftID"]);
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException)
            {
                shaftRows.Clear();
                shaftOfSample.Clear();
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
                    Convert.ToSingle(row["GameFalloffStart"]), Convert.ToSingle(row["GameFalloffEnd"]), paramsId, coords[0] == 0 && coords[1] == 0 && coords[2] == 0));
            }

            LightBand Band(List<DBCD.DBCDRow> rows, string column) =>
                LightBand.FromSamples(rows.Select(r => (Convert.ToUInt32(r["Time"]), unchecked((uint)Convert.ToInt64(r[column])))));

            LightFloatBand FloatBand(List<DBCD.DBCDRow> rows, Func<DBCD.DBCDRow, float> value) =>
                LightFloatBand.FromSamples(rows.Select(r => (Convert.ToUInt32(r["Time"]), value(r))));

            Dictionary<uint, LightProfile> profiles = [];
            foreach (uint paramsId in regions.Select(r => r.ParamsId).Distinct())
            {
                if (!samples.TryGetValue(paramsId, out var rows))
                {
                    continue;
                }

                var shaftSamples = rows.Where(r => shaftOfSample.TryGetValue((uint)r.ID, out uint shaft) && shaftRows.ContainsKey(shaft))
                    .Select(r => (Time: Convert.ToUInt32(r["Time"]), Shaft: shaftRows[shaftOfSample[(uint)r.ID]])).ToList();
                var shafts = shaftSamples.Count == 0 ? null : new ShaftBands(
                    LightBand.FromSamples(shaftSamples.Select(x => (x.Time, unchecked((uint)Convert.ToInt64(x.Shaft["Color"]))))),
                    LightFloatBand.FromSamples(shaftSamples.Select(x => (x.Time, Convert.ToSingle(x.Shaft["Field_11_0_0_54210_001"])))),
                    LightFloatBand.FromSamples(shaftSamples.Select(x => (x.Time, Convert.ToSingle(x.Shaft["Field_11_0_0_54210_002"])))));
                profiles[paramsId] = new(
                    paramSkybox.GetValueOrDefault(paramsId),
                    Band(rows, "DirectColor"),
                    Band(rows, "AmbientColor"),
                    Band(rows, "SkyTopColor"),
                    Band(rows, "SkySmogColor"),
                    Band(rows, "SkyFogColor"),
                    FogCurve.FromSamples(rows.Select(r => (Convert.ToUInt32(r["Time"]), Convert.ToSingle(r["FogEnd"]), Convert.ToSingle(r["FogScaler"])))),
                    new(Band(rows, "OceanCloseColor"), Band(rows, "OceanFarColor"), Band(rows, "RiverCloseColor"), Band(rows, "RiverFarColor")),
                    paramWater.GetValueOrDefault(paramsId, WaterAlphas.Default),
                    Band(rows, "SunColor"),
                    new(Band(rows, "SkyMiddleColor"), Band(rows, "SkyBand1Color"), Band(rows, "SkyBand2Color"), paramHighlight.GetValueOrDefault(paramsId)),
                    new(
                        LightFloatBand.FromSamples(rows.Select(r => (Convert.ToUInt32(r["Time"]), Convert.ToSingle(r["FogDensity"])))),
                        Band(rows, "EndFogColor"),
                        LightFloatBand.FromSamples(rows.Select(r => (Convert.ToUInt32(r["Time"]), Convert.ToSingle(r["EndFogColorDistance"])))),
                        new(
                            FloatBand(rows, r => Convert.ToSingle(r["FogHeight"])),
                            FloatBand(rows, r => Convert.ToSingle(r["FogHeightScaler"])),
                            FloatBand(rows, r => Convert.ToSingle(r["FogHeightDensity"])),
                            [.. Enumerable.Range(0, 4).Select(i => FloatBand(rows, r => Convert.ToSingle(((Array)r["FogHeightCoefficients"]).GetValue(i))))],
                            Band(rows, "FogHeightColor"),
                            Band(rows, "EndFogHeightColor")),
                        Band(rows, "SunFogColor"),
                        FloatBand(rows, r => Convert.ToSingle(r["SunFogStrength"]))),
                    new(FloatBand(rows, r => Convert.ToSingle(r["CloudDensity"])), Band(rows, "CloudSunColor"), Band(rows, "CloudEmissiveColor"), Band(rows, "CloudLayer1AmbientColor")),
                    shafts);
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
            var floatBand = new DbcTable(store.Read(@"DBFilesClient/LightFloatBand.dbc"));
            var regions = new List<LightRegion>();
            foreach (uint id in light.Ids)
            {
                double dbcX = light.F(id, 2), dbcY = light.F(id, 3), dbcZ = light.F(id, 4);
                var view = LightCoordinates.ToView(dbcX, dbcY, dbcZ);
                uint paramsId = light.U(id, 7);
                regions.Add(new(id, light.U(id, 1), new((float)view.X, (float)view.Y, (float)view.Z), light.F(id, 5) / 36f, light.F(id, 6) / 36f, paramsId, dbcX == 0 && dbcY == 0 && dbcZ == 0));
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
                int position = lightParams.Position(paramsId), baseRow = position * 18;
                // LightFloatBand holds 6 rows per entry: band 0 is the fog end, band 1 its start multiplier.
                int floatRow = position * 6;
                profiles[paramsId] = new(
                    skyboxId,
                    LightBand.Read(intBand, baseRow + 0),
                    LightBand.Read(intBand, baseRow + 1),
                    LightBand.Read(intBand, baseRow + 2),
                    LightBand.Read(intBand, baseRow + 6),
                    LightBand.Read(intBand, baseRow + 7),
                    FogCurve.FromBands(LightFloatBand.Read(floatBand, floatRow), LightFloatBand.Read(floatBand, floatRow + 1)),
                    new(LightBand.Read(intBand, baseRow + 14), LightBand.Read(intBand, baseRow + 15), LightBand.Read(intBand, baseRow + 16), LightBand.Read(intBand, baseRow + 17)),
                    new(lightParams.F(paramsId, 5), lightParams.F(paramsId, 6), lightParams.F(paramsId, 7), lightParams.F(paramsId, 8)),
                    LightBand.Read(intBand, baseRow + 9),
                    new(LightBand.Read(intBand, baseRow + 3), LightBand.Read(intBand, baseRow + 4), LightBand.Read(intBand, baseRow + 5), lightParams.U(paramsId, 1) != 0),
                    null,
                    new(LightFloatBand.Read(floatBand, floatRow + 3), LightBand.Read(intBand, baseRow + 10), LightBand.Read(intBand, baseRow + 11), LightBand.Read(intBand, baseRow + 12)));
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
