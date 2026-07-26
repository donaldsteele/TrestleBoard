namespace TrestleBoard.Widgets.Wizards;

/// <summary>
/// The declarative wizard (PLAN.md §5, docs/M7-spec.md §6): ordered steps rendered by ONE generic
/// window. A widget ships data, not a dialog.
/// </summary>
public sealed class WizardDefinition
{
    /// <summary>Appends a review step when the caller did not supply one; a review step anywhere
    /// but last, or more than one, is a programming error.</summary>
    public WizardDefinition(string title, string introText, IReadOnlyList<IWizardStep> steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("A wizard needs at least one step.", nameof(steps));
        }

        int reviewCount = steps.Count(s => s.Kind == WizardStepKind.Review);
        if (reviewCount > 1)
        {
            throw new ArgumentException("A wizard has at most one review step.", nameof(steps));
        }

        if (reviewCount == 1 && steps[^1].Kind != WizardStepKind.Review)
        {
            throw new ArgumentException("The review step must be last.", nameof(steps));
        }

        Title = title;
        IntroText = introText ?? "";
        Steps = reviewCount == 1 ? steps : [.. steps, new ReviewStep()];
    }

    public string Title { get; }

    /// <summary>One sentence on the first screen.</summary>
    public string IntroText { get; }

    public IReadOnlyList<IWizardStep> Steps { get; }
}
