using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.CommitteeList;

/// <summary>
/// Source-generated codec with exactly the container's options (docs/M7-spec.md §1.2): camelCase,
/// string enums, null-skipping, indented. Reflection-based serialization is forbidden here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CommitteeListData))]
internal sealed partial class CommitteeListJsonContext : JsonSerializerContext;
