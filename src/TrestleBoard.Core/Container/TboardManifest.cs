using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Container;

/// <summary>manifest.json (PLAN.md §2): format identity + version gates for migrations.</summary>
public sealed class TboardManifest
{
    public const string CurrentFormatName = "trestleboard";
    public const string CurrentFormatVersion = "1.0.0";
    public const string CurrentMinReaderVersion = "1.0.0";

    public string FormatName { get; set; } = CurrentFormatName;

    /// <summary>Semver of the document format this file was written in.</summary>
    public string FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Version of the app that wrote the file (informational).</summary>
    public string GeneratorVersion { get; set; } = "0.0.0";

    /// <summary>Oldest format version a reader must understand to open this file.</summary>
    public string MinReaderVersion { get; set; } = CurrentMinReaderVersion;

    public bool IsTemplate { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
