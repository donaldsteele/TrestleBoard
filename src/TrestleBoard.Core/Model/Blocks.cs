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
public abstract class Block
{
    public required string Id { get; set; }

    public RectPt FrameRect { get; set; }

    public int ZOrder { get; set; }

    public WrapMode WrapMode { get; set; } = WrapMode.None;

    public float WrapMarginPt { get; set; }

    public string? FrameStyleRef { get; set; }

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

public sealed class ImageFrame : Block
{
    /// <summary>Asset entry name inside the container, e.g. "img-01hzy...jpg".</summary>
    public required string AssetRef { get; set; }

    public ImageRecipe Recipe { get; set; } = new();

    public ImageFit Fit { get; set; } = ImageFit.Cover;

    public string? Caption { get; set; }

    /// <summary>Screen-reader description (PLAN.md §6 — accessibility is first-class).</summary>
    public string AltText { get; set; } = "";
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

public sealed class ShapeBlock : Block
{
    public ShapeKind Kind { get; set; } = ShapeKind.Rule;

    public uint? StrokeArgb { get; set; }

    public float StrokeWidthPt { get; set; }

    public uint? FillArgb { get; set; }
}
