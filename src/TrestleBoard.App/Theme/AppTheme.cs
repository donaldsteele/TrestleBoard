using Avalonia.Styling;

namespace TrestleBoard.App.Theme;

/// <summary>
/// The theme variants the chrome is painted in (PLAN.md §6, §11 M16).
///
/// <para>Light and Dark are Avalonia's own. High Contrast is ours, declared as a custom
/// <see cref="ThemeVariant"/> that <b>inherits Dark</b>: every FluentTheme control resource we do
/// not override — and there are hundreds — resolves through the inheritance chain to its Dark
/// value, so a High Contrast window is a dark window with our seven tokens swapped on top of it,
/// not a window missing three hundred brushes.</para>
///
/// <para>Until M16 High Contrast was a runtime <c>StyleInclude</c> overlay bolted onto the Dark
/// variant. That mechanism could express exactly one non-default look, so Light and Dark needed a
/// second one, and it reached only the controls whose bare element selectors it happened to list —
/// which is why the toolbar, the status bar and the area around the page stayed mid-grey with the
/// theme on. A ThemeDictionary reaches everything that asks for a token, because it answers the
/// lookup rather than restyling the asker.</para>
/// </summary>
public static class AppTheme
{
    /// <summary>
    /// The 7:1 variant. <c>inheritVariant: Dark</c> is load-bearing and
    /// <c>ThemeCompositionTests</c> is the thing that proves it still works after an Avalonia bump.
    /// </summary>
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);
}
