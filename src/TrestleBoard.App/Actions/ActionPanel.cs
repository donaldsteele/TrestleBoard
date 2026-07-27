using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TrestleBoard.Editing.Actions;

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

    internal ActionPanel()
    {
        Width = 320;
        Padding = new Avalonia.Thickness(12);

        // Themed by reference, not by value: High Contrast swaps these brushes out from under us
        // and a hard-coded colour would survive the swap and stop meeting the contrast rule.
        this[!BackgroundProperty] =
            new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush");

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

        if (!context.HasFrameSelection && !context.IsEditingText && nextSteps.Count > 0)
        {
            _content.Children.Add(BuildWhatsNext(nextSteps, invoke));
        }

        ActionGroup? currentGroup = null;
        foreach (ActionOffer offer in offers)
        {
            if (currentGroup != offer.Action.Group)
            {
                currentGroup = offer.Action.Group;
                _content.Children.Add(GroupHeading(ActionCatalog.DescribeGroup(currentGroup.Value)));
            }

            _content.Children.Add(BuildOffer(offer, invoke));
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

    private static TextBlock GroupHeading(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 12, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// One offered action. A blocked one keeps its button and gains a sentence saying why — the
    /// button is not disabled, so a screen reader still reaches it and pressing it explains itself.
    /// </summary>
    private static Control BuildOffer(ActionOffer offer, Action<string, Control?> invoke)
    {
        string gesture = offer.Action.DisplayGesture is { } g ? $"  ({g})" : string.Empty;
        var button = new Button
        {
            Content = offer.Action.Title + gesture,
            FontSize = 16,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(button, offer.Action.Title);
        AutomationProperties.SetHelpText(
            button,
            offer.IsAvailable ? offer.Action.ShortDescription : offer.Availability.Reason);
        button.Click += (_, _) => invoke(offer.Action.Id, button);

        if (offer.IsAvailable)
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
                var button = new Button
                {
                    Content = step.Title,
                    FontSize = 16,
                    MinHeight = 44,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
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
