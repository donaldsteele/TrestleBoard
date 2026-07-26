using System.Text.Json;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Layout.Widgets;

/// <summary>
/// Non-personal document facts a freshly inserted widget may copy (docs/M7-spec.md §8.3). Never
/// carries a person's name, phone number or date.
/// </summary>
public sealed record WidgetSeed(string LodgeName, int IssueMonth, int IssueYear, string MeetingRule);

public readonly record struct WidgetInfo(
    string TypeId,
    string DisplayName,
    string IconKey,
    int CurrentDataVersion,
    SizePt DefaultSizePt);

/// <summary>
/// What the editor needs to know about widgets without referencing <c>TrestleBoard.Widgets</c>
/// (docs/M7-spec.md §0.1).
/// </summary>
public interface IWidgetCatalog
{
    bool TryGetInfo(string typeId, out WidgetInfo info);

    /// <summary>Fresh, EMPTY data for a new block. Throws only for an unknown type id.</summary>
    JsonElement CreateEmptyData(string typeId, WidgetSeed seed);
}
