namespace TrestleBoard.App.Settings;

/// <summary>A rectangle in screen pixels. Deliberately not an Avalonia type — see the class below.</summary>
internal readonly record struct PlacementRect(int Left, int Top, int Width, int Height)
{
    internal int Right => Left + Width;

    internal int Bottom => Top + Height;

    /// <summary>Whether these two rectangles share any pixel at all.</summary>
    internal bool Overlaps(PlacementRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    /// <summary>How many pixels the two share. Used to insist on more than a sliver.</summary>
    internal long OverlapArea(PlacementRect other)
    {
        long width = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        long height = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return width <= 0 || height <= 0 ? 0 : width * height;
    }
}

/// <summary>
/// Where the window should open (PLAN.md §11 M77).
///
/// <para><b>Why this is a pure function over plain numbers.</b> The rule that matters — "put it
/// back where it was, unless where it was no longer exists" — is exactly the rule that cannot be
/// checked by looking at the app, because checking it means unplugging a monitor. Screens come and
/// go: a laptop that spent last month docked to a second display opens today with one screen, and a
/// window restored to where the second one used to be is a window the user cannot see, cannot
/// move and cannot close. So the decision is taken here, over numbers a test can invent, and the
/// shell does nothing but hand over what Avalonia told it.</para>
///
/// <para><b>An overlap, not a containment.</b> Requiring the whole window to fit inside one screen
/// would refuse the ordinary case of a window nudged a little off the right-hand edge, which the
/// user put there on purpose. Requiring a single shared pixel would accept a window whose title bar
/// is the only thing on screen. The rule is a quarter of the window's area, which is enough to take
/// hold of and drag back.</para>
/// </summary>
internal static class WindowPlacement
{
    /// <summary>The size M76 settled on, used whenever the remembered one is refused.</summary>
    internal const int DefaultWidth = 1280;

    internal const int DefaultHeight = 860;

    /// <summary>Smaller than this and the toolbar cannot be laid out; a remembered size below it is
    /// treated as damage rather than as a preference.</summary>
    internal const int MinimumWidth = 800;

    internal const int MinimumHeight = 600;

    /// <summary>What fraction of the window has to be on a screen for the position to be kept.</summary>
    private const double EnoughToGrab = 0.25;

    /// <summary>
    /// The size to open at. A remembered size is honoured when it is big enough to hold the window's
    /// own chrome, and otherwise replaced — never clamped silently to something in between, because
    /// a size the user has never seen is not a preference either.
    /// </summary>
    internal static (int Width, int Height) ChooseSize(int? rememberedWidth, int? rememberedHeight)
    {
        if (rememberedWidth is not { } width || rememberedHeight is not { } height)
        {
            return (DefaultWidth, DefaultHeight);
        }

        return width >= MinimumWidth && height >= MinimumHeight
            ? (width, height)
            : (DefaultWidth, DefaultHeight);
    }

    /// <summary>
    /// Whether the remembered position can be used, given the screens that exist right now.
    /// </summary>
    /// <param name="remembered">Where the window was when it last closed.</param>
    /// <param name="screens">Every connected screen's area, in the same coordinates.</param>
    /// <returns>
    /// False when the position must be thrown away and the window centred instead — no screens at
    /// all (which is what a headless session reports), or not enough of the window on any of them.
    /// </returns>
    internal static bool CanRestore(PlacementRect remembered, IReadOnlyList<PlacementRect> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (screens.Count == 0 || remembered.Width <= 0 || remembered.Height <= 0)
        {
            return false;
        }

        double needed = (double)remembered.Width * remembered.Height * EnoughToGrab;
        return screens.Any(screen => screen.OverlapArea(remembered) >= needed);
    }

    /// <summary>
    /// What to write back when the window closes.
    ///
    /// <para><b>A maximised window keeps the size it had before it was maximised.</b> Its current
    /// width and height are the whole screen, and writing those would mean that un-maximising next
    /// time landed on a window the size of the monitor — which is not a size the user ever chose.
    /// The maximised flag carries the intent; the numbers underneath it stay as they were.</para>
    ///
    /// <para>A pure function over plain numbers, and that is not tidiness. The first version of this
    /// rule lived in the window and was tested by maximising a headless window — which does not
    /// change its reported width, so the test passed against code that had the rule backwards. The
    /// seam could not express the failure. This one can: the caller says what the window reports,
    /// so a test can say "it reports the whole screen" without owning a screen.</para>
    /// </summary>
    internal static AppSettings Remember(
        AppSettings previous, bool maximised, int width, int height, int left, int top)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return previous with
        {
            WindowMaximised = maximised,
            WindowWidth = maximised ? previous.WindowWidth : width,
            WindowHeight = maximised ? previous.WindowHeight : height,
            WindowLeft = maximised ? previous.WindowLeft : left,
            WindowTop = maximised ? previous.WindowTop : top,
        };
    }
}
