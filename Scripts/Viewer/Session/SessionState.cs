namespace ACWorldEdit;

/// <summary>
/// What the open client can do right now. One state replaces the separate busy flag and the
/// open-world button's disabled flag, so a load cannot leave the button out of step with the
/// session. Unavailable also covers a client with no maps: there is nothing to open.
/// </summary>
public enum SessionState
{
    Unavailable,
    Loading,
    Ready
}
