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

    /// <summary>
    /// The two colours the editor paints ON the sheet (M17): the empty-frame hint and the hover
    /// outline. They live in the palette like everything else, but they are measured against
    /// <c>Page.Sheet</c> rather than <c>Chrome.Background</c>, because that is what is behind them.
    /// </summary>
    internal const string AdornmentHint = "TrestleBoard.Adornment.Hint";

    internal const string AdornmentHover = "TrestleBoard.Adornment.Hover";
    internal const string Focus = "TrestleBoard.Focus";
    internal const string FocusInner = "TrestleBoard.Focus.Inner";

    /// <summary>Never the only signal that something is wrong: the text says so too.</summary>
    internal const string Warning = "TrestleBoard.Warning";

    internal const string BorderThickness = "TrestleBoard.Border.Thickness";
    internal const string FocusThickness = "TrestleBoard.Focus.Thickness";
    internal const string RuleTop = "TrestleBoard.Rule.Top";
    internal const string RuleBottom = "TrestleBoard.Rule.Bottom";

    /// <summary>The ControlTheme in <c>Theme/Controls.axaml</c> that <see cref="Primary{T}"/> applies.</summary>
    internal const string PrimaryButtonTheme = "TrestleBoard.PrimaryButton";

    /// <summary>
    /// The one visual treatment for "this is the offer you probably came for" (PLAN.md §11 M16):
    /// the accent fill, its measured foreground, and a gold left bar. <b>Three signals, so colour is
    /// never the only one</b> — the bar is a shape as well as a colour, and the caller sets a taller
    /// minimum height on top.
    ///
    /// <para>Used both by the action panel, where <c>EditorAction.IsPrimary</c> chooses, and by
    /// dialog footers, where <c>IsDefault</c> does. Before M16 those two ideas looked identical to
    /// every other button on the screen, which is most of what "every button is the same grey slab"
    /// meant.</para>
    /// </summary>
    /// <summary>The ControlTheme that <see cref="Action{T}"/> applies.</summary>
    internal const string ActionButtonTheme = "TrestleBoard.ActionButton";

    /// <summary>The ControlTheme that <see cref="Destructive{T}"/> applies (M40).</summary>
    internal const string DestructiveButtonTheme = "TrestleBoard.DestructiveButton";

    /// <summary>
    /// Marks a button as one the APP made, and gives it the affordance that says so (M37).
    ///
    /// <para>Enabled it has a border and a full-contrast label; disabled it has neither border nor
    /// fill. Before this, an enabled toolbar button and a disabled one shared the identical fill
    /// and differed only in label contrast — and the action panel's items, which are never disabled
    /// at all, looked like the disabled ones.</para>
    ///
    /// <para>Called rather than inferred, because the alternative is a bare <c>Button</c> selector
    /// that would also restyle the insides of every ScrollBar and ComboBox. What stops it being
    /// forgotten is not discipline but <c>ActionSurfaceTests</c>, which walks every window and
    /// fails on a button that has neither this nor <see cref="Primary{T}"/>.</para>
    /// </summary>
    internal static T Action<T>(this T button)
        where T : Avalonia.Controls.Button
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Classes.Add("action");
        return button;
    }

    /// <summary>
    /// M40: marks a button that takes something away. It keeps the "action" class, so it is still a
    /// button the app made and <c>ActionSurfaceTests</c> still counts it, and adds a heavier outline
    /// in the warning colour on top.
    ///
    /// <para><b>Colour is not the only signal</b>, per PLAN.md §6: the outline is thicker as well as
    /// redder, and every caller's label already ends in an ellipsis promising a question. Somebody
    /// who cannot tell the two colours apart still has a heavier box and a different sentence.</para>
    /// </summary>
    internal static T Destructive<T>(this T button)
        where T : Avalonia.Controls.Button
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Classes.Add("action");
        button.Classes.Add("destructive");
        return button;
    }

    internal static T Primary<T>(this T button)
        where T : Avalonia.Controls.Button
    {
        ArgumentNullException.ThrowIfNull(button);

        return button.Token(Avalonia.StyledElement.ThemeProperty, PrimaryButtonTheme);
    }

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
