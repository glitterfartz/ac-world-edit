using ACWorldEdit;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The spawn inspector uses a named control for each kind of value instead of a raw number box.</summary>
[TestSuite]
[RequireGodotRuntime]
public class SpawnInspectorTests
{
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    static SpawnInspector Build()
    {
        var inspector = new SpawnInspector
        {
            Choices = field => field.Column == "emote" ? [(0, "None"), (69, "Wave")] : null,
            FlagBits = field => SpawnFieldPickers.Flags(WorldDialect.TrinityCore, field),
            Describe = SpawnFieldPickers.Describe
        };
        var row = SpawnEdits.NewCreature(TcSchema.Instance, 5, 448, Here, 0, 0);
        var addon = SpawnEdits.NewAddon(TcSchema.Instance, SpawnKind.Creature, 5);
        inspector.Show(WorldDialect.TrinityCore, SpawnKind.Creature, row, addon, null, null, true);
        return inspector;
    }

    [TestCase]
    public void RawFieldsBecomeSpinnersDropdownsAndCheckboxPopups()
    {
        var inspector = Build();
        AutoFree(inspector);

        AssertThat(inspector.Field("wander_distance")!.Value.Editor).IsInstanceOf<SpinBox>();
        AssertThat(inspector.Field("emote")!.Value.Editor).IsInstanceOf<OptionButton>();
        AssertThat(inspector.Field("npcflag")!.Value.Editor).IsInstanceOf<MenuButton>();
        AssertThat(inspector.Field("unit_flags")!.Value.Editor).IsInstanceOf<MenuButton>();
        AssertThat(inspector.Field("unit_flags2")!.Value.Editor).IsInstanceOf<MenuButton>();
        AssertThat(inspector.Field("unit_flags3")!.Value.Editor).IsInstanceOf<MenuButton>();
        AssertThat(inspector.Field("spawnDifficulties")!.Value.Editor).IsInstanceOf<MenuButton>();
    }

    [TestCase]
    public void TheRighthandEmoteAndFlagChoicesReuseTheTemplateNames()
    {
        var inspector = Build();
        AutoFree(inspector);

        var npc = (MenuButton)inspector.Field("npcflag")!.Value.Editor;
        AssertThat(npc.GetPopup().ItemCount).IsGreater(20);
        var unit = (MenuButton)inspector.Field("unit_flags")!.Value.Editor;
        AssertThat(unit.GetPopup().ItemCount).IsGreater(10);
        var emote = (OptionButton)inspector.Field("emote")!.Value.Editor;
        AssertThat(emote.ItemCount).IsEqual(2);
    }

    [TestCase]
    public void EveryFlagPopupRowCarriesItsDescriptionAsATooltip()
    {
        var inspector = Build();
        AutoFree(inspector);

        foreach (var column in new[] { "npcflag", "unit_flags", "unit_flags2", "unit_flags3" })
        {
            var popup = ((MenuButton)inspector.Field(column)!.Value.Editor).GetPopup();
            AssertThat(popup.ItemCount).IsGreater(3);
            for (int i = 0; i < popup.ItemCount; i++)
            {
                AssertArray(popup.GetItemTooltip(i) ?? "").IsNotEmpty();
                AssertThat(popup.GetItemText(i)).IsNotEmpty();
            }
        }
    }
}
