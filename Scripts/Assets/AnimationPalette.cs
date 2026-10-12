using System.Numerics;
namespace ACWorldEdit.Assets;

public sealed record AnimationPalette(int Width, int Height, byte[] Pixels, float Duration)
{
    public static AnimationPalette Bake(ModelData model, CancellationToken token)
    {
        int bones = Math.Max(1, model.Bones.Length);
        int frames = model.Duration > 0 && model.Bones.Length > 0 ? Math.Clamp((int)(model.Duration / 1000f * 30) + 1, 2, 601) : 1;
        float[] pixels = new float[checked(bones * 16 * frames)];
        for (int frame = 0; frame < frames; frame++)
        {
            token.ThrowIfCancellationRequested(); float time = frames == 1 ? 0 : frame * model.Duration / (float)(frames - 1);
            Matrix4x4[] matrices = new Matrix4x4[bones]; byte[] state = new byte[bones];
            Matrix4x4 Bone(int i)
            {
                if (state[i] == 2) return matrices[i]; if (state[i] == 1) throw new InvalidDataException("Cyclic bone hierarchy."); state[i] = 1;
                var bone = model.Bones[i];
                var translation = Sample(bone.Translation, time, Vector3.Zero, Vector3.Lerp);
                var rotation = Sample(bone.Rotation, time, Quaternion.Identity, Quaternion.Slerp);
                var scale = Sample(bone.Scale, time, Vector3.One, Vector3.Lerp);
                var local = Matrix4x4.CreateTranslation(-bone.Pivot) * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(bone.Pivot + translation);
                matrices[i] = bone.Parent >= 0 ? local * Bone(bone.Parent) : local; state[i] = 2; return matrices[i];
            }
            for (int i = 0; i < bones; i++)
            {
                var m = model.Bones.Length == 0 ? Matrix4x4.Identity : Bone(i); int p = (frame * bones + i) * 16;
                float[] values = [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
                values.CopyTo(pixels, p);
            }
        }
        byte[] bytes = new byte[pixels.Length * 4]; Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return new(bones * 4, frames, bytes, model.Duration / 1000f);
    }
    public static T Sample<T>(ModelTrack<T> track, float time, T fallback, Func<T,T,float,T> interpolate)
    {
        if (track.Times.Length == 0) return fallback;
        if (track.GlobalDuration > 0) time %= track.GlobalDuration;
        int found = Array.BinarySearch(track.Times, (uint)time); if (found >= 0) return track.Values[found];
        int next = ~found; if (next == 0) return track.Values[0]; if (next == track.Times.Length || track.Interpolation == 0) return track.Values[next - 1];
        float span = track.Times[next] - track.Times[next - 1];
        return interpolate(track.Values[next - 1], track.Values[next], span == 0 ? 0 : (time - track.Times[next - 1]) / span);
    }
}
