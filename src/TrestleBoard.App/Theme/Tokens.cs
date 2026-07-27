using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace TrestleBoard.App.Theme;

/// <summary>
/// The palette's keys, and the one way code-behind is allowed to use them (PLAN.md §6, §11 M16).
///
/// <para>Most of this application's chrome is built in C#, not XAML, so "colour is a token, never a
/// literal" needs somewhere to live that a <c>{DynamicResource}</c> string cannot: a mistyped key in
/// markup fails silently and leaves the control unpainted, and <c>Foreground = Brushes.Firebrick</c>
/// does not fail at all — it just survives the theme swap and lands unreadable red on black. These
/// constants make the first mistake a compile error and <see cref="Token"/> makes the second one
/// obvious in review.</para>
/// </summary>
internal static class Tokens
{
    internal const string ChromeBackground = "TrestleBoard.Chrome.Background";
    internal const string ChromeForeground = "TrestleBoard.Chrome.Foreground";

    /// <summary>Secondary text. Full white in High Contrast — see the palette's header.</summary>
    internal const string ChromeMuted = "TrestleBoard.Chrome.Muted";

    /// <summary>A boundary that has to be perceived (3:1). Not the same thing as a hairline.</summary>
    internal const string ChromeBorder = "TrestleBoard.Chrome.Border";

    /// <summary>A decorative hairline, with no contrast floor. Never a real boundary.</summary>
    internal const string ChromeDivider = "TrestleBoard.Chrome.Divider";

    internal const string Accent = "TrestleBoard.Accent";
    internal const string AccentForeground = "TrestleBoard.Accent.Foreground";

    /// <summary>The lodge gold. The key name states its only legal background.</summary>
    internal const string OrnamentOnAccent = "TrestleBoard.Ornament.OnAccent";

    internal const string PageBackdrop = "TrestleBoard.Page.Backdrop";
    internal const string Focus = "TrestleBoard.Focus";
    internal const string FocusInner = "TrestleBoard.Focus.Inner";

    /// <summary>Never the only signal that something is wrong: the text says so too.</summary>
    internal const string Warning = "TrestleBoard.Warning";

    internal const string BorderThickness = "TrestleBoard.Border.Thickness";
    internal const string FocusThickness = "TrestleBoard.Focus.Thickness";
    internal const string RuleTop = "TrestleBoard.Rule.Top";
    internal const string RuleBottom = "TrestleBoard.Rule.Bottom";

    /// <summary>
    /// Binds a property to a palette key by reference, so it re-resolves when the variant changes.
    /// Assigning the brush by value would work exactly once, at the moment the control was built.
    /// </summary>
    internal static T Token<T>(this T control, AvaloniaProperty property, string key)
        where T : AvaloniaObject
    {
        ArgumentNullException.ThrowIfNull(control);
        control[!property] = new DynamicResourceExtension(key);
        return control;
    }
}
