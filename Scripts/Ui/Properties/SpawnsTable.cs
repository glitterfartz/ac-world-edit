using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>One spawn row both Spawns pages list: GUID, map and server position.</summary>
public sealed record SpawnListRow(ulong Guid, uint Map, double X, double Y, double Z);

/// <summary>
/// The paginated spawn table shared by the creature and GameObject Spawns pages: GUID, map and
/// position columns with a per-row Fly to button, plus a pager. Page state lives in the caller,
/// which redraws after <paramref name="setPage"/>.
/// </summary>
public static class SpawnsTable
{
    /// <summary>
    /// Draw the table for this page of <paramref name="spawns"/> (at most SpawnPages.PageSize rows)
    /// under a header naming the total. <paramref name="fly"/> teleports to a row and returns the
    /// status line; <paramref name="report"/> shows it.
    /// </summary>
    public static void Render(VBoxContainer parent, IReadOnlyList<SpawnListRow> spawns, int total, int page, Action<int> setPage, Func<SpawnListRow, Task<string>> fly, Action<string> report)
    {
        page = SpawnPages.Clamp(page, total);
        var (start, count) = SpawnPages.Slice(page, total);
        var rows = spawns.Skip(start).Take(count).ToList();

        parent.AddChild(ClientStyle.Text($"Spawns ({total})", 16, ClientStyle.Header));
        if (total == 0)
        {
            parent.AddChild(ClientStyle.Text("No spawns use this template yet.", 13, ClientStyle.Faded));
            return;
        }

        var grid = new GridContainer { Columns = 4, Name = "SpawnTable" };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        parent.AddChild(grid);
        foreach (string heading in new[] { "GUID", "Map", "Position", "" })
        {
            grid.AddChild(ClientStyle.Text(heading, 12, ClientStyle.Faded));
        }

        foreach (var row in rows)
        {
            grid.AddChild(ClientStyle.Text(row.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture), 13));
            grid.AddChild(ClientStyle.Text(row.Map.ToString(System.Globalization.CultureInfo.InvariantCulture), 13));
            var position = ClientStyle.Text($"({row.X:0.##}, {row.Y:0.##}, {row.Z:0.##})", 13);
            position.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(position);
            var button = new Button { Text = "Fly to", Name = "FlyTo_" + row.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture), TooltipText = $"Teleport to spawn #{row.Guid}." };
            button.Pressed += () => Fly(row);
            grid.AddChild(button);
        }

        if (spawns.Count < total)
        {
            parent.AddChild(ClientStyle.Text($"First {spawns.Count} of {total} shown.", 11, ClientStyle.Faded));
        }

        int pages = SpawnPages.PageCount(total);
        if (pages <= 1)
        {
            return;
        }

        var nav = new HBoxContainer { Name = "SpawnPager" };
        nav.AddThemeConstantOverride("separation", 8);
        parent.AddChild(nav);
        var previous = new Button { Text = "◂ Prev", Name = "SpawnPrev", Disabled = page == 0 };
        previous.Pressed += () => setPage(page - 1);
        nav.AddChild(previous);
        var next = new Button { Text = "Next ▸", Name = "SpawnNext", Disabled = page >= pages - 1 };
        next.Pressed += () => setPage(page + 1);
        nav.AddChild(next);
        var label = ClientStyle.Text(pages <= 1 ? $"{total} spawns" : $"Page {page + 1} of {pages} · {total} spawns", 11, ClientStyle.Faded);
        label.Name = "SpawnPage";
        nav.AddChild(label);

        async void Fly(SpawnListRow row)
        {
            report($"Locating spawn #{row.Guid}…");
            report(await fly(row));
        }
    }
}
