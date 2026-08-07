using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The per-office diff shown before the officers table is filled in from the address book
/// (PLAN.md §11 M19), modelled line for line on <see cref="BirthdaySyncDialog"/>: M12's import-wizard
/// voice, 20pt, plain sentences, and the promise <em>nothing changes until you press the button</em>
/// written where the user is looking.
///
/// <para>Three rules this window exists to keep, and none of them is cosmetic:</para>
/// <list type="number">
/// <item><description><b>Per-row accept.</b> Every proposed change carries its own tick box, on by
/// default. The officers table is page 2 of a newsletter that goes to the whole lodge, and "all or
/// nothing" would make one wrong row a reason to abandon the other eleven.</description></item>
/// <item><description><b>Never auto-pick between two claimants.</b> When the address book says two
/// men hold the same office, the row shows both and starts with NEITHER chosen. Its tick box does
/// nothing until the user picks.</description></item>
/// <item><description><b>Unrecognised offices are shown, never guessed.</b> They sit above the fold
/// with the sentence that says the row is being left alone.</description></item>
/// </list>
///
/// <para>This window decides nothing else: <see cref="OfficersRosterProjection.Plan"/> has already
/// worked out the answer, and the shell commits it in one undo step.</para>
/// </summary>
internal sealed class OfficersSyncDialog : Window
{
    private readonly Dictionary<string, OfficerCandidate?> _chosen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _accepted = new(StringComparer.Ordinal);
    private readonly HashSet<string> _phoneWriteBacks = new(StringComparer.Ordinal);
    private readonly OfficersProjection _plan;

    /// <summary>
    /// Internal rather than private so the screenshot harness can build one without showing it
    /// modally. Nothing else constructs it directly — <see cref="AskAsync"/> is the door.
    /// </summary>
    internal OfficersSyncDialog(OfficersProjection plan, bool inserting)
    {
        _plan = plan;
        Title = inserting ? "Fill in the officers?" : "Update the officers table?";
        MinWidth = 720;
        Width = 780;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, Title);

        string confirmText = inserting ? "Yes, fill them in" : "Update the table";
        Button confirm = MakeButton(confirmText);
        confirm.IsDefault = true;
        confirm.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };

        Button leave = MakeButton(inserting ? "No, I'll type them myself" : "Leave it as it is");
        leave.IsCancel = true;
        leave.Click += (_, _) => Close();

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = Headline(plan, inserting),
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });

        // Above the fold, in this order: what we could not place, then what needs the user to
        // choose, then the ordinary changes. The two things only a human can settle come first.
        AddUnrecognised(body, plan.Unrecognised);
        AddProposals(body, "You will need to choose", plan.Proposals.Where(p => p.IsAmbiguous).ToList());
        AddProposals(body, "These will be filled in", plan.Proposals.Where(p => !p.IsAmbiguous).ToList());
        AddKeptManual(body, plan.KeptManual);

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Spacing = 12,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Nothing changes until you press “" + confirmText + "”.",
                            FontSize = 18,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { leave, confirm },
                        },
                    },
                },
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = body,
                },
            },
        };
    }

    /// <summary>True when the user asked for the change. Nothing has been applied either way.</summary>
    internal bool Confirmed { get; private set; }

    /// <summary>
    /// The rows the user ticked, with the man they chose where there was a choice to make. An
    /// ambiguous row with no choice made never appears here, however hard the tick box is pressed.
    /// </summary>
    internal IReadOnlyList<OfficerDecision> Decisions =>
    [
        .. _plan.Proposals
            .Where(p => _accepted.Contains(p.Position))
            .Select(p => new OfficerDecision(p.Position, Chosen(p)))
            .Where(d => !IsUndecided(d)),
    ];

    /// <summary>
    /// Phone numbers the user agreed to leave alone in the address book are not here; this is the
    /// other direction — numbers the TABLE holds and the book does not, offered back to the book.
    /// Off by default, and only ever about a man the user has actually accepted (M13's rule).
    /// </summary>
    internal IReadOnlyList<(string MemberId, string Phone)> PhoneWriteBacks =>
    [
        .. _plan.Proposals
            .Where(p => _accepted.Contains(p.Position) && _phoneWriteBacks.Contains(p.Position))
            .Select(p => (Chosen(p), p.CurrentPhone))
            .Where(x => x.Item1 is not null && !string.IsNullOrWhiteSpace(x.CurrentPhone))
            .Select(x => (x.Item1!.MemberId, x.CurrentPhone!)),
    ];

    /// <summary>Shows the diff and answers whether to go ahead.</summary>
    internal static async Task<OfficersSyncDialog> AskAsync(
        Window owner, OfficersProjection plan, bool inserting)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(plan);

        var dialog = new OfficersSyncDialog(plan, inserting);
        await dialog.ShowDialog(owner);
        return dialog;
    }

    /// <summary>"14 July 2026", from a round-trip UTC stamp. Invariant, so it is the same words on
    /// every machine that opens this newsletter.</summary>
    internal static string WhenText(string? generatedUtc) =>
        DateTimeOffset.TryParse(
            generatedUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset when)
            ? when.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            : string.Empty;

    private static string Headline(OfficersProjection plan, bool inserting)
    {
        int rows = plan.Proposals.Count;
        if (rows == 0)
        {
            return "There is nothing to fill in from your address book just now.";
        }

        if (inserting)
        {
            return rows == 1
                ? "We found one office in your address book. Fill it in?"
                : $"We found {rows} offices in your address book. Fill them in?";
        }

        return "Here is what would change in the officers table.";
    }

    /// <summary>
    /// Who would go into this row. When the user has asked to keep the number that is on the page,
    /// the page's number is what goes into the table too — otherwise the write-back would push one
    /// number into the address book while the table printed the other, which is the sort of quiet
    /// disagreement this whole milestone exists to prevent.
    /// </summary>
    private OfficerCandidate? Chosen(OfficerProposal proposal)
    {
        OfficerCandidate? candidate = proposal.IsAmbiguous
            ? _chosen.GetValueOrDefault(proposal.Position)
            : proposal.Only;

        return candidate is not null && _phoneWriteBacks.Contains(proposal.Position)
            ? candidate with { Phone = proposal.CurrentPhone }
            : candidate;
    }

    /// <summary>A contested row the user never settled. It is dropped rather than guessed at.</summary>
    private bool IsUndecided(OfficerDecision decision) =>
        decision.Chosen is null
        && _plan.Proposals.First(p => p.Position == decision.Position).IsAmbiguous;

    private static void AddUnrecognised(Panel body, IReadOnlyList<UnrecognisedOffice> unrecognised)
    {
        if (unrecognised.Count == 0)
        {
            return;
        }

        body.Children.Add(SectionHeading($"We didn't recognise these ({unrecognised.Count})"));
        body.Children.Add(new TextBlock
        {
            Text = "These offices are written in your address book in words TrestleBoard does not "
                + "know. Nothing is done about them — the rows stay exactly as they are.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16, 0, 0, 4),
        });

        foreach (UnrecognisedOffice entry in unrecognised)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"{entry.Name} — “{entry.Office}”",
                FontSize = 18,
                Margin = new Avalonia.Thickness(16, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void AddProposals(Panel body, string heading, List<OfficerProposal> proposals)
    {
        if (proposals.Count == 0)
        {
            return;
        }

        body.Children.Add(SectionHeading($"{heading} ({proposals.Count})"));
        foreach (OfficerProposal proposal in proposals)
        {
            body.Children.Add(proposal.IsAmbiguous ? ContestedRow(proposal) : PlainRow(proposal));
        }
    }

    private static void AddKeptManual(Panel body, IReadOnlyList<OfficerEntry> kept)
    {
        List<OfficerEntry> named = [.. kept.Where(k => k.Name.Length > 0)];
        if (named.Count == 0)
        {
            return;
        }

        body.Children.Add(SectionHeading($"These are yours, and will be left alone ({named.Count})"));
        foreach (OfficerEntry entry in named)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"{entry.Position} — {entry.Name}",
                FontSize = 18,
                Margin = new Avalonia.Thickness(16, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    /// <summary>One office where the address book gives one answer: current → proposed, with a tick.</summary>
    private Control PlainRow(OfficerProposal proposal)
    {
        _accepted.Add(proposal.Position);

        string was = proposal.CurrentName.Length > 0 ? proposal.CurrentName : "nobody";
        string now = proposal.Only is { } candidate ? candidate.Name : "nobody (it will print as vacant)";
        string text = $"{proposal.Position}: {was} → {now}";

        var box = new CheckBox
        {
            Content = text,
            FontSize = 18,
            MinHeight = 44,
            IsChecked = true,
            Margin = new Avalonia.Thickness(16, 0, 0, 0),
        };
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) => Accept(proposal.Position, box.IsChecked == true);

        Control? writeBack = PhoneWriteBackBox(proposal);
        if (writeBack is null)
        {
            return box;
        }

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(box);
        stack.Children.Add(writeBack);
        return stack;
    }

    /// <summary>
    /// One office two men claim. Both are offered, neither is chosen, and the tick box below them
    /// applies nothing until one is. This is the rule the whole milestone turns on.
    /// </summary>
    private StackPanel ContestedRow(OfficerProposal proposal)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(16, 8, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = $"{proposal.Position}: your address book names "
                + $"{proposal.Candidates.Count} people. Which one holds it?",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        var accept = new CheckBox
        {
            Content = $"Put the one I chose into the {proposal.Position} row",
            FontSize = 18,
            MinHeight = 44,
            IsChecked = false,
            IsEnabled = false,
        };
        AutomationProperties.SetName(accept, (string)accept.Content);
        accept.IsCheckedChanged += (_, _) => Accept(proposal.Position, accept.IsChecked == true);

        string group = "office-" + proposal.Position;
        foreach (OfficerCandidate candidate in proposal.Candidates)
        {
            string label = candidate.Phone is { Length: > 0 } phone
                ? $"{candidate.Name} — {phone}"
                : candidate.Name;
            var choice = new RadioButton
            {
                Content = label,
                GroupName = group,
                FontSize = 18,
                MinHeight = 44,
                Margin = new Avalonia.Thickness(16, 0, 0, 0),
            };
            AutomationProperties.SetName(choice, label);
            choice.IsCheckedChanged += (_, _) =>
            {
                if (choice.IsChecked != true)
                {
                    return;
                }

                _chosen[proposal.Position] = candidate;

                // Only now can the row be accepted, and it is ticked for the user: they have just
                // answered the question the row was asking.
                accept.IsEnabled = true;
                accept.IsChecked = true;
            };
            stack.Children.Add(choice);
        }

        stack.Children.Add(accept);
        return stack;
    }

    /// <summary>
    /// M13's favour, the other way round: the table holds a phone number for this brother and the
    /// address book does not, or holds a different one. Off by default and phrased as a question,
    /// because keeping the book fresh is a favour the user does, not something the app does behind
    /// their back.
    /// </summary>
    private CheckBox? PhoneWriteBackBox(OfficerProposal proposal)
    {
        if (proposal.Only is not { } candidate
            || string.IsNullOrWhiteSpace(proposal.CurrentPhone)
            || string.Equals(proposal.CurrentPhone, candidate.Phone, StringComparison.Ordinal)
            || !string.Equals(proposal.CurrentName, candidate.Name, StringComparison.Ordinal))
        {
            return null;
        }

        string text = string.IsNullOrWhiteSpace(candidate.Phone)
            ? $"Also add the phone number on the page to {candidate.Name} in your address book?"
            : $"Also keep the phone number on the page for {candidate.Name} in your address book?";
        var box = new CheckBox
        {
            Content = text,
            FontSize = 16,
            MinHeight = 44,
            IsChecked = false,
            Margin = new Avalonia.Thickness(40, 0, 0, 0),
        };
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked == true)
            {
                _phoneWriteBacks.Add(proposal.Position);
            }
            else
            {
                _phoneWriteBacks.Remove(proposal.Position);
            }
        };
        return box;
    }

    private void Accept(string position, bool accepted)
    {
        if (accepted)
        {
            _accepted.Add(position);
        }
        else
        {
            _accepted.Remove(position);
        }
    }

    private static TextBlock SectionHeading(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button MakeButton(string text)
    {
        var button = new Button { Content = text, FontSize = 20, MinHeight = 44, MinWidth = 200 };
        AutomationProperties.SetName(button, text);
        return (button).Action();
    }
}
