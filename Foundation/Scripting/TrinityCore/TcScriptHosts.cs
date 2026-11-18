namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// The Eluna (TrinityCore) host. It is listed as experimental from the start; the proof pack and
/// profile arrive with the multi-host milestone.
/// </summary>
public static class TcScriptHosts
{
    public const string Id = "eluna-trinitycore";
    public const string Label = "Eluna (TrinityCore)";

    public static ScriptHostInfo Info { get; } = new(Id, Label, Experimental: true);

    public static ScriptHostProfile? Profile => null;
}
