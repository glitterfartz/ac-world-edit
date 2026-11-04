using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// What the viewer needs to know about a client family besides how to read its files, so the UI asks
/// the profile instead of comparing the client kind.
/// </summary>
public sealed record ClientProfile(ClientKind Kind, string Title, string LoadingMessage, bool SupportsDatabase, bool OffersListfile)
{
    /// <summary>The world database flavour that serves this client: retail runs on TrinityCore.</summary>
    public WorldDialect Dialect => Kind == ClientKind.Retail ? WorldDialect.TrinityCore : WorldDialect.AzerothCore;

    static readonly ClientProfile Wrath = new(ClientKind.Wrath335, "WRATH 3.3.5", "Reading MPQ tables and map list…", true, false);
    static readonly ClientProfile Retail = new(ClientKind.Retail, "RETAIL", "Reading CASC storage and map list…", true, true);

    public static ClientProfile For(ClientKind kind) => kind == ClientKind.Retail ? Retail : Wrath;
}
