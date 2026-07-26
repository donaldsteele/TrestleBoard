using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.DistrictCalendar;

/// <summary>
/// Source-generated codec with exactly the container's options (docs/M7-spec.md §1.2): camelCase,
/// string enums (so <see cref="DistrictCalendarMode"/> writes as "meetingDays"/"events"/"both", never
/// a number), null-skipping, indented. Reflection-based serialization is forbidden here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DistrictCalendarData))]
internal sealed partial class DistrictCalendarJsonContext : JsonSerializerContext;
