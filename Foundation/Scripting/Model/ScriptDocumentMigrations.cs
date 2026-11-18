namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// The format gate. A newer file is refused with a clear message and never rewritten; older files
/// run through the migration hooks here.
/// </summary>
public static class ScriptDocumentMigrations
{
    public static void EnsureSupported(int format)
    {
        if (format > ScriptDocumentJson.CurrentFormat || format < 1)
        {
            throw new ScriptFormatException(format);
        }
    }

    public static ScriptDocument Migrate(ScriptDocument document)
    {
        EnsureSupported(document.Format);
        document.Format = ScriptDocumentJson.CurrentFormat;
        return document;
    }
}

/// <summary>A document written in a format this build does not read.</summary>
public sealed class ScriptFormatException : Exception
{
    public ScriptFormatException(int format)
        : base($"Unsupported script document format {format}; this build reads version {ScriptDocumentJson.CurrentFormat}.")
    {
        Format = format;
    }

    public int Format { get; }
}
