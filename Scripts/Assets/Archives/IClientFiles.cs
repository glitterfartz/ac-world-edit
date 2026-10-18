namespace ACWorldEdit.Assets;

/// <summary>
/// Which client family a file source reads. The viewer picks one implementation per loaded client;
/// parsers never branch on it.
/// </summary>
public enum ClientKind
{
    Wrath335,
    Retail,
}

/// <summary>
/// Read-only source of client files. Implementations return detached bytes so workers can parse
/// without holding archive state, and must be safe to call from several threads. Disposal waits for
/// the owner to stop its workers first.
/// </summary>
public interface IClientFiles : IDisposable
{
    ClientKind Kind { get; }

    /// <summary>
    /// Client build string, such as 3.3.5.12340 or 12.1.5.70448.
    /// </summary>
    string Build { get; }

    /// <summary>
    /// Read one file by client path or by an <see cref="AssetRef"/> FileDataID reference. A missing
    /// file throws <see cref="FileNotFoundException"/>; a reference kind this client cannot address
    /// throws <see cref="NotSupportedException"/>.
    /// </summary>
    byte[] Read(string path);

    /// <summary>
    /// True when the file exists. Unaddressable reference kinds report false rather than throwing.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Provenance of the winning copy for validation output, or "missing".
    /// </summary>
    string Source(string path);
}
