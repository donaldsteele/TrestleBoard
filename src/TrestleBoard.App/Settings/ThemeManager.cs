using Avalonia;
using Avalonia.Styling;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Settings;

/// <summary>
/// Applies the chrome preferences (PLAN.md §6, docs/M9-spec.md §4). Two rules worth stating:
/// <list type="bullet">
/// <item><see cref="ThemeChoice.System"/> follows the OS hint, because someone who has already set
/// their machine to dark or high contrast has told us what they need.</item>
/// <item><b>The page never themes.</b> It is a piece of paper: white in dark mode too, because the
/// user is laying out something that will be printed.</item>
/// </list>
///
/// <para><b>M16 removed the moving part.</b> Until then this class also added and removed a
/// <c>StyleInclude</c> overlay for High Contrast — a second mechanism that reached only the controls
/// its bare element selectors happened to name, which is why the toolbar, the status bar and the
/// area around the page stayed mid-grey with the theme on. All three variants now live in
/// <c>Theme/Palette.axaml</c> as theme dictionaries, so this method sets one property and nothing
/// else: whichever variant is asked for, every token answers in it.</para>
/// </summary>
public static class ThemeManager
{
    public static void Apply(Application application, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settings);

        application.RequestedThemeVariant = VariantFor(settings.Theme);
    }

    /// <summary>The variant a choice asks for. Public so the theme tests can name it.</summary>
    internal static ThemeVariant VariantFor(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => ThemeVariant.Light,
        ThemeChoice.Dark => ThemeVariant.Dark,
        ThemeChoice.HighContrast => AppTheme.HighContrast,
        _ => ThemeVariant.Default,
    };

    /// <summary>
    /// Whether the OS is asking for a dark look. Used only to describe what "Follow my computer"
    /// will do, so the settings dialog can say it rather than leaving the user to guess.
    /// </summary>
    public static bool SystemPrefersDark(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.ActualThemeVariant == ThemeVariant.Dark;
    }
}
