using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Smart;

public class SmartCatalogTests
{
    static IEnumerable<(string Kind, SmartType Type)> All() =>
        SmartCatalog.Ac.Events.Select(t => ("event", t)).Concat(SmartCatalog.Ac.Actions.Select(t => ("action", t))).Concat(SmartCatalog.Ac.Targets.Select(t => ("target", t)));

    [Fact]
    public void TypeIdsAreUniquePerKind()
    {
        foreach (var group in All().GroupBy(x => x.Kind))
        {
            var duplicates = group.GroupBy(x => x.Type.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, $"{group.Key} ids duplicated: {string.Join(",", duplicates)}");
        }
    }

    [Fact]
    public void SentencesOnlyReferenceDeclaredParameters()
    {
        foreach (var (kind, type) in All().Where(x => x.Type.ParamsKnown && x.Type.Sentence.Length > 0))
        {
            for (int i = type.Params.Length + 1; i <= 6; i++)
            {
                Assert.False(type.Sentence.Contains("{" + i + "}"), $"{kind} {type.Id} {type.Code} sentence references undeclared {{{i}}}");
            }
        }
    }

    [Fact]
    public void EveryParameterHasADescriptiveTooltip()
    {
        foreach (var catalog in new[] { SmartCatalog.Ac, SmartCatalog.Trinity })
        {
            foreach (var type in catalog.Events.Concat(catalog.Actions).Concat(catalog.Targets))
            {
                foreach (var param in type.Params)
                {
                    string tip = param.Tip(type.Name, type.Help);
                    string where = $"{catalog.Dialect} {type.Code}";
                    Assert.StartsWith(param.Label + ":", tip);
                    Assert.True(tip.Length >= 20, $"{where} {param.Label} tooltip is too short: {tip}");
                    Assert.Contains(' ', tip);
                }
            }
        }
    }

    [KnownBug("KB-001", "Percent parameters already render with a trailing %, so catalog sentences that also write % show '30%%' (PROGRESS.md notes 'When its health is 0%-30%%').")]
    public void PercentParametersAreNotFollowedByLiteralPercent()
    {
        var offenders = new List<string>();
        foreach (var (kind, type) in All())
        {
            for (int i = 0; i < type.Params.Length; i++)
            {
                if (type.Params[i].Kind == SmartParamKind.Percent && type.Sentence.Contains("{" + (i + 1) + "}%"))
                {
                    offenders.Add($"{kind} {type.Id} {type.Code}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Doubled %: " + string.Join("; ", offenders));
    }
}
