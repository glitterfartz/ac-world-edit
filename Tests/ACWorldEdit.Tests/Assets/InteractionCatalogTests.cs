using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Wrath DBC reference reads against the real client.</summary>
public class InteractionCatalogTests
{
    [ClientDataFact]
    public void EmoteAnimMapsEmotesToTheirAnimationClips()
    {
        var catalog = InteractionCatalog.Load(ClientData.Store);
        Assert.Equal(67u, catalog.EmoteAnim(3)); // ONESHOT_WAVE -> EmoteWave
        Assert.Equal(69u, catalog.EmoteAnim(10)); // STATE_DANCE -> EmoteDance
        Assert.Equal(0u, catalog.EmoteAnim(12)); // STATE_SLEEP names no clip
        Assert.Equal(0u, catalog.EmoteAnim(uint.MaxValue));
    }

    [ClientDataFact]
    public void MountSpellsNameTheirMountDisplays()
    {
        var catalog = InteractionCatalog.Load(ClientData.Store);
        // Spell 458 (Brown Horse) and 5784 (Felsteed) carry aura 78 with the display in EffectMiscValue.
        Assert.Contains(284u, catalog.MountDisplays);
        Assert.Contains(304u, catalog.MountDisplays);
        Assert.Contains(4710u, catalog.MountDisplays);
        Assert.True(catalog.MountDisplays.Count > 100, $"only {catalog.MountDisplays.Count} mount displays");
    }
}
