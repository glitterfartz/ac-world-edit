using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>The M2 MountMain seat (attachment ID 0 through the lookup) against the real client:
/// the bone the rider hangs off and the seat position in that bone's space. The client glues the
/// rider's root to boneMatrix * position, so bone-relative composition puts the saddle on the
/// back (a Wyvern's bone-relative seat rides at shoulder height; the raw position alone would
/// bury the rider in the torso).</summary>
public class M2SeatTests
{
    [ClientDataFact]
    public void MountModelsCarryASeatOnABone()
    {
        var store = ClientData.Store;
        var horse = M2Model.Read(store, "Creature/Horse/Horse.m2").Seat;
        Assert.NotNull(horse);
        Assert.Equal(56, horse.Bone);
        // Back height 1.87 in viewer space (matches CreatureModelData.mountHeight: the seat bone
        // is unmoved at Stand frame 0, so bone-relative and model-space agree here).
        Assert.True(Math.Abs(horse.Position.Y - 1.87f) < 0.02f, $"horse seat height is {horse.Position.Y}");
        var kodo = M2Model.Read(store, "Creature/KodoBeast/KodoBeast.m2").Seat;
        Assert.NotNull(kodo);
        Assert.True(Math.Abs(kodo.Position.Y - 3.54f) < 0.02f, $"kodo seat height is {kodo.Position.Y}");
    }

    [ClientDataFact]
    public void NonMountModelsHaveNoSeat()
    {
        var store = ClientData.Store;
        Assert.Null(M2Model.Read(store, "Creature/Basilisk/Basilisk.m2").Seat);
        Assert.Null(M2Model.Read(store, "Creature/KodoBeast/KodoBeastTame.m2").Seat);
    }
}
