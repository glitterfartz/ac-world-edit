namespace ACWorldEdit.Foundation.Scripting;

/// <summary>The ALE (AzerothCore) host: its id, label and the merged embedded profile.</summary>
public static class AcScriptHosts
{
    public const string Id = "ale-azerothcore";
    public const string Label = "ALE (AzerothCore)";

    public static ScriptHostInfo Info { get; } = new(Id, Label);

    static readonly Lazy<ScriptHostProfile> LazyProfile = new(ScriptCatalogLoader.LoadAleAzerothCore);

    public static ScriptHostProfile Profile => LazyProfile.Value;
}
