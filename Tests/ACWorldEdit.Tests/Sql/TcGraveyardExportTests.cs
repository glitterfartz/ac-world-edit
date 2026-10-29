using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Sql;

/// <summary>
/// TrinityCore graveyard export: positions go to world_safe_locs, links to graveyard_zone without a
/// Faction column, and the faction to one plain CONDITION_TEAM row (source type 27) per link. Every
/// transition is checked in both directions. The scripts are not executed here.
/// </summary>
public class TcGraveyardExportTests
{
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static readonly GraveyardRecord Goldshire = new(106, 0, -9339.72, 170.2, 67.8, "Elwynn Forest, Goldshire", [new(12, GraveyardLink.Alliance, "Elwynn Forest")]);

    static SqlScripts Export(GraveyardRecord after) =>
        SqlExport.Build("src", [new GraveyardEdit("src", Goldshire.Id, Goldshire, after)], [], Stamp, null, TcSchema.Instance);

    static string[] Statements(string script) =>
        script.Split('\n').Where(l => l.StartsWith("UPDATE ", StringComparison.Ordinal) || l.StartsWith("INSERT ", StringComparison.Ordinal) || l.StartsWith("DELETE ", StringComparison.Ordinal)).ToArray();

    static GraveyardRecord WithFaction(ushort faction) => Goldshire with { Links = [Goldshire.Links[0] with { Faction = faction }] };

    [Fact]
    public void AMoveUpdatesWorldSafeLocsAndNothingElse()
    {
        var scripts = Export(Goldshire with { X = -9330, Comment = "Goldshire" });
        var apply = Assert.Single(Statements(scripts.Apply));
        Assert.StartsWith("UPDATE `world_safe_locs` SET `LocX` = -9330, `LocY` = 170.2, `LocZ` = 67.8, `Comment` = 'Goldshire' WHERE `ID` = 106", apply);
        Assert.Contains("ABS(`LocX` - (-9339.72)) < 0.0001", apply);
        Assert.StartsWith("UPDATE `world_safe_locs` SET `LocX` = -9339.72", Assert.Single(Statements(scripts.Revert)));
        Assert.DoesNotContain("game_graveyard", scripts.Apply);
    }

    [Fact]
    public void NeutralToAllianceInsertsATeamRowThatTheRevertDeletes()
    {
        var neutral = WithFaction(GraveyardLink.Neutral);
        var scripts = SqlExport.Build("src", [new GraveyardEdit("src", 106, neutral, Goldshire)], [], Stamp, null, TcSchema.Instance);
        string insert = Assert.Single(Statements(scripts.Apply));
        Assert.StartsWith("INSERT INTO `conditions`", insert);
        Assert.Contains("VALUES (27, 12, 106, 0, 0, 6, 0, 469, 0, 0, '', 0, 0, 0, '', 'Graveyard - 106 - 12 - Elwynn Forest, Goldshire - Elwynn Forest - Team Alliance');", insert);
        // The preflight requires the link to have no conditions at all.
        Assert.Contains("IF(COUNT(*) = 0, 'ok', 'STALE') AS `state` FROM `conditions` WHERE `SourceTypeOrReferenceId` = 27 AND `SourceGroup` = 12 AND `SourceEntry` = 106;", scripts.Apply);
        string delete = Assert.Single(Statements(scripts.Revert));
        Assert.StartsWith("DELETE FROM `conditions` WHERE `SourceTypeOrReferenceId` = 27 AND `SourceGroup` = 12 AND `SourceEntry` = 106", delete);
        Assert.Contains("`ConditionTypeOrReference` = 6", delete);
        Assert.Contains("`ConditionValue1` = 469", delete);
    }

    [Fact]
    public void AllianceToHordeChangesOnlyTheTeamValue()
    {
        var scripts = Export(WithFaction(GraveyardLink.Horde));
        string apply = Assert.Single(Statements(scripts.Apply));
        Assert.StartsWith("UPDATE `conditions` SET `ConditionValue1` = 67 WHERE", apply);
        Assert.Contains("`ConditionValue1` = 469 AND", apply);
        string revert = Assert.Single(Statements(scripts.Revert));
        Assert.StartsWith("UPDATE `conditions` SET `ConditionValue1` = 469 WHERE", revert);
        Assert.Contains("`ConditionValue1` = 67 AND", revert);
    }

    [Fact]
    public void HordeToNeutralDeletesTheTeamRow()
    {
        var horde = WithFaction(GraveyardLink.Horde);
        var scripts = SqlExport.Build("src", [new GraveyardEdit("src", 106, horde, WithFaction(GraveyardLink.Neutral))], [], Stamp, null, TcSchema.Instance);
        Assert.StartsWith("DELETE FROM `conditions`", Assert.Single(Statements(scripts.Apply)));
        Assert.Contains("Team Horde", Assert.Single(Statements(scripts.Revert)));
    }

    [Fact]
    public void LinkingAZoneWithATeamAddsTheLinkThenItsTeamAndRevertsInReverse()
    {
        var after = Goldshire with { Links = [.. Goldshire.Links, new GraveyardLink(40, GraveyardLink.Horde, "Westfall")] };
        var scripts = Export(after);
        var apply = Statements(scripts.Apply);
        Assert.Equal(2, apply.Length);
        Assert.Equal("INSERT INTO `graveyard_zone` (`ID`, `GhostZone`, `Comment`) VALUES (106, 40, 'Westfall');", apply[0]);
        Assert.StartsWith("INSERT INTO `conditions`", apply[1]);
        Assert.Contains("(27, 40, 106, 0, 0, 6, 0, 67,", apply[1]);
        var revert = Statements(scripts.Revert);
        Assert.StartsWith("DELETE FROM `conditions`", revert[0]);
        Assert.StartsWith("DELETE FROM `graveyard_zone` WHERE `ID` = 106 AND `GhostZone` = 40", revert[1]);
        Assert.DoesNotContain("Faction", scripts.Apply);
    }

    [Fact]
    public void LinkingANeutralZoneWritesNoCondition()
    {
        var scripts = Export(Goldshire with { Links = [.. Goldshire.Links, new GraveyardLink(40, GraveyardLink.Neutral, "")] });
        Assert.StartsWith("INSERT INTO `graveyard_zone`", Assert.Single(Statements(scripts.Apply)));
    }

    [Fact]
    public void UnlinkingATeamZoneDropsItsTeamRowFirst()
    {
        var scripts = Export(Goldshire with { Links = [] });
        var apply = Statements(scripts.Apply);
        Assert.Equal(2, apply.Length);
        Assert.StartsWith("DELETE FROM `conditions`", apply[0]);
        Assert.StartsWith("DELETE FROM `graveyard_zone` WHERE `ID` = 106 AND `GhostZone` = 12 AND BINARY COALESCE(`Comment`, '') = BINARY 'Elwynn Forest';", apply[1]);
        var revert = Statements(scripts.Revert);
        Assert.StartsWith("INSERT INTO `graveyard_zone`", revert[0]);
        Assert.StartsWith("INSERT INTO `conditions`", revert[1]);
    }

    [Fact]
    public void ALinkCommentChangeTouchesOnlyGraveyardZone()
    {
        var scripts = Export(Goldshire with { Links = [Goldshire.Links[0] with { Comment = "Elwynn" }] });
        Assert.Equal("UPDATE `graveyard_zone` SET `Comment` = 'Elwynn' WHERE `ID` = 106 AND `GhostZone` = 12 AND BINARY COALESCE(`Comment`, '') = BINARY 'Elwynn Forest';", Assert.Single(Statements(scripts.Apply)));
    }
}
