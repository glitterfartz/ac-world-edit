using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Base of the TrinityCore creature sections: they draw the read model (<see cref="TcCreatureView"/>)
/// with editing enabled separately by each implemented page. They use the AzerothCore sections' ids, so
/// opening the window on "gossip" or "vendor" finds the page of the open database.
/// </summary>
public abstract partial class TcSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override bool Supports(WorldDialect dialect) => dialect == WorldDialect.TrinityCore;

    /// <summary>Fill the section for a loaded creature.</summary>
    protected abstract void Populate(TcCreatureView v);

    public override void Refresh()
    {
        Clear();
        if (Ctx.TcView is { } v)
        {
            Populate(v);
        }
        else
        {
            AddChild(Placeholder());
        }
    }

    protected void Heading(string text) => AddChild(ClientStyle.Text(text, 16, ClientStyle.Header));

    protected Label Line(string text, Color? color = null, int size = 13, Control? parent = null)
    {
        var label = ClientStyle.Text(text, size, color ?? ClientStyle.Body);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        (parent ?? this).AddChild(label);
        return label;
    }

    /// <summary>A two-column grid of caption/value pairs; values that are 0 or empty are still shown.</summary>
    protected void Facts(IEnumerable<(string Caption, string Value)> facts, Control? parent = null)
    {
        var grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 2);
        (parent ?? this).AddChild(grid);
        foreach (var (caption, value) in facts)
        {
            grid.AddChild(ClientStyle.Tag(caption));
            var label = ClientStyle.Text(value, 13, ClientStyle.Body);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(label);
        }
    }

    /// <summary>An indented block for one menu, trainer or item.</summary>
    protected VBoxContainer Block()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        margin.AddChild(box);
        return box;
    }

    protected static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// One search box's term and last results, kept across redraws. <see cref="Reset"/> (creature
    /// changed) also drops results of a search still running.
    /// </summary>
    protected sealed class SearchState<T>
    {
        public string Term = "";
        public IReadOnlyList<T> Results = [];
        public int Generation;
        public bool Searching;

        public void Reset()
        {
            Generation++;
            Term = "";
            Results = [];
            Searching = false;
        }
    }

    /// <summary>
    /// A search box and button: the term is trimmed to 80 characters and <paramref name="run"/> fills
    /// <see cref="SearchState{T}.Results"/>, reporting "n {noun}(s) found." on the status line. The
    /// button is disabled while searching and, when <paramref name="needsClient"/>, without a client lookup.
    /// </summary>
    protected void SearchBox<T>(Control parent, SearchState<T> state, string name, string placeholder, string noun, bool needsClient, Func<string, Task<IReadOnlyList<T>>> run)
    {
        var box = new HBoxContainer();
        parent.AddChild(box);
        var term = new LineEdit { Name = name, Text = state.Term, PlaceholderText = placeholder, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var search = new Button { Text = "Search", Disabled = state.Searching || (needsClient && Ctx.Lookup() == null) };
        term.TextChanged += text => state.Term = text;
        async void Search()
        {
            if (state.Searching || (needsClient && Ctx.Lookup() == null)) return;
            string text = term.Text.Trim();
            if (text.Length == 0) return;
            text = text[..Math.Min(80, text.Length)];
            state.Term = text;
            int generation = ++state.Generation;
            state.Searching = true;
            Ctx.Status = $"Searching {noun}s…";
            Ctx.Redraw();
            try
            {
                var found = await run(text);
                if (generation != state.Generation) return;
                state.Results = found;
                Ctx.Status = $"{found.Count} {noun}(s) found.";
            }
            catch (Exception error)
            {
                if (generation != state.Generation) return;
                Ctx.Status = DbErrors.Safe(error);
            }
            if (generation != state.Generation) return;
            state.Searching = false;
            Ctx.Redraw();
        }
        search.Pressed += Search;
        term.TextSubmitted += _ => Search();
        box.AddChild(term);
        box.AddChild(search);
    }
}
