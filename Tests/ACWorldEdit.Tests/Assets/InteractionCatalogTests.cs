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
}
