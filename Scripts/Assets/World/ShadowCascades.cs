using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// One cascade map's fitted light-space frame. <see cref="LightForward"/> is the direction the sun
/// light travels (the caster camera looks along it), <see cref="LightRight"/> and
/// <see cref="LightUp"/> span the orthographic image, and <see cref="Eye"/> is the orthographic
/// camera's position. <see cref="Radius"/> is the orthographic half-extent (the receiver sphere plus
/// <c>casterPadding</c>), so the visible square spans <c>2 * Radius</c>.
/// <para>
/// <see cref="UvRowX"/> and <see cref="UvRowY"/> map a world position (as <c>vec4(world, 1)</c>) to
/// the map's 0..1 UV. <see cref="SplitAndTexel"/> is (split distance, world size of one texel, map
/// resolution, cascade index) and <see cref="FadeBounds"/> is (fade, fade, 1 - fade, 1 - fade).
/// </para>
/// </summary>
public readonly record struct ShadowCascadeFit(
    Vector3 LightForward,
    Vector3 LightRight,
    Vector3 LightUp,
    Vector3 Eye,
    float Radius,
    float Near,
    float Far,
    float SplitDistance,
    float TexelWorldSize,
    Vector4 UvRowX,
    Vector4 UvRowY,
    Vector4 SplitAndTexel,
    Vector4 FadeBounds);

/// <summary>
/// The retail cascade frame and mask math, kept free of Godot so the xUnit project can verify it.
/// The caller fits one cascade per view-depth span, renders the casters into an orthographic map and
/// hands the receiver the same rows; the shader in <c>Shaders/shared/client_shadow.gdshaderinc</c>
/// mirrors it.
/// </summary>
public static class ShadowCascades
{
    /// <summary>Practical-split blend: 0.75 leans toward logarithmic spacing (the client's own choice).</summary>
    public const float DefaultLambda = 0.75f;

    /// <summary>How much of the outer UV square fades to fully lit, matching the retail mask.</summary>
    public const float FadeFraction = 0.05f;

    /// <summary>Radius steps of 1/8 yard keep tiny camera movements from resizing every frame.</summary>
    public const float RadiusQuantum = 1f / 8f;

    /// <summary>The orthographic caster camera's near plane.</summary>
    public const float CasterNear = 0.01f;

    /// <summary>
    /// Practical split depths, from <paramref name="near"/> to <paramref name="far"/> inclusive. A
    /// count of <c>n</c> returns <c>n + 1</c> boundaries: boundary 0 is the camera near plane, boundary
    /// <c>i</c> is cascade <c>i - 1</c>'s far distance. Fewer than one cascade returns no boundaries.
    /// </summary>
    public static float[] SplitDepths(float near, float far, int count, float lambda = DefaultLambda)
    {
        near = MathF.Max(near, 0.001f);
        if (count < 1 || far <= near)
        {
            return [];
        }

        lambda = Math.Clamp(lambda, 0f, 1f);
        float[] splits = new float[count + 1];
        for (int i = 0; i <= count; i++)
        {
            float t = i / (float)count;
            float logarithmic = near * MathF.Pow(far / near, t);
            float uniform = near + (far - near) * t;
            splits[i] = lambda * logarithmic + (1f - lambda) * uniform;
        }

        splits[0] = near;
        splits[count] = far;
        // Rounding can only tie neighbouring values at absurd near/far ratios; keep them ordered so
        // selection stays deterministic.
        for (int i = 1; i <= count; i++)
        {
            splits[i] = MathF.Max(splits[i], splits[i - 1] + 0.001f);
        }

        splits[count] = far;
        return splits;
    }

    /// <summary>
    /// The light's orthonormal frame: <paramref name="forward"/> is the direction the light travels,
    /// right and up span the image. A near-vertical sun falls back to a Z-up helper so the cross
    /// product never degenerates.
    /// </summary>
    public static (Vector3 Forward, Vector3 Right, Vector3 Up) LightBasis(Vector3 lightDirection)
    {
        Vector3 forward = lightDirection.LengthSquared() < 1e-8f ? Vector3.UnitY : Vector3.Normalize(lightDirection);
        Vector3 helper = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, helper));
        Vector3 up = Vector3.Cross(right, forward);
        return (forward, right, up);
    }

    /// <summary>
    /// The eight world-space corners of a camera frustum slice. <paramref name="fov"/> is the vertical
    /// field of view in radians; the camera looks along its local -Z, as Godot's does.
    /// </summary>
    public static Vector3[] FrustumCorners(Vector3 position, Quaternion rotation, float fov, float aspect, float near, float far)
    {
        near = MathF.Max(near, 0.001f);
        far = MathF.Max(far, near + 0.001f);
        float tan = MathF.Tan(Math.Clamp(fov, 0.01f, MathF.PI - 0.01f) * 0.5f);
        float nearHeight = tan * near, nearWidth = nearHeight * aspect;
        float farHeight = tan * far, farWidth = farHeight * aspect;
        Vector3[] local =
        [
            new(-nearWidth, -nearHeight, -near), new(nearWidth, -nearHeight, -near),
            new(-nearWidth, nearHeight, -near), new(nearWidth, nearHeight, -near),
            new(-farWidth, -farHeight, -far), new(farWidth, -farHeight, -far),
            new(-farWidth, farHeight, -far), new(farWidth, farHeight, -far),
        ];
        for (int i = 0; i < local.Length; i++)
        {
            local[i] = position + Vector3.Transform(local[i], rotation);
        }

        return local;
    }

    /// <summary>
    /// Fit one cascade around the camera frustum slice from <paramref name="farStart"/> to
    /// <paramref name="farEnd"/>. <paramref name="casterPadding"/> extends the orthographic box toward
    /// the sun so tall buildings in front of the receiver still cast; <paramref name="index"/> is the
    /// cascade's own number, stored with the split for the data texture.
    /// </summary>
    public static ShadowCascadeFit FitCascade(
        Vector3 lightDirection,
        Vector3 cameraPosition,
        Quaternion cameraRotation,
        float fov,
        float aspect,
        float near,
        float farStart,
        float farEnd,
        int resolution,
        float casterPadding,
        float fadeFraction = FadeFraction,
        int index = 0)
    {
        var corners = FrustumCorners(cameraPosition, cameraRotation, fov, aspect, MathF.Max(farStart, near), farEnd);
        Vector3 center = Vector3.Zero;
        foreach (var corner in corners)
        {
            center += corner;
        }

        center /= corners.Length;
        float corpusRadius = 0f;
        foreach (var corner in corners)
        {
            corpusRadius = MathF.Max(corpusRadius, Vector3.Distance(corner, center));
        }

        // Quantize upward so the map keeps its size (and texel grid) across sub-quantum camera motion.
        corpusRadius = MathF.Ceiling(MathF.Max(corpusRadius, 0f) / RadiusQuantum) * RadiusQuantum;
        float radius = MathF.Max(corpusRadius + MathF.Max(casterPadding, 0f), RadiusQuantum);
        var (forward, right, up) = LightBasis(lightDirection);
        float texel = 2f * radius / MathF.Max(resolution, 1);
        float fade = Math.Clamp(fadeFraction, 0f, 0.45f);
        return new(
            forward,
            right,
            up,
            center - forward * radius,
            radius,
            CasterNear,
            2f * radius,
            farEnd,
            texel,
            UvRow(right, center, radius),
            // Godot textures put v = 0 at the top, while the light's up axis increases upward, so the
            // map's vertical axis runs the other way. Flipping it here keeps the shader's dot product
            // and the texture's sampling convention in agreement (verified by the caster smoke probe).
            TextureRow(up, center, radius),
            new(farEnd, texel, resolution, index),
            new(fade, fade, 1f - fade, 1f - fade));
    }

    /// <summary>
    /// Move the fit's center along the light's right and up axes to the nearest whole texel, leaving
    /// its sun-space depth alone. The rows and eye are rewritten; everything else is unchanged.
    /// Snapping keeps a stationary receiver stable while the camera drifts by less than one texel,
    /// which otherwise makes shadow edges crawl.
    /// </summary>
    public static ShadowCascadeFit SnapCenter(in ShadowCascadeFit fit, int resolution)
    {
        if (resolution <= 0 || fit.Radius <= 0f)
        {
            return fit;
        }

        float texel = 2f * fit.Radius / resolution;
        Vector3 center = fit.Eye + fit.LightForward * fit.Radius;
        float right = MathF.Round(Vector3.Dot(center, fit.LightRight) / texel) * texel;
        float up = MathF.Round(Vector3.Dot(center, fit.LightUp) / texel) * texel;
        float forward = Vector3.Dot(center, fit.LightForward);
        Vector3 snapped = fit.LightRight * right + fit.LightUp * up + fit.LightForward * forward;
        return fit with
        {
            Eye = snapped - fit.LightForward * fit.Radius,
            UvRowX = UvRow(fit.LightRight, snapped, fit.Radius),
            UvRowY = TextureRow(fit.LightUp, snapped, fit.Radius),
        };
    }

    /// <summary>Map a world position to the fitted map's 0..1 UV.</summary>
    public static Vector2 WorldToUv(in ShadowCascadeFit fit, Vector3 world)
    {
        var point = new Vector4(world, 1f);
        return new(Vector4.Dot(fit.UvRowX, point), Vector4.Dot(fit.UvRowY, point));
    }

    /// <summary>
    /// The first cascade whose far distance covers <paramref name="viewDepth"/>; the last cascade
    /// covers everything beyond. An empty list selects 0.
    /// </summary>
    public static int SelectCascade(float viewDepth, IReadOnlyList<float> cascadeFarDistances)
    {
        for (int i = 0; i < cascadeFarDistances.Count; i++)
        {
            if (viewDepth <= cascadeFarDistances[i])
            {
                return i;
            }
        }

        return Math.Max(cascadeFarDistances.Count - 1, 0);
    }

    /// <summary>
    /// How much of the next cascade to blend in over the last <paramref name="blendStart"/> fraction
    /// of a split (0 = this cascade only, 1 = next cascade only). Retail blends the last 5 %.
    /// </summary>
    public static float BlendWeight(float viewDepth, float split, float blendStart = 0.95f)
    {
        if (split <= 0f)
        {
            return 0f;
        }

        float start = split * Math.Clamp(blendStart, 0f, 1f);
        return Math.Clamp((viewDepth - start) / MathF.Max(split - start, 1e-4f), 0f, 1f);
    }

    /// <summary>
    /// The retail edge fade: the smallest distance to any faded UV edge, times 10, clamped to 1. A
    /// fragment in the middle of the map stays lit; one outside the square is fully lit (no shadow)
    /// rather than shadowed by a cascade that does not cover it.
    /// </summary>
    public static float EdgeFade(Vector2 uv, Vector4 fadeBounds)
    {
        float term = MathF.Min(
            MathF.Min(Saturate(uv.X - fadeBounds.X), Saturate(fadeBounds.Z - uv.X)),
            MathF.Min(Saturate(uv.Y - fadeBounds.Y), Saturate(fadeBounds.W - uv.Y)));
        return MathF.Min(term * 10f, 1f);
    }

    static Vector4 UvRow(Vector3 axis, Vector3 center, float radius)
    {
        float scale = 1f / (2f * radius);
        return new(axis.X * scale, axis.Y * scale, axis.Z * scale, 0.5f - Vector3.Dot(center, axis) * scale);
    }

    /// <summary>The vertical row, flipped to Godot's top-left texture origin.</summary>
    static Vector4 TextureRow(Vector3 up, Vector3 center, float radius)
    {
        float scale = 1f / (2f * radius);
        return new(-up.X * scale, -up.Y * scale, -up.Z * scale, 0.5f + Vector3.Dot(center, up) * scale);
    }

    static float Saturate(float value) => Math.Clamp(value, 0f, 1f);
}
