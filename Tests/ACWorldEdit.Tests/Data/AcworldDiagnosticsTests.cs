using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Data;

/// <summary>
/// Unit coverage for the history-database error text: every mapped MySQL error must name the
/// database and say what to do, never leak credentials, and unknown codes must stay identifiable.
/// </summary>
public class AcworldDiagnosticsTests
{
    static DatabaseSettings Acworld() => new("db.example", 3306, "editor", "secret-pw", "acworld");

    [Fact]
    public void Error1044NamesDatabaseUserAndGrant()
    {
        string text = AcworldDiagnostics.DescribeCode(1044, Acworld());
        Assert.Contains("1044", text);
        Assert.Contains("`acworld`", text);
        Assert.Contains("editor", text);
        Assert.Contains("GRANT", text);
        Assert.DoesNotContain("secret-pw", text);
    }

    [Fact]
    public void Error1045PointsAtCredentials()
    {
        string text = AcworldDiagnostics.DescribeCode(1045, Acworld());
        Assert.Contains("1045", text);
        Assert.Contains("editor", text);
        Assert.DoesNotContain("secret-pw", text);
    }

    [Fact]
    public void Error1049NamesMissingDatabase()
    {
        string text = AcworldDiagnostics.DescribeCode(1049, Acworld());
        Assert.Contains("1049", text);
        Assert.Contains("acworld", text);
    }

    [Fact]
    public void Error1142AsksForTablePrivileges()
    {
        string text = AcworldDiagnostics.DescribeCode(1142, Acworld());
        Assert.Contains("1142", text);
        Assert.Contains("acworld", text);
        Assert.DoesNotContain("secret-pw", text);
    }

    [Fact]
    public void UnknownCodesStayIdentifiable()
    {
        string text = AcworldDiagnostics.DescribeCode(9999, Acworld());
        Assert.Contains("9999", text);
        Assert.Contains("acworld", text);
    }

    [Fact]
    public void DescribeNeverLeaksThePassword()
    {
        var error = new InvalidOperationException("boom");
        Assert.DoesNotContain("secret-pw", AcworldDiagnostics.Describe(error, Acworld()));
        Assert.DoesNotContain("secret-pw", AcworldDiagnostics.Describe(new OperationCanceledException(), Acworld()));
    }
}
