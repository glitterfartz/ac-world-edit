using System.Globalization;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Sql;

/// <summary>
/// Guards for FLOAT columns must compare against the value the column actually stores. A 4-byte float
/// rounds a clicked coordinate (4709.97 is stored as 4709.97021484375), and at large coordinates that
/// rounding exceeds the 0.0001 window, so a guard written with the full double misses the row the same
/// change just wrote and the apply rolls back.
/// </summary>
public class SqlExportFloatGuardTests
{
    static Row Node(double y) => Row.Of(
        ("id", 53004763u), ("point", 1u),
        ("position_x", -1476.42), ("position_y", y), ("position_z", -2.21487),
        ("orientation", Row.NullValue), ("velocity", 0.0), ("delay", 0u),
        ("move_type", 0u), ("smoothTransition", 0u), ("action", 0u),
        ("action_chance", 100u), ("wpguid", 0u));

    [Theory]
    [InlineData(4709.97)]
    [InlineData(4701.72)]
    [InlineData(-9425.82)]
    [InlineData(0.1)]
    public void AGuardUsesTheFloatTheColumnHolds(double y)
    {
        string apply = SqlExport.Build("db", [], [new RowEdit("waypoint_data", "53004763|1", null, Node(y))], null, AcSchema.Instance).Apply;

        string stored = ((double)(float)y).ToString("R", CultureInfo.InvariantCulture);
        Assert.Contains($"ABS(`position_y` - ({stored})) < 0.0001", apply);
        // The double literal is what the old guard used; for these samples it is a different number.
        Assert.DoesNotContain($"ABS(`position_y` - ({y.ToString("R", CultureInfo.InvariantCulture)})) < 0.0001", apply);
    }

    [Fact]
    public void TheRoundingIsBigEnoughToHaveFailedTheOldGuard()
    {
        // 4709.97 stores 0.000215 away, beyond the 0.0001 window, which is exactly the reported failure.
        Assert.True(Math.Abs((double)(float)4709.97 - 4709.97) >= 0.0001);
    }
}
