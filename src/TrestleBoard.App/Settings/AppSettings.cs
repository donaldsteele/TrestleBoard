using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.App.Settings;

/// <summary>
/// Which look the app wears (PLAN.md §6). <see cref="System"/> follows the OS hint, which is what a
/// first run does — someone who has already set their machine to a dark or high-contrast theme has
/// told us what they need.
/// </summary>
public enum ThemeChoice
{
    System,
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// The user's chrome preferences (PLAN.md §6). Deliberately tiny and deliberately persisted: an
/// elderly user who has found a scale that works must not have to find it again next week.
/// </summary>
public sealed record AppSettings
{
    /// <summary>PLAN.md §6 pins the range at 100–200%.</summary>
    public const int MinScalePercent = 100;

    public const int MaxScalePercent = 200;

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    public int UiScalePercent { get; init; } = MinScalePercent;

    /// <summary>
    /// Whether the panel of things-you-can-do is docked open (M11). On by default: it is the
    /// primary way actions are discovered now, and someone who has never seen it cannot decide
    /// they want it.
    /// </summary>
    public bool ShowActionPanel { get; init; } = true;

    [JsonIgnore]
    public double UiScale => Math.Clamp(UiScalePercent, MinScalePercent, MaxScalePercent) / 100d;

    /// <summary>Clamped rather than rejected: a settings file edited by hand should not stop the app.</summary>
    public AppSettings Normalised() => this with
    {
        UiScalePercent = Math.Clamp(UiScalePercent, MinScalePercent, MaxScalePercent),
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeChoice.System,
    };

    /// <summary>
    /// From M12 this comes through <see cref="AppPaths"/> rather than building the AppData path
    /// itself, so a harness that redirects the root redirects this too.
    /// </summary>
    public static string DefaultPath() => AppPaths.SettingsFile;

    /// <summary>
    /// Never throws. A settings file that is missing, unreadable or garbage yields the defaults —
    /// losing a preference is a nuisance, refusing to start is not.
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return (JsonSerializer.Deserialize(File.ReadAllBytes(path), SettingsJsonContext.Default.AppSettings)
                ?? new AppSettings()).Normalised();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Best-effort. A preference that cannot be written is not worth an error dialog.</summary>
    public void Save(string? path = null)
    {
        path = path ?? DefaultPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(
                Normalised(), SettingsJsonContext.Default.AppSettings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
