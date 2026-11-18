using ACWorldEdit.Assets;
using ACWorldEdit.Data.Scripting;
using Xunit;

/// <summary>The scripting settings resolver: defaults, per-profile sections and saved overrides.</summary>
public class ScriptingSettingsTests
{
    static readonly Dictionary<(string Section, string Key), string> Saved = [];

    static string Read(string section, string key, string fallback) =>
        Saved.GetValueOrDefault((section, key), fallback);

    [Fact]
    public void TargetHostDefaultsToAle()
    {
        Saved.Clear();

        Assert.Equal(ScriptingSettings.TargetHostDefault, ScriptingSettings.TargetHost(Read));
        Assert.Equal("ale-azerothcore", ScriptingSettings.TargetHostDefault);
    }

    [Fact]
    public void SavedValuesWinOverDefaults()
    {
        Saved.Clear();
        Saved[("scripting", "target_host")] = "eluna-trinitycore";
        Saved[("scripting", "auto_export")] = "1";
        Saved[("scripting", "last_document")] = "deadmines";
        Saved[("scripting.wrath", "workspace")] = "user://elsewhere";
        Saved[("scripting.retail", "export_dir")] = "user://exports/retail";

        Assert.Equal("eluna-trinitycore", ScriptingSettings.TargetHost(Read));
        Assert.True(ScriptingSettings.AutoExport(Read));
        Assert.Equal("deadmines", ScriptingSettings.LastDocument(Read));
        Assert.Equal("user://elsewhere", ScriptingSettings.Workspace(ClientKind.Wrath335, Read));
        Assert.Equal("user://exports/retail", ScriptingSettings.ExportDirectory(ClientKind.Retail, Read));
    }

    [Fact]
    public void WorkspaceDefaultsArePerProfile()
    {
        Saved.Clear();

        Assert.Equal("user://scripts/wrath", ScriptingSettings.Workspace(ClientKind.Wrath335, Read));
        Assert.Equal("user://scripts/retail", ScriptingSettings.Workspace(ClientKind.Retail, Read));
    }

    [Fact]
    public void ExportDirectoryIsUnsetByDefault()
    {
        Saved.Clear();

        Assert.Equal("", ScriptingSettings.ExportDirectory(ClientKind.Wrath335, Read));
        Assert.Equal("", ScriptingSettings.ExportDirectory(ClientKind.Retail, Read));
    }

    [Fact]
    public void AutoExportAcceptsTheSavedFormsAndRejectsNoise()
    {
        Saved.Clear();
        Assert.False(ScriptingSettings.AutoExport(Read));
        foreach (string on in new[] { "1", "true", "TRUE", "yes" })
        {
            Saved[("scripting", "auto_export")] = on;
            Assert.True(ScriptingSettings.AutoExport(Read), on);
        }

        Saved[("scripting", "auto_export")] = "maybe";
        Assert.False(ScriptingSettings.AutoExport(Read));
        Saved.Clear();
    }

    [Fact]
    public void SectionsArePerClientProfile()
    {
        Assert.Equal("scripting", ScriptingSettings.Section);
        Assert.Equal("scripting.wrath", ScriptingSettings.ProfileSection(ClientKind.Wrath335));
        Assert.Equal("scripting.retail", ScriptingSettings.ProfileSection(ClientKind.Retail));
    }
}
