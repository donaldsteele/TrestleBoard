namespace TrestleBoard.Editing.Actions;

/// <summary>The three answers to "can I do this right now?" (PLAN.md §6, §11 M11).</summary>
public enum ActionAvailabilityKind
{
    /// <summary>Do it.</summary>
    Available,

    /// <summary>Not about the selected thing at all — the panel leaves it out entirely.</summary>
    NotApplicable,

    /// <summary>About this thing, but not right now, and there is a reason the user can act on.</summary>
    Blocked,
}

/// <summary>
/// Whether an action can run, and — this is the part that matters — why not, in words the user can
/// act on. An action the user can see must never fail silently (PLAN.md §6).
///
/// <see cref="Blocked"/> refuses an empty reason at construction. That is deliberate: the owner's
/// complaint was thirty controls greying themselves out with no explanation, and a rule enforced by
/// a constructor cannot be forgotten in a later edit the way a code review can.
/// </summary>
public sealed record ActionAvailability
{
    private ActionAvailability(ActionAvailabilityKind kind, string reason, string? remedyId)
    {
        Kind = kind;
        Reason = reason;
        RemedyId = remedyId;
    }

    /// <summary>The action can run now.</summary>
    public static ActionAvailability Available { get; } =
        new(ActionAvailabilityKind.Available, string.Empty, null);

    /// <summary>
    /// The action has nothing to do with the current selection. The panel omits it entirely — the
    /// panel is headed "A photo is selected", so absence reads as "not about photos". The menu bar
    /// keeps it, greyed, and says this out loud, because a menu is a global index and hiding items
    /// there would break the every-command-has-a-menu-item guarantee (PLAN.md §6).
    ///
    /// A reason is required here too, for exactly that second surface.
    /// </summary>
    public static ActionAvailability NotApplicable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "An unavailable action must say why in plain language (PLAN.md §6).", nameof(reason));
        }

        return new ActionAvailability(ActionAvailabilityKind.NotApplicable, reason, null);
    }

    public ActionAvailabilityKind Kind { get; }

    /// <summary>Plain language, addressed to the user, ending in a full stop. Empty unless blocked.</summary>
    public string Reason { get; }

    /// <summary>The action that would unblock this one, if there is a single obvious one.</summary>
    public string? RemedyId { get; }

    public bool IsAvailable => Kind == ActionAvailabilityKind.Available;

    /// <summary>Blocked, with the reason the user is owed.</summary>
    /// <param name="reason">Plain language: what is missing and what to do about it.</param>
    /// <param name="remedyId">Optionally, the action that would fix it.</param>
    public static ActionAvailability Blocked(string reason, string? remedyId = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A blocked action must say why in plain language (PLAN.md §6).", nameof(reason));
        }

        return new ActionAvailability(ActionAvailabilityKind.Blocked, reason, remedyId);
    }
}

/// <summary>An action together with what the user can do about it right now.</summary>
public sealed record ActionOffer(EditorAction Action, ActionAvailability Availability)
{
    public bool IsAvailable => Availability.IsAvailable;
}
