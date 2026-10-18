namespace ACWorldEdit.Assets;

/// <summary>
/// Client file reference carried in the string fields that already hold asset paths (model paths,
/// texture names, placement paths). A modern client addresses most files by FileDataID, which is
/// written as <c>fdid:123</c>. A colon cannot occur in an MPQ path, so the two forms never collide
/// and caches keyed by these strings keep working unchanged.
/// </summary>
public static class AssetRef
{
    const string Prefix = "fdid:";

    /// <summary>
    /// Suffix that marks a reference as a WMO root file, so the extension checks that tell buildings
    /// from doodads work on references too.
    /// </summary>
    const string WmoSuffix = ".wmo";

    /// <summary>
    /// Build the reference string for a FileDataID. Zero means "no file" and is rejected.
    /// </summary>
    public static string FromFileId(uint fileId, bool wmo = false)
    {
        if (fileId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileId), "FileDataID 0 is not a file.");
        }

        return Prefix + fileId.ToString(System.Globalization.CultureInfo.InvariantCulture) + (wmo ? WmoSuffix : "");
    }

    /// <summary>
    /// True when <paramref name="reference"/> is a well-formed FileDataID reference; the ID is
    /// non-zero.
    /// </summary>
    public static bool TryParse(string reference, out uint fileId)
    {
        fileId = 0;
        var digits = reference.AsSpan();
        if (digits.EndsWith(WmoSuffix, StringComparison.Ordinal))
        {
            digits = digits[..^WmoSuffix.Length];
        }

        return reference.StartsWith(Prefix, StringComparison.Ordinal)
            && uint.TryParse(digits.Length >= Prefix.Length ? digits[Prefix.Length..] : default, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out fileId)
            && fileId != 0;
    }

    /// <summary>
    /// True for any string in the FileDataID form, including malformed ones, so path-only sources
    /// can refuse them instead of treating them as a missing path.
    /// </summary>
    public static bool IsFileReference(string reference) => reference.StartsWith(Prefix, StringComparison.Ordinal);
}
