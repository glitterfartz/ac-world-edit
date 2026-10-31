namespace ACWorldEdit.Assets;

/// <summary>
/// The native library and data folder the Retail profile needs beyond the client itself, checked
/// without opening a client. The search rules are CascNative.Candidates and Db2Tables.FindDefinitions,
/// the same ones the Retail loader uses, so a warning here means the load would fail. The Wrath
/// profile has no such dependencies.
/// </summary>
public static class DependencyCheck
{
    public const string SetupHint = "Run python Tools/setup.py.";

    public const string CascLibMissing = "CascLib.dll was not found. " + SetupHint;

    public const string DefinitionsMissing = "Retail table definitions (Data/WoWDBDefs) were not found. " + SetupHint;

    public static bool CascLibPresent(string? overridePath, params string?[] directories) =>
        CascNative.Candidates(overridePath, directories).Any(File.Exists);

    public static bool DefinitionsPresent(string? overridePath, params string?[] directories) =>
        Db2Tables.FindDefinitions(overridePath, directories) != null;

    /// <summary>
    /// What is missing for this client profile, as sentences ready for the status bar. Empty when
    /// the profile can load.
    /// </summary>
    public static List<string> Missing(ClientKind kind, string? cascLibOverride, string? definitionsOverride, params string?[] directories)
    {
        var problems = new List<string>();
        if (kind != ClientKind.Retail)
        {
            return problems;
        }

        if (!CascLibPresent(cascLibOverride, directories))
        {
            problems.Add(CascLibMissing);
        }

        if (!DefinitionsPresent(definitionsOverride, directories))
        {
            problems.Add(DefinitionsMissing);
        }

        return problems;
    }
}
