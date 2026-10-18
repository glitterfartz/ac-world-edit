using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// What the viewer needs to know about a client family besides how to read its files, so the UI asks
/// the profile instead of comparing the client kind.
/// </summary>
public sealed record ClientProfile(ClientKind Kind, string Title, string LoadingMessage, bool SupportsDatabase, bool OffersListfile)
{
    static readonly ClientProfile Wrath = new(ClientKind.Wrath335, "WRATH 3.3.5", "Reading MPQ tables and map list…", true, false);
    static readonly ClientProfile Retail = new(ClientKind.Retail, "RETAIL", "Reading CASC storage and map list…", false, true);

    public static ClientProfile For(ClientKind kind) => kind == ClientKind.Retail ? Retail : Wrath;
}
