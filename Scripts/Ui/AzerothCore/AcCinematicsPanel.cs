using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The AzerothCore side of the cinematics editor: the database has no conversations, scenes or
/// player choices, so the panel says so instead of showing empty pages.
/// </summary>
public partial class AcCinematicsPanel : CinematicsPanel
{
    public override WorldDialect Dialect => WorldDialect.AzerothCore;
    public string Notice { get; } = "Cinematics are a TrinityCore feature. The AzerothCore world database has no conversations, scenes or player choices.";

    protected override void Build()
    {
        AddThemeConstantOverride("separation", 6);
        var label = ClientStyle.Text(Notice, 14, ClientStyle.Faded);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(label);
    }

    public override void Open(uint id)
    {
    }

    public override Task Load(uint id) => Task.CompletedTask;
    public override Task NewItem(uint? copyFrom) => Task.CompletedTask;
    public override Task<IReadOnlyList<(uint Id, string Name)>> Search(string term) => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>([]);
    protected override void RenderTab()
    {
    }

    protected override void Rebuild()
    {
    }

    protected override void ReloadAfterHistory()
    {
    }
}
