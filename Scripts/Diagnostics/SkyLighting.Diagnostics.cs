using Godot;

namespace ACWorldEdit;

/// <summary>The lighting and atmosphere half of the F12 dump: a snapshot for the report and a flat mode for the second screenshot.</summary>
public sealed partial class SkyLighting
{
    (float Sun, Color Ambient, float AmbientEnergy, bool Fog, Godot.Environment.ToneMapper Tonemap)? saved;

    static float[] Rgb(Color c) => [MathF.Round(c.R, 3), MathF.Round(c.G, 3), MathF.Round(c.B, 3)];

    /// <summary>The lighting values in effect this frame, for the diagnostic report.</summary>
    public object Describe()
    {
        var env = environment.Environment;
        return new
        {
            timeOfDayHours = Math.Round(TimeOfDayHours, 2),
            interior,
            hasLightTable = table != null,
            mapId,
            flat = saved != null,
            sun = new { energy = MathF.Round(sun.LightEnergy, 3), color = Rgb(sun.LightColor), rotationDegrees = new[] { sun.RotationDegrees.X, sun.RotationDegrees.Y, sun.RotationDegrees.Z }, shadows = sun.ShadowEnabled },
            ambient = new { source = env.AmbientLightSource.ToString(), color = Rgb(env.AmbientLightColor), energy = MathF.Round(env.AmbientLightEnergy, 3) },
            fog = new { enabled = clientFog.W > 0.5f, startYards = MathF.Round(clientFog.X, 1), endYards = MathF.Round(clientFog.Y, 1), viewDistance = ViewDistance },
            background = new { mode = env.BackgroundMode.ToString(), color = Rgb(env.BackgroundColor), skyTop = Rgb(skyTopColor), skyHorizon = Rgb(skyHorizonColor) },
            tonemap = env.TonemapMode.ToString(),
            skybox = skyboxPath,
            skyboxReady = SkyboxReady,
        };
    }

    /// <summary>The flat values; <see cref="Update"/> calls this last each frame because it rewrites sun, ambient and fog.</summary>
    void ApplyFlat()
    {
        if (saved == null)
        {
            return;
        }

        var env = environment.Environment;
        sun.LightEnergy = 0;
        env.AmbientLightColor = Colors.White;
        env.AmbientLightEnergy = 1;
        env.FogEnabled = false;
        ClientShaderGlobals.Set(ClientShaderGlobals.Fog, new Vector4(0, 1, 1, 0));
        env.TonemapMode = Godot.Environment.ToneMapper.Linear;
    }

    /// <summary>
    /// Diagnostic mode for the second F12 screenshot: no sun, fog or tone mapping and a white ambient, so
    /// lit surfaces show their raw texture and vertex colour. Unlit batches (baked interiors) are unchanged.
    /// Setting it false restores what was in effect.
    /// </summary>
    public bool Flat
    {
        get => saved != null;
        set
        {
            var env = environment.Environment;
            if (value && saved == null)
            {
                saved = (sun.LightEnergy, env.AmbientLightColor, env.AmbientLightEnergy, env.FogEnabled, env.TonemapMode);
                ApplyFlat();
            }
            else if (!value && saved is { } was)
            {
                sun.LightEnergy = was.Sun;
                env.AmbientLightColor = was.Ambient;
                env.AmbientLightEnergy = was.AmbientEnergy;
                env.FogEnabled = was.Fog;
                env.TonemapMode = was.Tonemap;
                saved = null;
            }
        }
    }
}
