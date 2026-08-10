using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Core.Model;
using TrestleBoard.Emblems;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Emblems;

/// <summary>
/// The one place a shelf emblem turns into geometry anything else can use (PLAN.md §11 M72).
///
/// <para>It lives in <c>App</c> because <c>App</c> is the only project that may reference
/// <c>TrestleBoard.Emblems</c> — the same rule that keeps <c>Editing</c> away from <c>Roster</c>.
/// <c>Rendering</c> must never learn that emblems exist (§9), and <c>Core</c> must never learn that
/// SkiaSharp does, so the two sides meet here: an <see cref="Emblem"/> in, and either a document
/// block's parts or a renderer's path specs out.</para>
///
/// <para>Both conversions are the same three fields. They are written twice rather than one calling
/// the other because the two destinations belong to different layers and neither may reference the
/// other's types.</para>
/// </summary>
internal static class EmblemGeometry
{
    /// <summary>The emblem's parts as document geometry, for <c>PhotoController.InsertVector</c>.</summary>
    internal static IReadOnlyList<VectorPart> PartsOf(Emblem emblem) =>
        [.. emblem.Parts.Select(part => new VectorPart
        {
            PathData = part.PathData,
            StrokeWidth = part.StrokeWidth,
        })];

    /// <summary>The emblem's parts as draw instructions, for the picker's thumbnails.</summary>
    internal static IReadOnlyList<VectorPathSpec> SpecsOf(Emblem emblem) =>
        [.. emblem.Parts.Select(part => new VectorPathSpec(part.PathData, part.StrokeWidth))];
}
