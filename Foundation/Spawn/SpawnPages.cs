namespace ACWorldEdit.Foundation;

/// <summary>
/// Paging over a template's spawn list, shared by the creature and GameObject Spawns pages.
/// Pages are zero-based; an empty list has no pages.
/// </summary>
public static class SpawnPages
{
    public const int PageSize = 25;

    public static int PageCount(int total) => total <= 0 ? 0 : (total + PageSize - 1) / PageSize;

    public static int Clamp(int page, int total) => total <= 0 ? 0 : Math.Clamp(page, 0, PageCount(total) - 1);

    /// <summary>The slice of the list shown on this page: start index and row count.</summary>
    public static (int Start, int Count) Slice(int page, int total)
    {
        page = Clamp(page, total);
        int start = page * PageSize;
        return (start, Math.Max(0, Math.Min(PageSize, total - start)));
    }
}
