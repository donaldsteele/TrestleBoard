using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.SimpleList;

/// <summary>
/// One row of a list of the user's own: up to three cells, in column order (PLAN.md §11 M60).
/// </summary>
public sealed class SimpleListRow
{
    public string First { get; set; } = "";

    public string? Second { get; set; }

    public string? Third { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>The cells that exist, in order — one, two or three of them.</summary>
    public IReadOnlyList<string> Cells(int columns) => columns switch
    {
        <= 1 => [First],
        2 => [First, Second ?? ""],
        _ => [First, Second ?? "", Third ?? ""],
    };

    /// <summary>True when nothing has been typed in this row at all.</summary>
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(First)
        && string.IsNullOrWhiteSpace(Second)
        && string.IsNullOrWhiteSpace(Third);
}

/// <summary>
/// A list of the user's own (PLAN.md §11 M60) — Eastern Star news, a degree schedule, a dinner
/// menu: content that is table-shaped but is not one of the six lists TrestleBoard fills in.
///
/// <para><b>Three columns is a hard cap, and it is a cap on the question rather than on the
/// answer.</b> There are three heading boxes and no way to ask for a fourth. The wizard's promise
/// is one question at a time, and that promise erodes the moment a row is a grid; three cells is
/// the most that still reads as three questions.</para>
/// </summary>
public sealed class SimpleListData
{
    public string Heading { get; set; } = "";

    /// <summary>The first column's heading. A list always has at least one column.</summary>
    public string FirstColumn { get; set; } = "";

    /// <summary>The second column's heading, or null when the list has one column.</summary>
    public string? SecondColumn { get; set; }

    /// <summary>The third column's heading, or null.</summary>
    public string? ThirdColumn { get; set; }

    public List<SimpleListRow> Rows { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>
    /// How many columns this list has, derived from how many headings were filled in rather than
    /// stored. One less thing that can disagree with itself: a stored count and a set of headings
    /// would eventually say different numbers, and the printed table would follow the wrong one.
    /// </summary>
    [JsonIgnore]
    public int ColumnCount =>
        !string.IsNullOrWhiteSpace(ThirdColumn) ? 3
        : !string.IsNullOrWhiteSpace(SecondColumn) ? 2
        : 1;

    /// <summary>The column headings that exist, in order.</summary>
    public IReadOnlyList<string> ColumnHeadings() => ColumnCount switch
    {
        1 => [FirstColumn],
        2 => [FirstColumn, SecondColumn ?? ""],
        _ => [FirstColumn, SecondColumn ?? "", ThirdColumn ?? ""],
    };

    /// <summary>True when any column has been given a name worth printing as a header row.</summary>
    [JsonIgnore]
    public bool HasColumnHeadings => ColumnHeadings().Any(h => !string.IsNullOrWhiteSpace(h));

    /// <summary>The rows with anything in them. A blank row is a stray Enter, not an entry.</summary>
    public IReadOnlyList<SimpleListRow> FilledRows() => [.. Rows.Where(r => !r.IsBlank)];
}
