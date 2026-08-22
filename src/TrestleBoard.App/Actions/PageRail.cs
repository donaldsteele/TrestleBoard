using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.App.Actions;

/// <summary>
/// The left-docked row of miniature pages (M76 (g), docs/M76-spec.md §6).
///
/// <para><b>What was wrong.</b> "Page 1 of 5" sat between two buttons, and pressing Next three times
/// was the only route to page four. A four-to-six page newsletter is small enough to show whole, and
/// showing it whole is what a trestle board <i>is</i> — the plan laid out where everyone can see
/// it.</para>
///
/// <para><b>The current page is marked three ways, and only one of them is a colour</b> (PLAN.md §6:
/// colour is never the only signal). An accent bar appears down the left of the tile — its
/// <i>presence</i> is a shape, not a hue — carrying the lodge gold notch, which is legal here and
/// nowhere else because <c>Ornament.OnAccent</c> names its only legal ground and this is precisely
/// that ground. The sheet's border steps up to the focus thickness. And the tile's accessible name
/// says "Page 3 of 5, showing now", so a screen-reader user is told the same fact in the same
/// breath as the page number.</para>
///
/// <para><b>Nothing here is ever greyed.</b> That is M11's rule for the action panel and it is the
/// rule here for the same reason: a control that goes grey has stopped explaining itself. A tile
/// whose page cannot be jumped to, and a move button that cannot move, stay pressable and answer in
/// words through <c>ActionRunner</c> — which is the same sentence the catalog gives the menu bar,
/// carried here in <c>HelpText</c> as well so a screen reader finds it without pressing anything.
/// </para>
///
/// <para><b>Reordering is buttons first.</b> <c>page.moveEarlier</c> and <c>page.moveLater</c> have
/// existed since the page commands landed; what the rail adds is a place where the thing they move
/// is visible while they move it. No drag is implemented: §6 forbids a drag-only path, and a drag
/// that is merely an accelerator can be added later without any of this changing.</para>
/// </summary>
internal sealed class PageRail : Border
{
    /// <summary>
    /// How wide the rail is.
    ///
    /// <para>Set by what has to fit inside it rather than chosen: a miniature page wide enough to
    /// recognise a cover by, and a 16pt label under it, with the rail's own padding round both.
    /// Smaller text was not an option — PLAN.md §6 sets a 16pt floor for chrome and this audience is
    /// the reason it exists — so the width is the number that follows from the floor.</para>
    /// </summary>
    internal const double RailWidth = 184d;

    /// <summary>How wide a miniature sheet is drawn, in device-independent pixels.</summary>
    private const double ThumbnailWidth = 112d;

    /// <summary>
    /// What fraction of full size a page is rendered at. Deliberately about twice
    /// <see cref="ThumbnailWidth"/> over a letter page's 612pt, so the miniature is still crisp on a
    /// high-density display and when an elderly user has raised the UI scale to 200% — the two
    /// occasions a thumbnail rendered exactly to size turns to mush.
    /// </summary>
    internal const float ThumbnailScale = 0.36f;

    /// <summary>
    /// How long after the last edit the miniatures catch up.
    ///
    /// <para>A thumbnail is expensive enough that re-rendering one per keystroke would be felt, and
    /// stale enough after an edit that never re-rendering is a lie about the newsletter. So an edit
    /// marks its page dirty and restarts this; the render happens once, when the typing stops.</para>
    /// </summary>
    private static readonly TimeSpan CatchUpDelay = TimeSpan.FromMilliseconds(500);

    private readonly Func<int, byte[]?> _renderPage;
    private readonly Action<Control> _invoke;
    private readonly TextBlock _heading;
    private readonly StackPanel _tileStack;
    private readonly List<Tile> _tiles = [];
    /// <summary>
    /// The miniatures, keyed by the PAGE'S OWN ID and never by its position.
    ///
    /// <para><b>M76 review.</b> This was an ordinal, and an ordinal is not what a thumbnail is a
    /// picture of. Reordering, adding or deleting a page moves every page at or past the edit
    /// without changing a single miniature, so a rail keyed by position hands back the picture of
    /// whatever page used to be standing there — and the tile's label and accessible name, which are
    /// recomputed correctly on the same pass, then disagree with the picture beside them. Nothing
    /// caught it because a swap leaves the page COUNT alone, so the tiles are not even rebuilt.</para>
    ///
    /// <para>Keyed by identity, the whole class of bug is gone rather than patched: a page carries
    /// its miniature with it wherever it moves, a new page has no entry and so is drawn, and a
    /// deleted page's bitmap is dropped in <see cref="ForgetWhatIsNoLongerHere"/> because its id
    /// stopped being in the newsletter.</para>
    /// </summary>
    private readonly Dictionary<string, Bitmap> _thumbnails = new(StringComparer.Ordinal);

    /// <summary>Which pages have been edited since their miniature was drawn, by id.</summary>
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);

    /// <summary>
    /// The ids of the pages the tiles stand for, in order — the one place an index and an identity
    /// are allowed to meet, rewritten on every refresh before anything else reads it.
    /// </summary>
    private readonly List<string> _pageIds = [];
    private readonly DispatcherTimer _catchUp;
    private readonly Button _moveEarlier;
    private readonly Button _moveLater;

    /// <summary>
    /// How many tiles are built. Starts at -1 rather than 0 so that the FIRST refresh — which
    /// happens in the window's constructor, with no newsletter open — still counts as a change and
    /// builds the empty state. At 0 it would agree with itself and build nothing, and the rail would
    /// stand blank until somebody opened a file.
    /// </summary>
    private int _pageCount = -1;

    /// <summary>
    /// Whether the rail is actually in a window that is on screen.
    ///
    /// <para><b>Nothing is rendered until it is.</b> A miniature costs a full page rasterisation, and
    /// this control is constructed by every <c>MainWindow</c> — including the several hundred the
    /// headless suite makes and never shows. Deferring to the moment the rail joins a visual tree
    /// means the work happens exactly when somebody could see it, which is also the honest reading of
    /// "lazily": a tile that is not on screen has nothing to be out of date about.</para>
    /// </summary>
    private bool _onScreen;

    /// <param name="renderPage">
    /// A PNG of one page, or null when there is no newsletter. Supplied by the shell rather than
    /// reached for, because <c>DocumentRenderSource</c> belongs to the open document and this
    /// control outlives every document the window opens.
    /// </param>
    /// <param name="invoke">
    /// Runs whatever command the pressed control's <c>Tag</c> names — a bare id for the two move
    /// buttons, an <see cref="ActionTarget"/> carrying a page number for a tile. Everything the rail
    /// does goes through the one runner, so a refusal here is worded by the same catalog that words
    /// the menu bar's (M11).
    /// </param>
    internal PageRail(Func<int, byte[]?> renderPage, Action<Control> invoke)
    {
        _renderPage = renderPage;
        _invoke = invoke;

        Width = RailWidth;
        Padding = new Thickness(8);

        // Themed by reference, never by value: High Contrast swaps these brushes out from under us
        // and a hard-coded colour would survive the swap and stop meeting the contrast rule.
        //
        // A real border on all four sides rather than a rule on the one that meets the canvas: the
        // rail is a strip of chrome standing against the page backdrop, and the boundary has to be a
        // shape that survives High Contrast, where Chrome.Surface and Chrome.Background collapse to
        // the same black on purpose and Border.Thickness steps to 2px to do the whole job.
        this.Token(Border.BackgroundProperty, Tokens.ChromeBackground);
        this.Token(Border.BorderBrushProperty, Tokens.ChromeBorder);
        this.Token(Border.BorderThicknessProperty, Tokens.BorderThickness);

        _heading = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Text = "Pages",
        };
        AutomationProperties.SetName(_heading, "Pages");
        AutomationProperties.SetLiveSetting(_heading, AutomationLiveSetting.Polite);

        _tileStack = new StackPanel { Spacing = 8 };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _tileStack,
        };

        // M69's finding, one surface along: an auto-hiding scrollbar draws OVER the content and only
        // appears once the pointer is already moving across it, so there is nothing on screen to say
        // the list continues below the fold. Six pages in a rail is exactly that list.
        ScrollViewer.SetAllowAutoHide(scroller, false);

        _moveEarlier = MoveButton(ActionId.MovePageEarlier);
        _moveLater = MoveButton(ActionId.MovePageLater);

        var moves = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        moves.Children.Add(_moveEarlier);
        moves.Children.Add(_moveLater);
        AutomationProperties.SetName(moves, "Move this page");

        var dock = new DockPanel();
        DockPanel.SetDock(_heading, Dock.Top);
        DockPanel.SetDock(moves, Dock.Bottom);
        dock.Children.Add(_heading);
        dock.Children.Add(moves);
        dock.Children.Add(scroller);
        Child = dock;

        AutomationProperties.SetName(this, "The pages of this newsletter");

        // See _onScreen: the miniatures are drawn when the rail joins a window, not when it is
        // built, and the tiles that were asked for while it was off screen are caught up here.
        AttachedToVisualTree += (_, _) =>
        {
            _onScreen = true;
            DrawTheMissingOnes();
        };
        DetachedFromVisualTree += (_, _) => _onScreen = false;

        _catchUp = new DispatcherTimer { Interval = CatchUpDelay };
        _catchUp.Tick += (_, _) =>
        {
            _catchUp.Stop();
            DrawTheStaleOnes();
        };
    }

    /// <summary>Every tile, in page order, for the tests and for the keyboard walk.</summary>
    internal IReadOnlyList<Button> TilesForTest => [.. _tiles.Select(t => t.Button)];

    /// <summary>The two reorder buttons, so a test can prove they are here and never greyed.</summary>
    internal IReadOnlyList<Button> MoveButtonsForTest => [_moveEarlier, _moveLater];

    /// <summary>What the rail is saying about how many pages there are.</summary>
    internal string HeadingForTest => _heading.Text ?? string.Empty;

    /// <summary>
    /// Rebuilds the rail for the newsletter as it stands now.
    ///
    /// <para><b>In place wherever it can be.</b> This runs from the window's <c>RefreshActions</c>,
    /// which runs after every command and every selection change, and rebuilding the tiles each time
    /// would take the focus out from under a keyboard user in the middle of walking them. Tiles are
    /// therefore only constructed when the number of pages changes; everything else — which tile is
    /// current, what each one says, what the move buttons explain — is written onto the tiles that
    /// are already there.</para>
    /// </summary>
    /// <param name="pageIds">
    /// The id of every page, in the order the newsletter has them. The rail is handed identities
    /// rather than a count because a count cannot tell a reorder from nothing having happened at
    /// all, and a miniature belongs to a page rather than to a position — see
    /// <see cref="_thumbnails"/>.
    /// </param>
    internal void Update(
        IReadOnlyList<string> pageIds,
        int currentPage,
        ActionAvailability goTo,
        ActionAvailability moveEarlier,
        ActionAvailability moveLater)
    {
        ArgumentNullException.ThrowIfNull(pageIds);
        ArgumentNullException.ThrowIfNull(goTo);
        ArgumentNullException.ThrowIfNull(moveEarlier);
        ArgumentNullException.ThrowIfNull(moveLater);

        int pageCount = pageIds.Count;

        // Written BEFORE a tile is touched: everything below resolves a tile's index to the page it
        // is showing through this list.
        _pageIds.Clear();
        _pageIds.AddRange(pageIds);
        ForgetWhatIsNoLongerHere();

        if (pageCount != _pageCount)
        {
            BuildTiles(pageCount);
        }

        _pageCount = pageCount;

        string heading = pageCount switch
        {
            0 => "No newsletter",
            1 => "1 page",
            _ => $"{pageCount} pages",
        };
        _heading.Text = heading;
        AutomationProperties.SetName(_heading, heading);

        for (int index = 0; index < _tiles.Count; index++)
        {
            Tile tile = _tiles[index];
            bool current = index == currentPage;

            // The number the user reads is one-based, because there is no page zero in anybody's
            // newsletter. "showing now" is the third mark on the current tile and the only one a
            // screen reader can hear, which is why it is in the name and not only in the label.
            string name = current
                ? $"Page {index + 1} of {pageCount}, showing now"
                : $"Page {index + 1} of {pageCount}";
            tile.Label.Text = current ? $"Page {index + 1} — showing now" : $"Page {index + 1}";
            AutomationProperties.SetName(tile.Button, name);
            AutomationProperties.SetHelpText(
                tile.Button,
                goTo.IsAvailable
                    ? (current
                        ? "This is the page you are looking at."
                        : $"Shows page {index + 1}.")
                    : goTo.Reason);

            // Three signals for one fact. The bar is a shape that is there or is not; the border
            // steps to the focus thickness, which is a thickness and not a hue; and the name above
            // says it in words. The accent and the gold are the fourth, for everyone who can see
            // them (PLAN.md §6).
            //
            // Guarded on the change rather than written every time: this method runs after every
            // command in the application, and Token() installs a fresh DynamicResource binding each
            // call. The tile's ordinary thickness is bound once, when the tile is built.
            if (tile.Marker.IsVisible != current)
            {
                tile.Marker.IsVisible = current;
                tile.Sheet.Token(
                    Border.BorderThicknessProperty,
                    current ? Tokens.FocusThickness : Tokens.BorderThickness);
            }

            EnsureThumbnail(index, tile);
        }

        // Never `IsEnabled = false`. The reason a page cannot move is a sentence, and a greyed
        // button is how this application used to lose it (M11, PLAN.md §6).
        Explain(_moveEarlier, moveEarlier);
        Explain(_moveLater, moveLater);
    }

    /// <summary>
    /// A page has been edited, so its miniature is out of date. The render is not done here — see
    /// <see cref="CatchUpDelay"/>: this is called once per change to the newsletter, which during
    /// typing is often, and the point of the delay is that the work happens once when the typing
    /// stops.
    /// </summary>
    internal void NoteThePageChanged(int pageIndex)
    {
        // Resolved to an id against the order the rail last saw, which is the order the caller was
        // looking at when it counted. A page whose index is past the end is one the rail has not
        // been told about yet; the refresh that follows the change will draw it.
        if (pageIndex < 0 || pageIndex >= _pageIds.Count)
        {
            return;
        }

        _stale.Add(_pageIds[pageIndex]);
        _catchUp.Stop();
        _catchUp.Start();
    }

    /// <summary>
    /// A different newsletter is open, so every miniature belongs to a document that is gone. Also
    /// called when the window closes, which is what releases the bitmaps.
    /// </summary>
    internal void ForgetEveryThumbnail()
    {
        _catchUp.Stop();
        _stale.Clear();
        foreach (Bitmap bitmap in _thumbnails.Values)
        {
            bitmap.Dispose();
        }

        _thumbnails.Clear();
        foreach (Tile tile in _tiles)
        {
            tile.Picture.Source = null;
        }
    }

    /// <summary>
    /// Drops the miniature of any page that is no longer in the newsletter, and the bitmap with it.
    ///
    /// <para>The other half of keying by identity. A deleted page's picture is not only wrong to
    /// show — it is a bitmap nothing will ever ask for again, and left in the dictionary it would
    /// sit there until the window closed. Undo brings the page back with the same id, so the whole
    /// cost of dropping it is one re-render.</para>
    /// </summary>
    private void ForgetWhatIsNoLongerHere()
    {
        if (_thumbnails.Count == 0)
        {
            return;
        }

        var present = new HashSet<string>(_pageIds, StringComparer.Ordinal);
        foreach (string gone in _thumbnails.Keys.Where(id => !present.Contains(id)).ToList())
        {
            if (_thumbnails.Remove(gone, out Bitmap? bitmap))
            {
                bitmap.Dispose();
            }

            _stale.Remove(gone);
        }
    }

    /// <summary>Which page a tile is showing, or null when the rail has no page at that index.</summary>
    private string? PageIdAt(int index) =>
        index >= 0 && index < _pageIds.Count ? _pageIds[index] : null;

    /// <summary>
    /// Puts the keyboard on a tile — the one the user is looking at, when they ask for the rail from
    /// the menu. Answers false when there is nothing to stand on, so the caller can say so out loud
    /// rather than appearing to have done nothing (M70(c)).
    /// </summary>
    internal bool FocusTile(int index)
    {
        if (index < 0 || index >= _tiles.Count)
        {
            return false;
        }

        Button button = _tiles[index].Button;
        button.BringIntoView();
        return button.Focus();
    }

    /// <summary>The tile the keyboard is standing on, or -1. Used by the arrow walk and its test.</summary>
    internal int FocusedTileForTest =>
        _tiles.FindIndex(t => t.Button.IsFocused);

    private static void Explain(Button button, ActionAvailability availability) =>
        AutomationProperties.SetHelpText(
            button,
            availability.IsAvailable
                ? ActionCatalog.Get(ActionTarget.IdOf(button.Tag)!).ShortDescription
                : availability.Reason);

    /// <summary>
    /// One reorder button. The label is the catalog's own title, so the rail, the Page menu and the
    /// help window all say the same words about the same command (M27).
    /// </summary>
    private Button MoveButton(string actionId)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = ActionCatalog.Get(actionId).Title,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
            },
            FontSize = 16,
            MinHeight = 44,
            Padding = new Thickness(8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = actionId,
        }.Action();

        AutomationProperties.SetName(button, ActionCatalog.Get(actionId).Title);
        ToolTip.SetTip(button, MainWindow.TooltipFor(ActionCatalog.Get(actionId)));
        button.Click += (_, _) => _invoke(button);
        return button;
    }

    private void BuildTiles(int pageCount)
    {
        _tileStack.Children.Clear();
        _tiles.Clear();

        for (int index = 0; index < pageCount; index++)
        {
            Tile tile = BuildTile(index);
            _tiles.Add(tile);
            _tileStack.Children.Add(tile.Button);
        }

        if (pageCount == 0)
        {
            _tileStack.Children.Add(new TextBlock
            {
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Text = "When a newsletter is open, its pages appear here and you can go straight to "
                    + "any one of them.",
            });
        }
    }

    private Tile BuildTile(int index)
    {
        var picture = new Image
        {
            Width = ThumbnailWidth,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // The miniature sheet. Chrome.Surface is the raised plane this milestone added, and a page
        // rail's tiles are named in its declaration as one of the three things it is for (§4).
        var sheet = new Border
        {
            Padding = new Thickness(4),
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = picture,
        };
        sheet.Token(Border.BackgroundProperty, Tokens.ChromeSurface)
            .Token(Border.BorderBrushProperty, Tokens.ChromeBorder)
            .Token(Border.BorderThicknessProperty, Tokens.BorderThickness)
            .Token(Border.CornerRadiusProperty, Tokens.RadiusSurface);

        // The gold notch, on the accent bar and nowhere else: Ornament.OnAccent's key name states
        // its only legal background, and this is it (M16's palette, docs/M76-spec.md §6).
        var notch = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        notch.Token(Border.BackgroundProperty, Tokens.OrnamentOnAccent);

        var marker = new Border
        {
            Width = 12,
            Margin = new Thickness(0, 0, 6, 0),
            IsVisible = false,
            Child = notch,
        };
        marker.Token(Border.BackgroundProperty, Tokens.Accent)
            .Token(Border.CornerRadiusProperty, Tokens.RadiusControl);

        var label = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Text = $"Page {index + 1}",
        };

        var column = new StackPanel();
        column.Children.Add(sheet);
        column.Children.Add(label);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        Grid.SetColumn(marker, 0);
        Grid.SetColumn(column, 1);
        layout.Children.Add(marker);
        layout.Children.Add(column);

        var button = new Button
        {
            Content = layout,
            FontSize = 16,
            MinHeight = 44,
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,

            // The id AND the page it means, in the one Tag every surface in this app dispatches on
            // (M76 (g)). See ActionTarget for why the page number rides beside the id rather than
            // becoming sixty ActionIds of its own.
            Tag = new ActionTarget(ActionId.GoToPage, index),
        }.Action();

        // A tile is a surface rather than a control, and the two radii are different on purpose so
        // they read as different kinds of thing. Set here as a local value, which beats the theme's
        // setter, exactly as the primary button's Theme does on the toolbar.
        button.Token(TemplatedControl.CornerRadiusProperty, Tokens.RadiusSurface);

        button.Click += (_, _) => _invoke(button);

        // TUNNEL, not bubble. Button's own class handler turns Enter into a click, and a bubbling
        // handler here would run after it — so Enter would either fire twice or be impossible to
        // tell apart from the arrow keys. Coming down the tree, this sees the key first and says
        // exactly what each one does.
        button.AddHandler(KeyDownEvent, OnTileKeyDown, RoutingStrategies.Tunnel);

        return new Tile(button, marker, sheet, picture, label);
    }

    /// <summary>
    /// The rail's keyboard, whole: Tab reaches it like any other strip of chrome, the arrows walk
    /// the tiles, Home and End jump to the ends, and Enter goes to the page you are standing on
    /// (PLAN.md §6, docs/M76-spec.md §9).
    ///
    /// <para>Both axes are accepted because a rail is a column that looks like a list and reads like
    /// a row of pages: up/down is what a list-shaped control has taught this audience, and
    /// left/right is what "the page before this one" means to somebody looking at a newsletter.</para>
    /// </summary>
    private void OnTileKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        int index = _tiles.FindIndex(t => ReferenceEquals(t.Button, button));
        if (index < 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down or Key.Right:
                e.Handled = FocusTile(index + 1);
                break;
            case Key.Up or Key.Left:
                e.Handled = FocusTile(index - 1);
                break;
            case Key.Home:
                e.Handled = FocusTile(0);
                break;
            case Key.End:
                e.Handled = FocusTile(_tiles.Count - 1);
                break;
            case Key.Enter:
                _invoke(button);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void DrawTheStaleOnes()
    {
        foreach (string id in _stale.ToList())
        {
            int index = _pageIds.IndexOf(id);
            if (index >= 0 && index < _tiles.Count)
            {
                Draw(index, _tiles[index]);
            }
        }

        _stale.Clear();
    }

    private void EnsureThumbnail(int index, Tile tile)
    {
        if (PageIdAt(index) is { } id && _thumbnails.TryGetValue(id, out Bitmap? cached))
        {
            // Already drawn for this document. A stale one is left exactly as it is until the
            // catch-up timer fires, which is the whole reason the cache exists: this method runs on
            // every refresh, and a refresh is not a reason to redraw five pages.
            tile.Picture.Source = cached;
            return;
        }

        if (_onScreen)
        {
            Draw(index, tile);
        }
    }

    private void DrawTheMissingOnes()
    {
        for (int index = 0; index < _tiles.Count; index++)
        {
            if (PageIdAt(index) is not { } id || !_thumbnails.ContainsKey(id))
            {
                Draw(index, _tiles[index]);
            }
        }
    }

    private void Draw(int index, Tile tile)
    {
        try
        {
            if (PageIdAt(index) is not { } id)
            {
                return;
            }

            if (_renderPage(index) is not { Length: > 0 } png)
            {
                return;
            }

            using var stream = new MemoryStream(png);
            var bitmap = new Bitmap(stream);
            if (_thumbnails.Remove(id, out Bitmap? old))
            {
                old.Dispose();
            }

            _thumbnails[id] = bitmap;
            tile.Picture.Source = bitmap;
        }
        catch (Exception)
        {
            // Deliberately everything. A miniature is a convenience laid over a rail that works
            // without it: every tile still carries its page number, its accessible name and the
            // command that goes there, so the failure a user can see is a blank rectangle rather
            // than a rail that is not on screen. Losing the newsletter over a thumbnail — a decode
            // that fails, a page that cannot be laid out, a platform with no bitmap loader under
            // it — is not a trade this application makes (see ActionRunner for the same boundary).
        }
    }

    /// <summary>The parts of one tile the refresh has to write to, held together so it cannot write
    /// to four parallel lists and get one of them out of step.</summary>
    private sealed record Tile(Button Button, Border Marker, Border Sheet, Image Picture, TextBlock Label);
}
