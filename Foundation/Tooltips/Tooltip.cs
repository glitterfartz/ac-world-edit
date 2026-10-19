namespace ACWorldEdit.Foundation;

/// <summary>How a tooltip line is coloured. The renderer maps tones to colours, so builders stay UI-free.</summary>
public enum TooltipTone
{
    Normal,
    /// <summary>Coloured by <see cref="TooltipLine.Quality"/>; used for the name.</summary>
    Title,
    Muted,
    /// <summary>Green: "Equip:" effects.</summary>
    Good,
    /// <summary>Red: unmet or unusable.</summary>
    Bad,
    /// <summary>Yellow: flavour text and notes.</summary>
    Flavor,
    /// <summary>Soft gold: sell price and other money lines.</summary>
    Gold
}

/// <summary>One row of a tooltip: text on the left and optional text pushed to the right edge.</summary>
public sealed record TooltipLine(string Left, string Right = "", TooltipTone Tone = TooltipTone.Normal, int Quality = 0);

/// <summary>
/// What a game-style tooltip shows, independent of how it is drawn. Item, spell and other builders
/// produce one of these and the UI renders it, so each kind of tooltip needs no UI code of its own.
/// </summary>
public sealed record Tooltip(IReadOnlyList<TooltipLine> Lines);
