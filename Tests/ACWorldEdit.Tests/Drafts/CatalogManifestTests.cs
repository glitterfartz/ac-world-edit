using ACWorldEdit.Foundation;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>
/// Pins both table catalogs and the schema wiring without a database, so moving or splitting a
/// catalog file cannot silently drop, reorder or retype a table. The Db tests skip when no server is
/// configured; this test is the always-on record of what the editors may read and write.
/// </summary>
public class CatalogManifestTests
{
    /// <summary>The AzerothCore catalog in order, with a digest of each table's keys, columns and defaults.</summary>
    static readonly (string Name, string Digest)[] Ac =
    [
        ("creature_template", "32640a3ea2e7f23e"),
        ("gossip_menu", "de5c68cfe20b07ef"),
        ("npc_text", "2a4a23e38ce611ba"),
        ("gossip_menu_option", "4750ae74760a1a50"),
        ("creature_queststarter", "5734a45f3371ccdf"),
        ("creature_questender", "344380a448c9b52b"),
        ("npc_vendor", "d73740f05ba96042"),
        ("creature_default_trainer", "2bf8bb638db8e679"),
        ("trainer", "cae31704e1d6043d"),
        ("trainer_spell", "d0c0167548908ed7"),
        ("npc_spellclick_spells", "cb412375af3b7cbe"),
        ("creature_ai", "9e8573d72b944fdb"),
        ("creature_stats", "08baccc0c3022f3e"),
        ("creature_details", "80c3feb19821c855"),
        ("quest_template", "14c3f2c68415caff"),
        ("quest_template_addon", "c6a92205306f0913"),
        ("quest_request_items", "c714092021b1e462"),
        ("quest_offer_reward", "aafb7bd0c0dec17c"),
        ("smart_scripts", "e244a26705927af3"),
        ("creature_text", "7a9027b39a189a4e"),
        ("areatrigger_scripts", "9a2341b75520d1c7"),
        ("creature", "a59d1b61ede947f3"),
        ("gameobject", "c0ced5d721ea00be"),
        ("creature_addon", "ce77249617b0097a"),
        ("gameobject_addon", "bf2cc513545d50e2"),
        ("creature_template_addon", "e9e06c884e4445ae"),
        ("conditions", "add21ca56426edbd"),
        ("gameobject_template", "c78ae1dfb354e08b"),
        ("gameobject_template_addon", "d730ff897bfc2539"),
        ("gameobject_queststarter", "56f537e804fa821b"),
        ("gameobject_questender", "c84f45775121cebc"),
        ("gameobject_questitem", "c80f524d1a46551b")
    ];

    /// <summary>The TrinityCore master catalog in order, with a digest of each table's keys, columns and defaults.</summary>
    static readonly (string Name, string Digest)[] Tc =
    [
        ("creature_template", "7645c1b34b0ecacc"),
        ("creature_template_difficulty", "f6e687d58cb9dca8"),
        ("creature_template_gossip", "c2f187f0459e563a"),
        ("gossip_menu", "de5c68cfe20b07ef"),
        ("npc_text", "43a5b17d4eac3d29"),
        ("gossip_menu_option", "86d4b89380c65bf9"),
        ("creature_trainer", "1388ad45cf2eaba5"),
        ("trainer", "b9c5981ff993badc"),
        ("trainer_spell", "be185ac4c97a2605"),
        ("npc_vendor", "b827817da9585bce"),
        ("quest_template", "6f38518356067c05"),
        ("quest_template_addon", "b2bff99d57d39560"),
        ("quest_request_items", "e0d969e6e64793e6"),
        ("quest_offer_reward", "198a64596f212833"),
        ("quest_objectives", "e5d9de3d3b9960ef"),
        ("creature_queststarter", "5734a45f3371ccdf"),
        ("creature_questender", "344380a448c9b52b"),
        ("npc_spellclick_spells", "cb412375af3b7cbe"),
        ("creature_ai", "9e8573d72b944fdb"),
        ("smart_scripts", "5991bab1e17c6896"),
        ("creature_text", "55b78d1f952d5f4b"),
        ("areatrigger_scripts", "9a2341b75520d1c7"),
        ("areatrigger_ai", "6addf038e9887d3d"),
        ("creature", "488cb1ab1ec67c8a"),
        ("gameobject", "cbd35983b6482c19"),
        ("creature_addon", "563de75ba45a27f3"),
        ("gameobject_addon", "447fe6560e3771ec"),
        ("creature_template_addon", "f2d89873e5eb50ed"),
        ("broadcast_text", "dcde26e8f3ccdedb"),
        ("hotfix_data", "aaf4aa0913eb44f9"),
        ("conditions", "01339ec5a5390766"),
        ("gameobject_template", "8264e170bcaf47b1"),
        ("gameobject_template_addon", "a7fb1228f8103fa6"),
        ("gameobject_queststarter", "56f537e804fa821b"),
        ("gameobject_questender", "c84f45775121cebc"),
        ("gameobject_questitem", "c80f524d1a46551b")
    ];

    /// <summary>Everything a table means to the editors: identity, keys, every column with its kind and text limit, the defaults and the nullable set.</summary>
    static string Canonical(TableSchema table) =>
        $"{table.Name}|{table.SqlName}|{string.Join(",", table.Key)}|" +
        string.Join(",", table.Columns.Select(c => $"{c.Name}:{c.Kind}:{c.MaxLength}")) +
        "|" + string.Join(",", (table.Added ?? []).Select(a => $"{a.Column}={a.Default}")) +
        "|" + string.Join(",", table.Nullable ?? []);

    static string Digest(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];

    static void AssertManifest(ITableCatalog catalog, (string Name, string Digest)[] expected)
    {
        Assert.Equal(expected.Length, catalog.All.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, catalog.All[i].Name);
            string canonical = Canonical(catalog.All[i]);
            Assert.True(expected[i].Digest == Digest(canonical), $"{catalog.All[i].Name} changed; the table is now:\n{canonical}");
        }
    }

    [Fact]
    public void AzerothCoreCatalogKeepsItsTablesInOrder()
    {
        AssertManifest(AcTables.Instance, Ac);
        Assert.Equal(Ac.Select(t => t.Name), AcTables.Tables.Select(t => t.Name));
    }

    [Fact]
    public void TrinityCoreCatalogKeepsItsTablesInOrder()
    {
        AssertManifest(TcTables.Instance, Tc);
        Assert.Equal(Tc.Select(t => t.Name), TcTables.Tables.Select(t => t.Name));
    }

    [Fact]
    public void FindReturnsTheListedTableAndRefusesAnUnknownOne()
    {
        ITableCatalog ac = AcTables.Instance;
        ITableCatalog tc = TcTables.Instance;
        foreach (var (name, _) in Ac)
        {
            Assert.Same(AcTables.Tables.First(t => t.Name == name), ac.Find(name));
        }

        foreach (var (name, _) in Tc)
        {
            Assert.Same(TcTables.Tables.First(t => t.Name == name), tc.Find(name));
        }

        Assert.Throws<InvalidDataException>(() => ac.Find("not_a_table"));
        Assert.Throws<InvalidDataException>(() => tc.Find("not_a_table"));
        // creature_default_trainer is an AzerothCore table; TrinityCore reaches a trainer through an option.
        Assert.Throws<InvalidDataException>(() => tc.Find("creature_default_trainer"));
    }

    [Fact]
    public void EachDialectWiresItsOwnSchema()
    {
        Assert.Same(AcSchema.Instance, WorldSchemas.For(WorldDialect.AzerothCore));
        Assert.Same(TcSchema.Instance, WorldSchemas.For(WorldDialect.TrinityCore));
        Assert.Throws<InvalidDataException>(() => WorldSchemas.For((WorldDialect)99));

        Assert.Equal(WorldDialect.AzerothCore, AcSchema.Instance.Dialect);
        Assert.Same(AcTables.Instance, AcSchema.Instance.Tables);
        Assert.Same(AcSqlDialect.Instance, AcSchema.Instance.Sql);
        Assert.Same(AcCreaturePage.Page, AcSchema.Instance.TemplatePage);
        Assert.Same(AcStatsPage.Page, AcSchema.Instance.StatsPage);
        Assert.Same(Foundation.VendorPage.AzerothCore, AcSchema.Instance.VendorPage);
        Assert.Same(Foundation.GossipPage.AzerothCore, AcSchema.Instance.GossipPage);
        Assert.Same(Foundation.TrainerPage.AzerothCore, AcSchema.Instance.TrainerPage);
        Assert.Same(Foundation.QuestLinksPage.AzerothCore, AcSchema.Instance.QuestLinksPage);
        Assert.Same(SmartCatalog.Ac, AcSchema.Instance.Smart);
        Assert.Same(ConditionCatalog.Ac, AcSchema.Instance.Conditions);

        Assert.Equal(WorldDialect.TrinityCore, TcSchema.Instance.Dialect);
        Assert.Same(TcTables.Instance, TcSchema.Instance.Tables);
        Assert.Same(TcSqlDialect.Instance, TcSchema.Instance.Sql);
        Assert.Same(TcCreaturePage.Page, TcSchema.Instance.TemplatePage);
        Assert.Same(TcStatsPage.Page, TcSchema.Instance.StatsPage);
        Assert.Same(Foundation.VendorPage.TrinityCore, TcSchema.Instance.VendorPage);
        Assert.Same(Foundation.GossipPage.TrinityCore, TcSchema.Instance.GossipPage);
        Assert.Same(Foundation.TrainerPage.TrinityCore, TcSchema.Instance.TrainerPage);
        Assert.Same(Foundation.QuestLinksPage.TrinityCore, TcSchema.Instance.QuestLinksPage);
        Assert.Same(SmartCatalog.Trinity, TcSchema.Instance.Smart);
        Assert.Same(ConditionCatalog.Trinity, TcSchema.Instance.Conditions);
    }

    [Fact]
    public void SmartaiCatalogsKeepTheirTypeCountsAndParameters()
    {
        Assert.Equal(WorldDialect.AzerothCore, SmartCatalog.Ac.Dialect);
        Assert.Equal((96, 182, 40), (SmartCatalog.Ac.Events.Count, SmartCatalog.Ac.Actions.Count, SmartCatalog.Ac.Targets.Count));
        Assert.Equal((6, 6), (SmartCatalog.Ac.EventParamCount, SmartCatalog.Ac.ActionParamCount));
        Assert.Equal(8, SmartCatalog.Ac.EventFlags.Length);
        Assert.Equal(0u, SmartCatalog.Ac.DeprecatedEventFlags);
        Assert.False(SmartCatalog.Ac.HasStringParams);
        Assert.Null(SmartCatalog.Ac.AreaTriggerAi);
        Assert.Same(AcTables.Instance, SmartCatalog.Ac.Tables);
        Assert.Same(AcTables.SmartScripts, SmartCatalog.Ac.Scripts);

        Assert.Equal(WorldDialect.TrinityCore, SmartCatalog.Trinity.Dialect);
        Assert.Equal((92, 161, 32), (SmartCatalog.Trinity.Events.Count, SmartCatalog.Trinity.Actions.Count, SmartCatalog.Trinity.Targets.Count));
        Assert.Equal((5, 7), (SmartCatalog.Trinity.EventParamCount, SmartCatalog.Trinity.ActionParamCount));
        Assert.Equal(5, SmartCatalog.Trinity.EventFlags.Length);
        Assert.Equal(0x1Eu, SmartCatalog.Trinity.DeprecatedEventFlags);
        Assert.True(SmartCatalog.Trinity.HasStringParams);
        Assert.Same(TcTables.AreaTriggerAI, SmartCatalog.Trinity.AreaTriggerAi);
        Assert.Same(TcTables.Instance, SmartCatalog.Trinity.Tables);
        Assert.Same(TcTables.SmartScripts, SmartCatalog.Trinity.Scripts);

        Assert.Same(SmartCatalog.Ac, SmartCatalog.For(WorldDialect.AzerothCore));
        Assert.Same(SmartCatalog.Trinity, SmartCatalog.For(WorldDialect.TrinityCore));
    }

    [Fact]
    public void ConditionCatalogsKeepTheirTypeAndSourceCounts()
    {
        Assert.Equal((56, 31), (ConditionCatalog.Ac.Types.Count, ConditionCatalog.Ac.Sources.Count));
        Assert.Equal((61, 36), (ConditionCatalog.Trinity.Types.Count, ConditionCatalog.Trinity.Sources.Count));
        Assert.Same(ConditionCatalog.Ac, AcSchema.Instance.Conditions);
        Assert.Same(ConditionCatalog.Trinity, TcSchema.Instance.Conditions);
    }

    [Fact]
    public void AnUnknownSourceIsAzerothCoreUntilItIsRemembered()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "sources.json");
        var dialects = new SourceDialects(path);
        Assert.Equal(WorldDialect.AzerothCore, dialects.DialectOf("unknown"));
        Assert.Same(AcSchema.Instance, dialects.SchemaFor("unknown"));

        dialects.Remember("tc", WorldDialect.TrinityCore);
        Assert.Same(TcSchema.Instance, dialects.SchemaFor("tc"));
        Assert.Equal(WorldDialect.TrinityCore, new SourceDialects(path).DialectOf("tc"));
    }
}
