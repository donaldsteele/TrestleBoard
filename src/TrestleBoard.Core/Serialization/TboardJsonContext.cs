using System.Text.Json;
using System.Text.Json.Serialization;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Serialization;

/// <summary>document.json shape: the document tree minus styling (PLAN.md §2 splits styles out).</summary>
public sealed class DocumentBodyFile
{
    public DocumentMetadata Metadata { get; set; } = new();

    public List<PageMaster> PageMasters { get; set; } = [];

    public List<Page> Pages { get; set; } = [];

    public List<Story> Stories { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>styles.json shape: named styles + theme tokens, separate so templates can share.</summary>
public sealed class StylesFile
{
    public Theme Theme { get; set; } = new();

    public StyleSheet StyleSheet { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>
/// Source-generated serialization (PLAN.md §1): camelCase, UTF-8, string enums, indented for
/// diffability. AllowOutOfOrderMetadataProperties keeps polymorphic blocks robust when third
/// parties reorder properties. Unknown properties survive via [JsonExtensionData] overflow bags.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DocumentBodyFile))]
[JsonSerializable(typeof(StylesFile))]
[JsonSerializable(typeof(TboardManifest))]
public sealed partial class TboardJsonContext : JsonSerializerContext;
