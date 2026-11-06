using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.GameObject;

/// <summary>
/// Pins the generated GameObject catalogues without a database, so a header or generator change
/// cannot silently drop a type, reorder a field or turn a reference into a plain number.
/// </summary>
public class GameObjectTypeCatalogTests
{
    static readonly HashSet<string> References =
    [
        "Achievement", "AnimationData", "AnimKit", "Area", "AreaTrigger", "BroadcastText", "CameraMode",
        "CharShipmentContainer", "ChestProperties", "Cinematic", "Creature", "CreatureDisplay", "Currency",
        "CustomItemBonusFilter", "DestructibleHitpoint", "DestructibleModelData", "DungeonEncounter", "ExtendedCost",
        "Faction", "FactionTemplate", "GameEvent", "GameObject", "GameObjectDiffAnim", "GameObjectDisplay", "GarrPlotInstance",
        "GarrTalentTree", "Gossip", "Item", "Language", "Lock", "Loot", "Map", "MawPowerFilter", "MultiProperties",
        "PageText", "PageTextMaterial", "PlayerChoice", "PlayerCondition", "PlayerDataFlag", "Quest", "QuestGiver",
        "SiegeableProperties", "SkillLine", "Spell", "SpellFocusObject", "SpellScript", "SpellVisual", "TaxiPath",
        "TransportPhysics", "TrophyInstance", "TrophyType", "UiItemInteraction", "UiMap", "UiTextureAtlasMember",
        "Vignette", "WorldState", "WorldStateExpression"
    ];

    [Fact]
    public void AzerothCoreHasEveryTypeAndNoDuplicates()
    {
        var catalog = GameObjectTypeCatalog.Ac;
        Assert.Equal(36, catalog.Types.Count);
        Assert.Equal((0, 35), (catalog.Types[0].Id, catalog.MaxType));
        Assert.Equal(36, catalog.Types.Select(t => t.Id).Distinct().Count());
        Assert.Equal("Door", catalog.Name(0));
        Assert.Equal("Do not use 2", catalog.Name(28));
        Assert.Equal("Capture point", catalog.Name(29));
    }

    [Fact]
    public void TrinityCoreKnowsItsRetailTypes()
    {
        var catalog = GameObjectTypeCatalog.Trinity;
        Assert.Equal(67, catalog.Types.Count);
        Assert.Equal((0, 66), (catalog.Types[0].Id, catalog.MaxType));
        Assert.Equal("Gathering node", catalog.Name(50));
        Assert.Equal("Control zone", catalog.Name(29));
        Assert.Equal("Player choice chest", catalog.Name(56));
    }

    [Fact]
    public void EveryTypeHasUniqueSortedFieldIndicesInRange()
    {
        foreach (var (catalog, maxData) in new[] { (GameObjectTypeCatalog.Ac, 23), (GameObjectTypeCatalog.Trinity, 34) })
        {
            foreach (var type in catalog.Types)
            {
                var indices = type.Fields.Select(f => f.Index).ToArray();
                Assert.True(indices.SequenceEqual(indices.OrderBy(i => i)), $"{type.Code} fields are not sorted");
                Assert.True(indices.Distinct().Count() == indices.Length, $"{type.Code} has a duplicate field index");
                Assert.All(indices, index => Assert.InRange(index, 0, maxData));
                Assert.All(type.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Code), $"{type.Code} has a field without a code"));
                Assert.All(type.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Label), $"{type.Code}.{f.Code} has no label"));
            }
        }
    }

    [Fact]
    public void CommonTypesCarryTheirWotLKFields()
    {
        var ac = GameObjectTypeCatalog.Ac;
        var door = ac.Find(0)!;
        Assert.Equal("Lock", door.Field(1)!.Label);
        Assert.Equal(GameObjectFieldKind.Reference, door.Field(1)!.Kind);
        Assert.Equal("Lock", door.Field(1)!.Reference);
        Assert.Equal(65536d, door.Field(2)!.Scale);

        var chest = ac.Find(3)!;
        Assert.Equal("Loot table", chest.Field(1)!.Label);
        Assert.Equal("Loot", chest.Field(1)!.Reference);
        Assert.Equal(GameObjectFieldKind.Bool, chest.Field(3)!.Kind);

        var trap = ac.Find(6)!;
        Assert.Equal(GameObjectFieldKind.Choice, trap.Field(4)!.Kind);
        Assert.Equal(3, trap.Field(4)!.Options!.Length);

        var chair = ac.Find(7)!;
        Assert.Equal(1, chair.Field(0)!.Default);

        var tcDoor = GameObjectTypeCatalog.Trinity.Find(0)!;
        Assert.Equal(GameObjectFieldKind.Milliseconds, tcDoor.Field(2)!.Kind);
        Assert.Equal(3000, tcDoor.Field(2)!.Default);
        Assert.Equal(GameObjectFieldKind.Bool, tcDoor.Field(0)!.Kind);
    }

    [Fact]
    public void Type29IsNotSharedBetweenDialects()
    {
        var ac = GameObjectTypeCatalog.Ac.Find(29)!;
        var tc = GameObjectTypeCatalog.Trinity.Find(29)!;
        Assert.Equal("GAMEOBJECT_TYPE_CAPTURE_POINT", ac.Code);
        Assert.Equal("GAMEOBJECT_TYPE_CONTROL_ZONE", tc.Code);
        Assert.False(ac.Fields.Select(f => f.Code).SequenceEqual(tc.Fields.Select(f => f.Code)));
    }

    [Fact]
    public void TrinityCoreHasTypesAzerothCoreDoesNot()
    {
        Assert.Null(GameObjectTypeCatalog.Ac.Find(50));
        Assert.NotNull(GameObjectTypeCatalog.Trinity.Find(50));
        Assert.Equal(67, GameObjectTypeCatalog.Trinity.Types.Count);
        Assert.True(GameObjectTypeCatalog.Trinity.Types.Count > GameObjectTypeCatalog.Ac.Types.Count);
    }

    [Fact]
    public void ReferencesChoicesAndUnusedFieldsAreWellFormed()
    {
        foreach (var type in GameObjectTypeCatalog.Ac.Types.Concat(GameObjectTypeCatalog.Trinity.Types))
        {
            foreach (var field in type.Fields)
            {
                if (field.Kind == GameObjectFieldKind.Reference)
                {
                    Assert.False(string.IsNullOrWhiteSpace(field.Reference), $"{type.Code}.{field.Code} has no reference tag");
                    Assert.Contains(field.Reference, References);
                }

                if (field.Kind == GameObjectFieldKind.Choice)
                {
                    Assert.NotNull(field.Options);
                    Assert.True(field.Options!.Length >= 2, $"{type.Code}.{field.Code} has fewer than two options");
                    Assert.Equal(field.Options.Length, field.Options.Select(o => o.Value).Distinct().Count());
                }

                if (field.Unused)
                {
                    Assert.True(field.Label.Contains("Unused", StringComparison.OrdinalIgnoreCase) || field.Code.StartsWith("empty", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }

    [Fact]
    public void EveryUsedFieldHasADescriptiveTooltip()
    {
        string[] generic = ["", "On or off.", "One of the listed values.", "In milliseconds."];
        foreach (var type in GameObjectTypeCatalog.Ac.Types.Concat(GameObjectTypeCatalog.Trinity.Types))
        {
            foreach (var field in type.Fields.Where(f => !f.Unused))
            {
                Assert.False(string.IsNullOrWhiteSpace(field.Tip), $"{type.Code}.{field.Code} has no tooltip");
                Assert.DoesNotContain(field.Tip, generic);
                Assert.True(field.Tip.Length >= 15, $"{type.Code}.{field.Code} tooltip is too short: {field.Tip}");
                Assert.Contains(' ', field.Tip);
            }
        }
    }

    [Fact]
    public void CommonTypesExplainTheActualEffect()
    {
        var tc = GameObjectTypeCatalog.Trinity;
        Assert.Contains("fish is caught", tc.Find(25)!.Field(1)!.Tip);
        Assert.Contains("experience tier", tc.Find(3)!.Field(19)!.Tip);
        Assert.Contains("player-count advantage", tc.Find(29)!.Field(14)!.Tip);
        Assert.Contains("hitpoint record", tc.Find(33)!.Field(2)!.Tip);
        Assert.Contains("looted chest", tc.Find(3)!.Field(2)!.Tip);

        var ac = GameObjectTypeCatalog.Ac;
        Assert.Contains("trap", ac.Find(6)!.Field(4)!.Tip);
        Assert.Contains("1/65536", ac.Find(0)!.Field(2)!.Tip);
    }

    [Fact]
    public void UnknownTypesGetANumberedFallback()
    {
        Assert.Null(GameObjectTypeCatalog.Ac.Find(200));
        Assert.Equal("Type 200", GameObjectTypeCatalog.Ac.Name(200));
        Assert.Equal("Type 200", GameObjectTypeCatalog.Trinity.Name(200));
    }

    [Fact]
    public void TheCataloguesAreSelectedByDialect()
    {
        Assert.Same(GameObjectTypeCatalog.Ac, GameObjectTypeCatalog.For(WorldDialect.AzerothCore));
        Assert.Same(GameObjectTypeCatalog.Trinity, GameObjectTypeCatalog.For(WorldDialect.TrinityCore));
        Assert.Same(GameObjectTypeCatalog.Ac, GameObjectTypes.For(WorldDialect.AzerothCore));
        Assert.Equal(35, GameObjectTypes.MaxNamed(WorldDialect.AzerothCore));
        Assert.Equal(66, GameObjectTypes.MaxNamed(WorldDialect.TrinityCore));
    }
}
