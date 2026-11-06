using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Sampled native animation packed as RGBA-float texels: four per bone matrix, one row per
/// frame. Supports Stand and selected Walk/Run clips without per-instance CPU skinning.
/// </summary>
public sealed record AnimationPalette(int Width, int Height, byte[] Pixels, float Duration)
{
    /// <summary>
    /// Sample hierarchy transforms on a worker at about 30 Hz, capped at 601 frames. Pivot-relative
    /// matrices compose with parents; cycle detection rejects invalid skeletons.
    /// </summary>
    public static AnimationPalette Bake(ModelData model, CancellationToken token)
    {
        int bones = Math.Max(1, model.Bones.Length);
        int frames = model.Duration > 0 && model.Bones.Length > 0 ? Math.Clamp((int)(model.Duration / 1000f * 30) + 1, 2, 601) : 1;
        float[] pixels = new float[checked(bones * 16 * frames)];
        Matrix4x4[] matrices = new Matrix4x4[bones];
        byte[] state = new byte[bones];
        float time = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            token.ThrowIfCancellationRequested();
            time = frames == 1 ? 0 : frame * model.Duration / (float)(frames - 1);
            Array.Clear(state);
            // 0=unvisited, 1=on recursion stack, 2=complete. Parent order in the native
            // file is not guaranteed, so recursive memoization also detects cycles.
            for (int i = 0; i < bones; i++)
            {
                var m = model.Bones.Length == 0 ? Matrix4x4.Identity : Bone(model, state, matrices, i, time);
                int p = (frame * bones + i) * 16;
                // System.Numerics uses row-vector matrices. The shader loads these rows
                // as GLSL columns, giving the transpose needed for column-vector skinning.
                Span<float> values = pixels.AsSpan(p, 16);
                values[0] = m.M11; values[1] = m.M12; values[2] = m.M13; values[3] = m.M14;
                values[4] = m.M21; values[5] = m.M22; values[6] = m.M23; values[7] = m.M24;
                values[8] = m.M31; values[9] = m.M32; values[10] = m.M33; values[11] = m.M34;
                values[12] = m.M41; values[13] = m.M42; values[14] = m.M43; values[15] = m.M44;
            }
        }

        byte[] bytes = new byte[pixels.Length * 4];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return new(bones * 4, frames, bytes, model.Duration / 1000f);
    }

    /// <summary>
    /// One bone's hierarchy transform at a time: pivot-relative matrices compose with parents,
    /// and cycle detection rejects invalid skeletons. Shared by the palette bake and the mount
    /// seat track so the rider follows the same matrices the shader skins with.
    /// </summary>
    static Matrix4x4 Bone(ModelData model, byte[] state, Matrix4x4[] matrices, int i, float time)
    {
        if (state[i] == 2)
        {
            return matrices[i];
        }

        if (state[i] == 1)
        {
            throw new InvalidDataException("Cyclic bone hierarchy.");
        }

        state[i] = 1;
        var bone = model.Bones[i];
        var translation = Sample(bone.Translation, time, Vector3.Zero, Vector3.Lerp);
        var rotation = Sample(bone.Rotation, time, Quaternion.Identity, Quaternion.Slerp);
        var scale = Sample(bone.Scale, time, Vector3.One, Vector3.Lerp);
        var local = Matrix4x4.CreateTranslation(-bone.Pivot) * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(bone.Pivot + translation);
        matrices[i] = bone.Parent >= 0 ? local * Bone(model, state, matrices, bone.Parent, time) : local;
        state[i] = 2;
        return matrices[i];
    }

    /// <summary>
    /// One bone's world matrix per palette frame, sampled at the same times the bake uses, so a
    /// follower (a rider glued to a mount's seat bone) interpolates them with the shader's frame
    /// formula. Null when the model has no bones or the index is out of range.
    /// </summary>
    public static float[]? SampleBoneFrames(ModelData model, int bone, int frames, float duration, CancellationToken token)
    {
        if (model.Bones.Length == 0 || bone < 0 || bone >= model.Bones.Length || frames <= 0 || duration < 0)
        {
            return null;
        }

        var pixels = new float[frames * 16];
        for (int frame = 0; frame < frames; frame++)
        {
            token.ThrowIfCancellationRequested();
            float time = frames == 1 ? 0 : frame * model.Duration / (float)(frames - 1);
            var m = Bone(model, new byte[model.Bones.Length], new Matrix4x4[model.Bones.Length], bone, time);
            int p = frame * 16;
            pixels[p] = m.M11; pixels[p + 1] = m.M12; pixels[p + 2] = m.M13; pixels[p + 3] = m.M14;
            pixels[p + 4] = m.M21; pixels[p + 5] = m.M22; pixels[p + 6] = m.M23; pixels[p + 7] = m.M24;
            pixels[p + 8] = m.M31; pixels[p + 9] = m.M32; pixels[p + 10] = m.M33; pixels[p + 11] = m.M34;
            pixels[p + 12] = m.M41; pixels[p + 13] = m.M42; pixels[p + 14] = m.M43; pixels[p + 15] = m.M44;
        }

        return pixels;
    }

    /// <summary>
    /// Binary-search surrounding native keys, wrap global loops independently and respect
    /// constant/interpolated tracks. The reader reports spline-to-linear approximation.
    /// </summary>
    public static T Sample<T>(ModelTrack<T> track, float time, T fallback, Func<T, T, float, T> interpolate)
    {
        if (track.Times.Length == 0)
        {
            return fallback;
        }

        if (track.GlobalDuration > 0)
        {
            time %= track.GlobalDuration;
        }

        int found = Array.BinarySearch(track.Times, (uint)time);
        if (found >= 0)
        {
            return track.Values[found];
        }

        int next = ~found;
        if (next == 0)
        {
            return track.Values[0];
        }

        if (next == track.Times.Length || track.Interpolation == 0)
        {
            return track.Values[next - 1];
        }

        float span = track.Times[next] - track.Times[next - 1];
        return interpolate(track.Values[next - 1], track.Values[next], span == 0 ? 0 : (time - track.Times[next - 1]) / span);
    }
}
