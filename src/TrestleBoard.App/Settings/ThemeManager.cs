using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace TrestleBoard.App.Settings;

/// <summary>
/// Applies the chrome preferences (PLAN.md §6, docs/M9-spec.md §4). Two rules worth stating:
/// <list type="bullet">
/// <item><see cref="ThemeChoice.System"/> follows the OS hint, because someone who has already set
/// their machine to dark or high contrast has told us what they need.</item>
/// <item><b>The page never themes.</b> It is a piece of paper: white in dark mode too, because the
/// user is laying out something that will be printed.</item>
/// </list>
/// </summary>
public static class ThemeManager
{
    private const string HighContrastUri =
        "avares://TrestleBoard.App/Settings/HighContrastTheme.axaml";

    private static StyleInclude? _highContrast;

    public static void Apply(Application application, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settings);

        application.RequestedThemeVariant = settings.Theme switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,

            // High contrast is a dark base with the overlay below on top of it.
            ThemeChoice.HighContrast => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        SetHighContrast(application, settings.Theme == ThemeChoice.HighContrast);
    }

    /// <summary>
    /// Whether the OS is asking for a dark look. Used only to describe what "Follow my computer"
    /// will do, so the settings dialog can say it rather than leaving the user to guess.
    /// </summary>
    public static bool SystemPrefersDark(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.ActualThemeVariant == ThemeVariant.Dark;
    }

    private static void SetHighContrast(Application application, bool on)
    {
        if (on)
        {
            _highContrast ??= new StyleInclude(new Uri("avares://TrestleBoard.App/"))
            {
                Source = new Uri(HighContrastUri),
            };

            if (!application.Styles.Contains(_highContrast))
            {
                application.Styles.Add(_highContrast);
            }

            return;
        }

        if (_highContrast is not null)
        {
            application.Styles.Remove(_highContrast);
        }
    }
}
