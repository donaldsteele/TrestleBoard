namespace TrestleBoard.Screenshots;

/// <summary>What kind of picture this is, for the <c>--list</c> report and for docs/images/README.md.</summary>
internal enum ShotKind
{
    /// <summary>The whole editor window.</summary>
    Window,

    /// <summary>One dialog or wizard, on its own.</summary>
    Dialog,

    /// <summary>Drawn by the rendering engine, with no Avalonia involved at all.</summary>
    Render,
}

/// <summary>
/// One committed image, declared as data (PLAN.md §11, sizing notes): a name, the milestone it
/// needs, what it shows, the sentence it is captioned with, and the code that takes it.
///
/// <see cref="AltText"/> is not optional decoration. A README for an app built around
/// accessibility should not fail the standard the app is held to, so every image carries a real
/// sentence — and <c>DocsTests</c> checks the README actually uses it.
/// </summary>
internal sealed record Shot(
    string Name,
    ShotKind Kind,
    string? NeedsMilestone,
    string Caption,
    string AltText,
    Func<Stage, Task<byte[]>> Take);
