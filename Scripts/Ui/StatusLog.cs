namespace ACWorldEdit;

/// <summary>
/// Recent status messages for the status bar: the newest shows on the bar and the rest stay readable
/// in the history window. Main-thread only.
/// </summary>
public sealed class StatusLog
{
    const int Capacity = 200;
    readonly List<(DateTime Time, string Text)> entries = [];

    /// <summary>Raised after every posted message.</summary>
    public event Action? Posted;

    public IReadOnlyList<(DateTime Time, string Text)> Entries => entries;
    public string Latest => entries.Count > 0 ? entries[^1].Text : "";

    public void Post(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        entries.Add((DateTime.Now, text));
        if (entries.Count > Capacity)
        {
            entries.RemoveAt(0);
        }

        Posted?.Invoke();
    }

    public void Clear()
    {
        entries.Clear();
        Posted?.Invoke();
    }
}
