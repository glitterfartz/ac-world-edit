using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>
/// The one place that maps a semantic type to a GraphEdit port integer, a pin colour and the
/// assignability rules. The canvas and the validator both read this registry, so the two can never
/// disagree about whether two pins may connect.
/// </summary>
public class ScriptTypeSystemTests
{
    static readonly ScriptTypeRegistry Types = ScriptTypeRegistry.Default;

    [Fact]
    public void PortTypesAreStableAndUnique()
    {
        Assert.Equal(0, Types.PortType(ScriptValueType.Exec));
        Assert.Equal(2, Types.PortType(ScriptValueType.Int));
        Assert.Equal(Types.PortType(ScriptValueType.Player), Types.PortType(ScriptValueType.Player));

        var ports = Enum.GetValues<ScriptValueType>().Select(Types.PortType).ToArray();
        Assert.Equal(ports.Length, ports.Distinct().Count());
    }

    [Theory]
    [InlineData(ScriptValueType.Creature, ScriptValueType.Unit, true)]
    [InlineData(ScriptValueType.Creature, ScriptValueType.WorldObject, true)]
    [InlineData(ScriptValueType.Creature, ScriptValueType.Object, true)]
    [InlineData(ScriptValueType.Player, ScriptValueType.Unit, true)]
    [InlineData(ScriptValueType.Player, ScriptValueType.Creature, false)]
    [InlineData(ScriptValueType.Unit, ScriptValueType.Player, false)]
    [InlineData(ScriptValueType.Player, ScriptValueType.Player, true)]
    public void ObjectTypesFollowAleInheritance(ScriptValueType from, ScriptValueType to, bool assignable) =>
        Assert.Equal(assignable, Types.IsAssignable(from, to));

    [Theory]
    [InlineData(ScriptValueType.Int, ScriptValueType.Float, true)]
    [InlineData(ScriptValueType.Float, ScriptValueType.Int, false)]
    [InlineData(ScriptValueType.SpellId, ScriptValueType.Int, true)]
    [InlineData(ScriptValueType.Int, ScriptValueType.SpellId, true)]
    [InlineData(ScriptValueType.CreatureEntry, ScriptValueType.Int, true)]
    [InlineData(ScriptValueType.CreatureEntry, ScriptValueType.SpellId, false)]
    [InlineData(ScriptValueType.Exec, ScriptValueType.Int, false)]
    public void NumericConversions(ScriptValueType from, ScriptValueType to, bool assignable) =>
        Assert.Equal(assignable, Types.IsAssignable(from, to));

    [Theory]
    [InlineData(ScriptValueType.Int)]
    [InlineData(ScriptValueType.Creature)]
    [InlineData(ScriptValueType.Exec)]
    public void AnyAcceptsEveryType(ScriptValueType type)
    {
        Assert.True(Types.IsAssignable(type, ScriptValueType.Any));
        Assert.True(Types.IsAssignable(ScriptValueType.Any, type));
    }

    [Theory]
    [InlineData(ScriptValueType.Bool, true)]
    [InlineData(ScriptValueType.Int, true)]
    [InlineData(ScriptValueType.Float, true)]
    [InlineData(ScriptValueType.String, true)]
    [InlineData(ScriptValueType.SpellId, true)]
    [InlineData(ScriptValueType.ObjectGuid, true)]
    [InlineData(ScriptValueType.List, true)]
    [InlineData(ScriptValueType.Table, true)]
    [InlineData(ScriptValueType.Player, false)]
    [InlineData(ScriptValueType.WorldPacket, false)]
    public void ModuleVariablesHoldOnlyValueTypes(ScriptValueType type, bool storable) =>
        Assert.Equal(storable, Types.CanStoreInVariable(type));

    [Fact]
    public void PinColoursAreStableAndNonEmpty()
    {
        foreach (var type in Enum.GetValues<ScriptValueType>())
        {
            Assert.Matches("^#[0-9a-f]{6}$", Types.Color(type));
        }

        Assert.Equal("#ffffff", Types.Color(ScriptValueType.Exec));
        Assert.NotEqual(Types.Color(ScriptValueType.Bool), Types.Color(ScriptValueType.Int));
    }

    [Theory]
    [InlineData(ScriptValueType.Exec, "exec")]
    [InlineData(ScriptValueType.Int, "int")]
    [InlineData(ScriptValueType.Bool, "bool")]
    [InlineData(ScriptValueType.String, "string")]
    [InlineData(ScriptValueType.CreatureEntry, "CreatureEntry")]
    [InlineData(ScriptValueType.SpellId, "SpellId")]
    [InlineData(ScriptValueType.Player, "Player")]
    [InlineData(ScriptValueType.AleQuery, "ALEQuery")]
    public void JsonNamesRoundTrip(ScriptValueType type, string json)
    {
        Assert.Equal(json, ScriptValueTypeNames.ToJson(type));
        Assert.Equal(type, ScriptValueTypeNames.Parse(json));
    }
}
