using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Model;

/// <summary>Rectangle wrap is the entire float-with-wrap feature at the model level (PLAN.md §2).</summary>
public enum WrapMode
{
    None,
    Rectangle,
}

public enum VerticalAlignment
{
    Top,
    Middle,
    Bottom,
}

public enum ImageFit
{
    Cover,
    Contain,
    Stretch,
}

public enum ShapeKind
{
    Rule,
    Box,
    Decoration,
}

/// <summary>
/// Page-absolute rectangular element. Text frames below in z wrap around blocks whose
/// <see cref="WrapMode"/> is Rectangle, inflated by <see cref="WrapMarginPt"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ImageFrame), "image")]
[JsonDerivedType(typeof(WidgetBlock), "widget")]
[JsonDerivedType(typeof(ShapeBlock), "shape")]
[JsonDerivedType(typeof(VectorBlock), "vector")]
public abstract class Block
{
    public required string Id { get; set; }

    public RectPt FrameRect { get; set; }

    public int ZOrder { get; set; }

    public WrapMode WrapMode { get; set; } = WrapMode.None;

    public float WrapMarginPt { get; set; }

    public string? FrameStyleRef { get; set; }

    /// <summary>
    /// Whether this block stays where it is (M81).
    ///
    /// <para><b>Position and size only.</b> A locked block is still chosen, still typed into, still
    /// captioned, still deleted — what it refuses is being dragged and being resized, which are the
    /// two things a tremor does by accident on a page somebody has finished laying out.</para>
    ///
    /// <para><b>Nothing ships locked.</b> A template that arrived with something pinned would
    /// produce a "why will it not move" telephone call, which is worse than the stray drag this
    /// exists to prevent — so it is false everywhere until a person asks for it.</para>
    /// </summary>
    public bool Locked { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class TextBlock : Block
{
    public required string StoryRef { get; set; }

    public int ColumnCount { get; set; } = 1;

    public VerticalAlignment VerticalAlign { get; set; } = VerticalAlignment.Top;

    /// <summary>Next frame in the story chain; null terminates (overflow shows overset indicator).</summary>
    public string? LinkNext { get; set; }
}

/// <summary>
/// Non-destructive edit recipe applied at render time (PLAN.md §2): originals stay untouched
/// in assets/, so any image can be re-cropped months later with zero quality loss.
/// </summary>
public sealed class ImageRecipe
{
    /// <summary>Crop in normalized [0,1] source-image coordinates; null = full image.</summary>
    public RectPt? CropNormalized { get; set; }

    /// <summary>Clockwise 90° steps, 0–3.</summary>
    public int RotationSteps { get; set; }

    public float Brightness { get; set; }

    public float Contrast { get; set; }

    public float Saturation { get; set; }

    public bool AutoLevels { get; set; }

    /// <summary>
    /// Per-channel auto-levels instead of the luminance-only default. Kept as a separate flag
    /// rather than turning <see cref="AutoLevels"/> into an enum so documents written before M6
    /// still deserialize (docs/M6-spec.md §3).
    /// </summary>
    public bool AutoLevelsPerChannel { get; set; }

    /// <summary>
    /// M43: the frame shape the user has already been shown M23's "this may look stretched" note
    /// for, or null if they have never dismissed it.
    ///
    /// <para>It lives in the DOCUMENT because the note is about this picture in this frame, and the
    /// user's "I know" was about that too. It was a dictionary in the controller, so it lasted until
    /// the app closed and the note came back on the next open, at which point the app was arguing
    /// with somebody who had already answered (review §14.3).</para>
    ///
    /// <para>Still keyed by aspect, which keeps M23's rule intact: reshape the frame and the note
    /// returns, because that is a NEW mismatch rather than the one that was dismissed. Additive and
    /// nullable, so documents written before M43 deserialize unchanged.</para>
    /// </summary>
    public float? StretchNoticeDismissedAtAspect { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    public ImageRecipe Clone() => new()
    {
        StretchNoticeDismissedAtAspect = StretchNoticeDismissedAtAspect,
        CropNormalized = CropNormalized,
        RotationSteps = RotationSteps,
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        AutoLevels = AutoLevels,
        AutoLevelsPerChannel = AutoLevelsPerChannel,
        ExtraProperties = ExtraProperties is null ? null : new Dictionary<string, JsonElement>(ExtraProperties),
    };
}

/// <summary>
/// A block whose words live on the block itself rather than in a story: the sentence a screen
/// reader is told, and the caption printed underneath (M18).
///
/// <para>Introduced in M72, when the emblem stopped being an <see cref="ImageFrame"/>. The two
/// commands that write those words — <c>SetPictureWordsCommand</c> and the controller behind it —
/// were written against <see cref="ImageFrame"/>, and a drawing that could not be captioned or
/// described would have been a picture with the accessibility taken out of it.</para>
/// </summary>
public interface ICaptionedBlock
{
    /// <summary>Screen-reader description (PLAN.md §6 — accessibility is first-class).</summary>
    string AltText { get; set; }

    /// <summary>The words printed under the frame, or null for none.</summary>
    string? Caption { get; set; }
}

public sealed class ImageFrame : Block, ICaptionedBlock
{
    /// <summary>Asset entry name inside the container, e.g. "img-01hzy...jpg".</summary>
    public required string AssetRef { get; set; }

    public ImageRecipe Recipe { get; set; } = new();

    public ImageFit Fit { get; set; } = ImageFit.Cover;

    public string? Caption { get; set; }

    /// <summary>Screen-reader description (PLAN.md §6 — accessibility is first-class).</summary>
    public string AltText { get; set; } = "";

    /// <summary>
    /// M67: the container entry holding the PDF this picture was rendered from, when it was.
    ///
    /// <para>The picture is an ordinary picture and the app treats it as one — this is a
    /// <b>provenance note</b>, not a second source of truth. It exists so a later version can
    /// re-render the page at a higher resolution without the committee having to find the file
    /// again, which is why the original PDF bytes are kept in the container beside it (gate 7).
    /// Nothing reads it yet, and nothing about the picture depends on it.</para>
    ///
    /// <para>Null on every picture that did not come from a PDF, and never written when null — a
    /// newsletter saved by an older TrestleBoard opens and saves back byte-unchanged (M61's rule).
    /// This is not a new frame type: the acceptance asks that the model change be "an image like
    /// any other", and an optional note on the frame that already exists is exactly that.</para>
    /// </summary>
    public string? SourcePdfAssetRef { get; set; }

    /// <summary>M67: which page of <see cref="SourcePdfAssetRef"/> this picture is, 1-based.</summary>
    public int? SourcePdfPage { get; set; }
}

public sealed class WidgetBlock : Block
{
    public required string WidgetType { get; set; }

    /// <summary>Widget data schema version for M7+ evolution.</summary>
    public int DataVersion { get; set; } = 1;

    /// <summary>Widget-specific payload; opaque to Core (typed by the widget registry in M7).</summary>
    public JsonElement? Data { get; set; }

    public string? TableStyleRef { get; set; }
}

/// <summary>
/// One stroked or filled path of a <see cref="VectorBlock"/>, in the block's own viewbox
/// coordinates (PLAN.md §11 M72).
/// </summary>
public sealed class VectorPart
{
    /// <summary>
    /// SVG path data — a string, deliberately. <see cref="ShapeBlock"/> already stores its colour
    /// and pen width with no graphics type anywhere near it, and the same holds here: the parse
    /// happens once, in <c>TrestleBoard.Rendering</c>, where SkiaSharp already lives. Nothing about
    /// carrying a drawing in the document forces a graphics library into Core, and Core stays
    /// BCL-only (PLAN.md §9).
    /// </summary>
    public required string PathData { get; set; }

    /// <summary>
    /// Zero to fill the path; otherwise the pen width in viewbox units, with round caps and joins.
    /// It scales with the drawing, so the same block is the same picture at any size.
    /// </summary>
    public double StrokeWidth { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>
/// A drawing that stays a drawing: path data in the document, resolved to pixels only by whatever
/// surface is being painted (PLAN.md §11 M72).
///
/// <para><b>Why this frame type exists, against a written decision that it should not.</b>
/// docs/M65-spec.md §3 rasterised the emblem at insert time and handed the PNG to the ordinary
/// picture path, on the reasoning that the app's SkiaSharp pipeline was byte-identical everywhere.
/// It is not: antialiased coverage is floating-point arithmetic and arm64 contracts multiply-adds
/// where x64 cannot, so the committed emblem PNG hash failed on macOS CI for fifteen consecutive
/// builds. The consequence was not a red build but a real one — a macOS member's newsletter carried
/// different bytes from a Windows member's, because the raster was in the container. Storing the
/// geometry removes the raster, and cross-architecture identity becomes true by construction.</para>
///
/// <para>It also stops being a photograph that never wanted the photo toolkit: crop, rotation,
/// brightness and auto-levels do not apply to a two-colour line drawing, and the PDF now receives
/// path operators instead of a ~680dpi raster.</para>
/// </summary>
public sealed class VectorBlock : Block, ICaptionedBlock
{
    /// <summary>The drawing's own coordinate space. Parts are scaled to fit the frame, and centred.</summary>
    public double ViewBoxWidth { get; set; } = 1;

    /// <summary>The drawing's own coordinate space.</summary>
    public double ViewBoxHeight { get; set; } = 1;

    /// <summary>Painted in order, back to front.</summary>
    public List<VectorPart> Parts { get; set; } = [];

    /// <summary>
    /// Black on transparent, as M65 §9 decided and did not revisit — a trestle board is printed,
    /// usually in black and white, and an emblem over a tinted panel must not arrive in a white box.
    /// </summary>
    public const uint DefaultInkArgb = 0xFF000000u;

    /// <summary>The one colour everything in the drawing is painted in.</summary>
    public uint InkArgb { get; set; } = DefaultInkArgb;

    /// <summary>Screen-reader description (PLAN.md §6). Emblems arrive already described.</summary>
    public string AltText { get; set; } = "";

    /// <summary>The words printed under the drawing, or null for none.</summary>
    public string? Caption { get; set; }

    /// <summary>
    /// M72: which shelf emblem this drawing came from, when it came from one.
    ///
    /// <para><b>Provenance only</b>, on the <see cref="ImageFrame.SourcePdfAssetRef"/> precedent:
    /// nothing reads it, and nothing about how the block draws depends on it. It is here so a later
    /// version can tell a committee what they put on the page — and it is deliberately NOT how the
    /// drawing is resolved. Storing only the id and looking the geometry up at render time would
    /// need <c>Rendering → Emblems</c>, which §9 forbids, and would break the rule that a document
    /// is self-describing: a revision to the shelf would silently change how an existing newsletter
    /// prints.</para>
    ///
    /// <para>Null on any drawing that did not come from the shelf, and never written when null.</para>
    /// </summary>
    public string? EmblemId { get; set; }

    /// <summary>
    /// M72: <c>EmblemFingerprint.Of</c> for <see cref="EmblemId"/> at the moment it was inserted —
    /// the SHA-256 of the geometry, which is what gate 22 hashes. Provenance only, like the id: it
    /// records which drawing of the emblem this is, so a later redraw of the shelf is a fact anybody
    /// can check rather than a silent difference.
    /// </summary>
    public string? EmblemFingerprint { get; set; }

    /// <summary>Width over height of the viewbox — the shape the frame is kept at.</summary>
    [JsonIgnore]
    public double AspectRatio => ViewBoxHeight > 0 ? ViewBoxWidth / ViewBoxHeight : 1;
}

public sealed class ShapeBlock : Block
{
    public ShapeKind Kind { get; set; } = ShapeKind.Rule;

    public uint? StrokeArgb { get; set; }

    public float StrokeWidthPt { get; set; }

    public uint? FillArgb { get; set; }
}
