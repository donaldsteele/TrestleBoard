namespace TrestleBoard.App.Actions;

/// <summary>Which of the three things happened when a command was asked to run (PLAN.md §11 M73(f)).</summary>
public enum ActionResult
{
    /// <summary>
    /// The command did not run, and there is a sentence saying why: the catalog's refusal, the
    /// apology after a handler threw, or the bug report for an action nobody implemented. Every
    /// refusal carries a non-empty <see cref="ActionOutcome.Message"/> — M11's rule, and
    /// <see cref="TrestleBoard.Editing.Actions.ActionAvailability"/> enforces the same thing at
    /// the other end.
    /// </summary>
    Refused,

    /// <summary>
    /// The command ran and changed nothing: a file picker or a wizard was cancelled, or the thing
    /// asked for was already so. Not a failure and not a success — the distinction M73(f) exists
    /// for, because "Done" over a cancelled picker is the app claiming work it did not do.
    /// </summary>
    NothingHappened,

    /// <summary>The command ran and something about the newsletter, or what is on screen, changed.</summary>
    DidSomething,
}

/// <summary>
/// What <see cref="ActionRunner.RunAsync"/> hands back.
///
/// <para>Until M73(f) this was a <c>string?</c>, and its null meant "ran and nothing threw" — which
/// covered a silent early return, a cancelled picker, a cancelled wizard and an action id with no
/// handler at all. Two windows read that null as "it happened" and said so out loud
/// (<c>HelpWindow</c> and <c>ReviewWindow</c>), which are the two windows where a nervous user is
/// likeliest to press Cancel.</para>
/// </summary>
/// <param name="Result">Which of the three things happened.</param>
/// <param name="Message">
/// The sentence the status bar was given, for a refusal; null otherwise. Callers that want to show
/// it a second time — a window sitting over the status bar — should echo rather than re-announce
/// it, or a screen reader reads the same refusal twice.
/// </param>
public readonly record struct ActionOutcome(ActionResult Result, string? Message)
{
    /// <summary>The command ran and changed something.</summary>
    public static ActionOutcome Did { get; } = new(ActionResult.DidSomething, null);

    /// <summary>The command ran and changed nothing.</summary>
    public static ActionOutcome Nothing { get; } = new(ActionResult.NothingHappened, null);

    /// <summary>
    /// The command did not run, and this is why, in words the user can act on. Empty reasons are
    /// refused here for the same reason <c>ActionAvailability</c> refuses them at construction:
    /// nothing in the app may fail to happen without saying why.
    /// </summary>
    public static ActionOutcome Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ActionOutcome(ActionResult.Refused, reason);
    }

    /// <summary>True when the newsletter, or what is on screen, changed.</summary>
    public bool DidSomething => Result is ActionResult.DidSomething;

    /// <summary>True when the command was refused; <see cref="Message"/> is then the reason.</summary>
    public bool WasRefused => Result is ActionResult.Refused;
}
