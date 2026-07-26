using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TrestleBoard.Widgets;

/// <summary>
/// The one place widget payloads cross the untyped <see cref="JsonElement"/> boundary
/// (docs/M7-spec.md §1.2). Options mirror the container's <c>TboardJsonContext</c> exactly, so a
/// widget payload reads like the rest of document.json. Source-generated only — reflection-based
/// serialization is forbidden in this project.
/// </summary>
public static class WidgetData
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Null in, null out. Malformed JSON returns null rather than throwing (§1.1).</summary>
    public static T? Read<T>(JsonElement? data, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (data is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            return element.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Self-contained element, safe to store on a block and hand to a command.</summary>
    public static JsonElement Write<T>(T value, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return JsonSerializer.SerializeToElement(value, typeInfo);
    }
}
