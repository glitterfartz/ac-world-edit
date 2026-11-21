using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// One batch's baked appearance. Static values travel as uniforms. Animated channels use the
/// shared palette: column <c>Column</c> holds RGBA color (alpha already multiplied by texture
/// weight); <c>Column + 1</c>/<c>Column + 2</c> hold the UV affine rows (u' = dot(row0, (u, v, 1))).
/// <c>Column</c> is -1 when nothing animates. Hidden batches are never uploaded.
/// </summary>
public readonly record struct SurfaceMaterial(int Column, int ColorFrames, float ColorPeriod, int UvFrames, float UvPeriod, Vector4 Color, Vector3 UvRow0, Vector3 UvRow1, bool Hidden)
{
    public static readonly SurfaceMaterial Default = new(-1, 1, 0, 1, 0, Vector4.One, Vector3.UnitX, Vector3.UnitY, false);
}

/// <summary>
/// Per-variant RGBA-float texture of sampled batch color/UV animation, packed like
/// <see cref="AnimationPalette"/> (one row per frame). Every instance shares the shader clock,
/// so global-sequence effects (portal swirls, flame scrolling, flicker) stay in step.
/// </summary>
public sealed record MaterialPalette(int Width, int Height, byte[] Pixels, SurfaceMaterial[] Surfaces)
{
    /// <summary>
    /// Sample each animated channel over its own loop at about 30 Hz (capped at 601 frames). A
    /// channel's loop is the longest of its tracks: the clip duration for clip-bound tracks, the
    /// global-sequence duration otherwise. Tracks with different loop lengths in one channel
    /// can seam when the shorter loop does not divide the longer one.
    /// </summary>
    public static MaterialPalette Bake(ModelData model, CancellationToken token)
    {
        var surfaces = new SurfaceMaterial[model.Surfaces.Length];
        List<(int Column, int ColorFrames, float ColorPeriod, int UvFrames, float UvPeriod, SurfaceAnimation Tracks)> animated = [];
        for (int i = 0; i < surfaces.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var tracks = model.Surfaces[i].Material;
            if (tracks == null)
            {
                surfaces[i] = SurfaceMaterial.Default;
                continue;
            }

            float colorPeriod = Period(model.Duration, tracks.Color, tracks.Alpha, tracks.Weight);
            float uvPeriod = Period(model.Duration, tracks.UvTranslation, tracks.UvRotation, tracks.UvScale);
            var color = Color(tracks, 0, model.Duration);
            var (row0, row1) = Uv(tracks, 0, model.Duration);
            int colorFrames = Frames(colorPeriod), uvFrames = Frames(uvPeriod);
            // The client skips a batch whose combined alpha is zero; only a static zero is safe
            // to drop at upload, an animated one fades in the shader.
            bool hidden = colorFrames == 1 && color.W <= 1 / 255f;
            int column = colorFrames > 1 || uvFrames > 1 ? animated.Count * 3 : -1;
            surfaces[i] = new(column, colorFrames, colorPeriod / 1000, uvFrames, uvPeriod / 1000, color, row0, row1, hidden);
            if (column >= 0)
            {
                animated.Add((column, colorFrames, colorPeriod, uvFrames, uvPeriod, tracks));
            }
        }

        int width = Math.Max(1, animated.Count * 3);
        int height = Math.Max(1, animated.Count == 0 ? 1 : animated.Max(a => Math.Max(a.ColorFrames, a.UvFrames)));
        float[] pixels = new float[checked(width * height * 4)];
        foreach (var a in animated)
        {
            token.ThrowIfCancellationRequested();
            void Put(int column, int row, Vector4 value)
            {
                int p = (row * width + column) * 4;
                pixels[p] = value.X;
                pixels[p + 1] = value.Y;
                pixels[p + 2] = value.Z;
                pixels[p + 3] = value.W;
            }

            for (int row = 0; row < a.ColorFrames; row++)
            {
                float time = a.ColorFrames == 1 ? 0 : row * a.ColorPeriod / (a.ColorFrames - 1);
                Put(a.Column, row, Color(a.Tracks, time, model.Duration));
            }

            for (int row = 0; row < a.UvFrames; row++)
            {
                float time = a.UvFrames == 1 ? 0 : row * a.UvPeriod / (a.UvFrames - 1);
                var (row0, row1) = Uv(a.Tracks, time, model.Duration);
                Put(a.Column + 1, row, new(row0, 0));
                Put(a.Column + 2, row, new(row1, 0));
            }
        }

        byte[] bytes = new byte[pixels.Length * 4];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return new(width, height, bytes, surfaces);
    }

    static int Frames(float period) => period > 0 ? Math.Clamp((int)(period / 1000 * 30) + 1, 2, 601) : 1;

    /// <summary>
    /// Loop length in milliseconds of the tracks that actually change; zero means static. A
    /// clip-bound track loops with the clip, a global track with its global sequence.
    /// </summary>
    static float Period(uint clip, params object[] tracks)
    {
        float period = 0;
        foreach (var track in tracks)
        {
            var (times, global, duration) = track switch
            {
                ModelTrack<Vector3> t => (t.Times, t.Global, t.GlobalDuration),
                ModelTrack<Quaternion> t => (t.Times, t.Global, t.GlobalDuration),
                ModelTrack<float> t => (t.Times, t.Global, t.GlobalDuration),
                _ => throw new ArgumentException("Unsupported track type.")
            };
            if (times.Length > 1)
            {
                period = Math.Max(period, global ? duration : clip);
            }
        }

        return period;
    }

    /// <summary>
    /// Time within a track's own loop. A global sequence of length zero holds its first key; a
    /// clip-bound track wraps on the clip when a longer global loop drives the channel.
    /// </summary>
    static float Local<T>(ModelTrack<T> track, float time, uint clip) => track.Global ? (track.GlobalDuration > 0 ? time % track.GlobalDuration : 0) : (clip > 0 ? time % clip : 0);

    static Vector4 Color(SurfaceAnimation t, float time, uint clip)
    {
        var rgb = AnimationPalette.Sample(t.Color, Local(t.Color, time, clip), Vector3.One, Vector3.Lerp);
        float alpha = AnimationPalette.Sample(t.Alpha, Local(t.Alpha, time, clip), 1f, float.Lerp);
        float weight = AnimationPalette.Sample(t.Weight, Local(t.Weight, time, clip), 1f, float.Lerp);
        return new(rgb, Math.Clamp(alpha * weight, 0, 1));
    }

    /// <summary>
    /// Client texture matrix: translate, then rotate and scale about the texture center (0.5,
    /// 0.5). Recover the 2D affine rows by mapping the UV origin and unit axes.
    /// </summary>
    static (Vector3 Row0, Vector3 Row1) Uv(SurfaceAnimation t, float time, uint clip)
    {
        var translation = AnimationPalette.Sample(t.UvTranslation, Local(t.UvTranslation, time, clip), Vector3.Zero, Vector3.Lerp);
        var rotation = AnimationPalette.Sample(t.UvRotation, Local(t.UvRotation, time, clip), Quaternion.Identity, Quaternion.Slerp);
        var scale = AnimationPalette.Sample(t.UvScale, Local(t.UvScale, time, clip), Vector3.One, Vector3.Lerp);
        rotation = rotation.LengthSquared() > 1e-6f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
        var center = new Vector3(0.5f, 0.5f, 0);
        var matrix = Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(center + translation);
        var origin = Vector3.Transform(Vector3.Zero, matrix);
        var u = Vector3.Transform(Vector3.UnitX, matrix) - origin;
        var v = Vector3.Transform(Vector3.UnitY, matrix) - origin;
        return (new(u.X, v.X, origin.X), new(u.Y, v.Y, origin.Y));
    }
}
