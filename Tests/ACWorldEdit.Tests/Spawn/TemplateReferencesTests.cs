using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Spawn;

public class TemplateReferencesTests
{
    [Fact]
    public void EveryTemplateReferenceTableIsReadByTheSessionSchema()
    {
        // The reader skips a table the session's schema read did not cover, so a reference outside
        // WorldDatabase.SchemaTables would quietly count as zero.
        foreach (var dialect in new[] { WorldDialect.AzerothCore, WorldDialect.TrinityCore })
        {
            var known = WorldReadSession.SchemaTables(dialect);
            foreach (var kind in new[] { SpawnKind.Creature, SpawnKind.GameObject })
            {
                foreach (var reference in TemplateReferences.For(kind))
                {
                    Assert.Contains(reference.Table, known);
                }
            }
        }
    }

    [Fact]
    public void TemplatesNameTheirSpawnsAndContent()
    {
        var creature = TemplateReferences.For(SpawnKind.Creature);
        Assert.Contains(creature, r => r.Table == "creature" && r.Column == "id");
        Assert.Contains(creature, r => r.Table == "smart_scripts" && r.Where == "source_type = 0");

        var gameObject = TemplateReferences.For(SpawnKind.GameObject);
        Assert.Contains(gameObject, r => r.Table == "gameobject" && r.Column == "id");
        Assert.Contains(gameObject, r => r.Table == "smart_scripts" && r.Where == "source_type = 1");
    }
}
