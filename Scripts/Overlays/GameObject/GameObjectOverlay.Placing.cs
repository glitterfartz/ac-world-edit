using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Choosing a GameObject template to place; the viewer's placement tool does the placing.</summary>
public partial class GameObjectOverlay
{
    public PlaceCard PlaceCard { get; private set; } = null!;

    /// <summary>The user chose a GameObject template in the Place card.</summary>
    public event Action<uint, string>? PlaceRequested;

    void BuildPlaceCard(VBoxContainer page)
    {
        PlaceCard = new PlaceCard("GameObject") { Search = term => DBSource.Open(Connection()).SearchAsync(SmartParamKind.GameObject, term, default) };
        PlaceCard.Chosen += (entry, name) => PlaceRequested?.Invoke(entry, name);
        InspectorUi.Card(page, "Place").AddChild(PlaceCard);
    }
}
