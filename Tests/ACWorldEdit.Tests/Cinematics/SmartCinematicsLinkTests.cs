using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Cinematics;

/// <summary>
/// The SmartAI conversation and scene actions: their curated sentences name the parameter kinds, and
/// those kinds read as named references in the rule sentences.
/// </summary>
public class SmartCinematicsLinkTests
{
    sealed class NoNames : IContentNames
    {
        public string? Spell(uint id) => null;
        public string? Creature(uint id) => null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => null;
        public string? Item(uint id) => null;
        public string? Text(uint entry, uint group) => null;
        public bool CreatureSpawnExists(ulong guid) => false;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    [Theory]
    [InlineData(129u, SmartParamKind.Scene)]
    [InlineData(130u, SmartParamKind.Scene)]
    [InlineData(143u, SmartParamKind.Conversation)]
    [InlineData(154u, SmartParamKind.Conversation)]
    public void CuratedActionsNameTheirCinematicsKind(uint id, SmartParamKind kind)
    {
        var action = SmartCatalog.Trinity.Actions.First(entry => entry.Id == id);

        Assert.Equal(kind, action.Params[0].Kind);
        Assert.Contains("{1}", action.Sentence);
    }

    [Fact]
    public void SceneAndConversationValuesReadAsNamedKinds()
    {
        var names = new NoNames();
        Assert.Equal("scene 35", SmartCatalog.FormatValue(SmartParamKind.Scene, 35, names));
        Assert.Equal("conversation 754", SmartCatalog.FormatValue(SmartParamKind.Conversation, 754, names));
        Assert.Contains("conversation template", new SmartParam("Conversation", SmartParamKind.Conversation).Tip());
        Assert.Contains("scene template", new SmartParam("Scene", SmartParamKind.Scene).Tip());
    }
}
