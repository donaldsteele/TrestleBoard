using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TrestleBoard.Editing.Actions;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Actions;

/// <summary>
/// The right-docked panel of things you can do to whatever is selected (PLAN.md §6, §11 M11).
///
/// Two rules govern everything here. Actions that do not apply to the current selection are
/// <em>absent</em> — the panel is headed "A photo is selected", so absence reads as "not about
/// photos". Actions that apply but are blocked are shown with the reason in plain language and
/// remain pressable: **nothing in this panel is ever greyed**, because a greyed control is exactly
/// the thing M11 exists to remove. Pressing a blocked action says why, in the status bar.
/// </summary>
internal sealed class ActionPanel : Border
{
    private readonly TextBlock _heading;
    private readonly StackPanel _content;

    /// <summary>
    /// How wide the panel is. Widened 320 to 360 in M16: <c>BuildOffer</c> used to concatenate the
    /// gesture into the label, and "Wrap text around this  (Ctrl+Shift+W)" is thirty-seven
    /// characters. Moving the gesture to its own line took most of that back; the extra forty pixels
    /// take the rest. <b>Smaller text was rejected outright</b> — PLAN.md §6 sets a 16pt floor for
    /// interactive chrome and this audience is the reason it exists.
    ///
    /// <para>Wrapping is NOT eliminated and should not be. The M15 change that introduced the
    /// wrapping TextBlock fixed <em>clipping</em>, and a half-readable shortcut is worse than two
    /// lines. The goal is that wrapping becomes rare and looks deliberate.</para>
    /// </summary>
    internal const double PanelWidth = 360d;

    internal ActionPanel()
    {
        Width = PanelWidth;
        Padding = new Avalonia.Thickness(12);

        // Themed by reference, not by value: High Contrast swaps these brushes out from under us
        // and a hard-coded colour would survive the swap and stop meeting the contrast rule.
        this.Token(BackgroundProperty, Tokens.ChromeBackground);

        _heading = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 12),
            Text = "No newsletter is open",
        };
        AutomationProperties.SetName(_heading, "No newsletter is open");
        AutomationProperties.SetLiveSetting(_heading, AutomationLiveSetting.Polite);

        _content = new StackPanel { Spacing = 8 };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _content,
        };

        var dock = new DockPanel();
        DockPanel.SetDock(_heading, Dock.Top);
        dock.Children.Add(_heading);
        dock.Children.Add(scroller);
        Child = dock;

        AutomationProperties.SetName(this, "What you can do");
    }

    /// <summary>The panel's heading, which is also what a screen reader announces on a selection change.</summary>
    internal string HeadingForTest => _heading.Text ?? string.Empty;

    /// <summary>
    /// The label a panel button carries.
    ///
    /// <para><b>This is load-bearing and the test on it is not optional.</b> Only one assertion in
    /// the suite reads the returned value for its content; the rest use it to build failure
    /// messages. So if this ever started returning the empty string — which it would have done the
    /// moment M16 changed the button content from a bare TextBlock to an <see cref="IconText"/> —
    /// the audit suite would have stayed green while quietly auditing nothing.</para>
    /// </summary>
    internal static string LabelOf(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.Content switch
        {
            IconText offer => offer.Label.Text ?? string.Empty,
            TextBlock text => text.Text ?? string.Empty,
            string s => s,
            _ => string.Empty,
        };
    }

    /// <summary>Every button currently offered, for the audit tests.</summary>
    internal IReadOnlyList<Button> ButtonsForTest =>
        _content.Children.OfType<Button>().Concat(
            _content.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>())).ToList();

    /// <summary>
    /// Rebuilds the panel for the current selection. Called from one place — the window's
    /// RefreshActions — so the panel cannot disagree with the menu bar about what is possible.
    /// </summary>
    internal void Update(
        ActionContext context,
        IReadOnlyList<ActionOffer> offers,
        IReadOnlyList<NextStep> nextSteps,
        Action<string, Control?> invoke)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(nextSteps);
        ArgumentNullException.ThrowIfNull(invoke);

        string heading = ActionCatalog.DescribeSelection(context);
        _heading.Text = heading;
        AutomationProperties.SetName(_heading, heading);

        _content.Children.Clear();

        // M17: how this thing is edited, when a click on it does not say so by itself. A polite
        // live region, so a screen-reader user hears it on the selection change too.
        if (ActionCatalog.DescribeSelectionHint(context) is { Length: > 0 } hint)
        {
            var caption = new TextBlock
            {
                Text = hint,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
            };
            AutomationProperties.SetLiveSetting(caption, AutomationLiveSetting.Polite);
            _content.Children.Add(caption);
        }

        // M14: one of the three ways the user can tell this text carries a font of its own. The
        // other two are the View overlay and the styles window's footer — all three are needed,
        // because each answers the question from a different place.
        if (context.FontOverrideNote is { Length: > 0 } note)
        {
            var line = new TextBlock
            {
                Text = note,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
            };
            AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
            _content.Children.Add(line);
        }

        if (!context.HasFrameSelection && !context.IsEditingText && nextSteps.Count > 0)
        {
            _content.Children.Add(BuildWhatsNext(nextSteps, invoke));
        }

        ActionGroup? currentGroup = null;
        string? lastReasonShown = null;
        foreach (ActionOffer offer in offers)
        {
            if (currentGroup != offer.Action.Group)
            {
                currentGroup = offer.Action.Group;
                lastReasonShown = null;
                _content.Children.Add(GroupHeading(ActionCatalog.DescribeGroup(currentGroup.Value)));
            }

            // M18: four picture commands blocked for the SAME reason printed that reason four
            // times, which is four times the reading for one fact. The sentence is printed once
            // per run of buttons that share it; every button still carries it in its HelpText, so
            // a screen-reader user hears the reason on whichever one they land on.
            bool repeated = !offer.IsAvailable
                && string.Equals(offer.Availability.Reason, lastReasonShown, StringComparison.Ordinal);
            lastReasonShown = offer.IsAvailable ? null : offer.Availability.Reason;

            _content.Children.Add(BuildOffer(offer, context, invoke, repeated));
        }

        if (_content.Children.Count == 0)
        {
            _content.Children.Add(new TextBlock
            {
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Text = "Choose something on the page, and what you can do to it appears here.",
            });
        }
    }

    /// <summary>
    /// One full-width panel button.
    ///
    /// <para>The content is an <see cref="IconText"/>, which is a DockPanel and must stay one: a
    /// horizontal StackPanel measures its children with infinite width, so the label would never
    /// wrap and the clipping defect of 2026-07-27 would come straight back.</para>
    ///
    /// <para><b>Primary emphasis cashes <c>EditorAction.IsPrimary</c></b>, which has marked the right
    /// ten actions since M11 and which nothing read until M16. A primary offer gets the accent fill,
    /// a taller minimum and a gold left bar — <b>three signals, so colour is never the only
    /// one</b>. The other half of that field's old summary, "primary actions sort first in their
    /// group", is deliberately not implemented and the summary now says so: declaration order in
    /// ActionCatalog already agrees with it, so a sort would be a near-no-op carrying real risk of
    /// reordering a group a test depends on.</para>
    /// </summary>
    private static Button PanelButton(string label, string? actionId, string? gesture, bool isPrimary)
    {
        var button = new Button
        {
            Content = new IconText(
                actionId is null ? null : ActionIcons.ForAction(actionId),
                label,
                labelSize: 16,
                secondary: gesture,
                mutedSecondary: !isPrimary),
            FontSize = 16,
            MinHeight = isPrimary ? 56 : 44,
            Padding = new Avalonia.Thickness(isPrimary ? 12 : 10, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        if (isPrimary)
        {
            button.Primary();
        }

        return button;
    }

    /// <summary>
    /// A group heading that reads as a heading. The grouping the owner asked for in his review of
    /// the M15 screenshots already existed — <c>ActionCatalog.PanelGroups</c> orders the groups per
    /// selection and <c>DescribeGroup</c> gives each a plain-language name. What was missing was any
    /// visual difference between a heading and the buttons under it: 16pt SemiBold above 16pt
    /// buttons is not a hierarchy. Accent colour, a hairline rule and real space are.
    ///
    /// <para><b>Static, never collapsible.</b> A collapse control would make absence from the panel
    /// ambiguous for the first time, and absence meaning "not about photos" is the rule M11 exists
    /// to enforce.</para>
    /// </summary>
    private static StackPanel GroupHeading(string text)
    {
        var heading = new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        }.Token(TextBlock.ForegroundProperty, Tokens.Accent);

        var rule = new Border { Height = 1, Margin = new Avalonia.Thickness(0, 0, 0, 4) }
            .Token(BackgroundProperty, Tokens.ChromeDivider);

        var stack = new StackPanel { Margin = new Avalonia.Thickness(0, 16, 0, 0) };
        stack.Children.Add(heading);
        stack.Children.Add(rule);
        return stack;
    }

    /// <summary>
    /// One offered action. A blocked one keeps its button and gains a sentence saying why — the
    /// button is not disabled, so a screen reader still reaches it and pressing it explains itself.
    /// </summary>
    /// <param name="reasonAlreadyShown">
    /// The button above this one is blocked for the same reason and has already printed it. The
    /// sentence is left off here; it stays in <c>HelpText</c>, so nothing is hidden from a screen
    /// reader — only from a reader who has just read it.
    /// </param>
    private static Control BuildOffer(
        ActionOffer offer,
        ActionContext context,
        Action<string, Control?> invoke,
        bool reasonAlreadyShown = false)
    {
        // The gesture is a second, quieter line rather than a suffix on the title. That removes
        // fourteen to sixteen characters from most buttons and changes nothing a screen reader
        // hears, because AutomationProperties.Name below has always carried the title alone.
        //
        // M18: the title comes from the catalog's context-aware TitleFor rather than straight off
        // the declaration, so "Put a picture here…" becomes "Swap this picture…" once there is one.
        string title = ActionCatalog.TitleFor(offer.Action.Id, context);
        Button button = PanelButton(
            title, offer.Action.Id, offer.Action.DisplayGesture, offer.Action.IsPrimary);
        AutomationProperties.SetName(button, title);
        AutomationProperties.SetHelpText(
            button,
            offer.IsAvailable ? offer.Action.ShortDescription : offer.Availability.Reason);
        button.Click += (_, _) => invoke(offer.Action.Id, button);

        if (offer.IsAvailable || reasonAlreadyShown)
        {
            return button;
        }

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(button);
        stack.Children.Add(new TextBlock
        {
            Text = offer.Availability.Reason,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(4, 0, 4, 4),
        });
        return stack;
    }

    /// <summary>
    /// The no-selection state: what this newsletter still needs, one row per suggestion with a
    /// sentence saying why it is being suggested. This is where the monthly workflow becomes
    /// visible outside the start dialog.
    /// </summary>
    private static StackPanel BuildWhatsNext(IReadOnlyList<NextStep> steps, Action<string, Control?> invoke)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(GroupHeading("What's next"));

        foreach (NextStep step in steps)
        {
            stack.Children.Add(new TextBlock
            {
                Text = step.Why,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(4, 4, 4, 0),
            });

            if (step.ActionId is { } actionId)
            {
                Button button = PanelButton(step.Title, actionId, gesture: null, isPrimary: false);
                AutomationProperties.SetName(button, step.Title);
                AutomationProperties.SetHelpText(button, step.Why);
                button.Click += (_, _) => invoke(actionId, button);
                stack.Children.Add(button);
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = step.Title,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(4, 0, 4, 4),
                });
            }
        }

        return stack;
    }
}
