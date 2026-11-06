using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Seat-bone sampling against synthetic tracks: the follow math interpolates baked seat
/// matrices with the shader's frame formula, so a rider glued to the seat tracks the mount.</summary>
public class SeatTrackTests
{
    static ModelData OneBoneModel()
    {
        var translation = new ModelTrack<Vector3>(
            [0, 1000], [new Vector3(0, 0, 0), new Vector3(0, 0, 2)],
            1, 0);
        var rotation = new ModelTrack<Quaternion>([], [], 0, 0);
        var scale = new ModelTrack<Vector3>([], [], 0, 0);
        var bone = new ModelBone(-1, Vector3.Zero, translation, rotation, scale);
        var surface = new ModelSurface([], [], [], [], "", 0, false);
        return new([surface], [bone], 1000, [], []);
    }

    [Fact]
    public void SeatFramesSampleTheBoneTrackAtPaletteTimes()
    {
        var model = OneBoneModel();
        float[]? frames = AnimationPalette.SampleBoneFrames(model, 0, 3, 1f, CancellationToken.None);
        Assert.NotNull(frames);
        Assert.Equal(48, frames.Length);
        // Frame 0 at t=0 (no motion), frame 2 at t=1000 (full +2 Z), frame 1 lerps halfway.
        Assert.Equal(0, frames[14], 3);
        Assert.Equal(2, frames[2 * 16 + 14], 3);
        Assert.Equal(1, frames[16 + 14], 3);
    }

    [Fact]
    public void SeatFramesRejectBadInput()
    {
        var model = OneBoneModel();
        Assert.Null(AnimationPalette.SampleBoneFrames(model, 5, 3, 1f, CancellationToken.None));
        Assert.Null(AnimationPalette.SampleBoneFrames(model, -1, 3, 1f, CancellationToken.None));
        Assert.Null(AnimationPalette.SampleBoneFrames(model, 0, 0, 1f, CancellationToken.None));
    }
}
