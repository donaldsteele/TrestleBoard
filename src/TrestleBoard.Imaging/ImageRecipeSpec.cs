using System.Globalization;
using SkiaSharp;

namespace TrestleBoard.Imaging;

/// <summary>Rectangle in normalized [0,1] source-image coordinates.</summary>
public readonly record struct NormalizedRect(float X, float Y, float Width, float Height)
{
    public static readonly NormalizedRect Full = new(0f, 0f, 1f, 1f);

    public float Right => X + Width;

    public float Bottom => Y + Height;

    /// <summary>Clamps into the unit square, keeping at least a 1% sliver.</summary>
    public NormalizedRect Clamped()
    {
        float w = Math.Clamp(Width, 0.01f, 1f);
        float h = Math.Clamp(Height, 0.01f, 1f);
        float x = Math.Clamp(X, 0f, 1f - w);
        float y = Math.Clamp(Y, 0f, 1f - h);
        return new NormalizedRect(x, y, w, h);
    }

    public SKRectI ToPixels(int imageWidth, int imageHeight)
    {
        NormalizedRect r = Clamped();
        int left = (int)MathF.Round(r.X * imageWidth);
        int top = (int)MathF.Round(r.Y * imageHeight);
        int right = (int)MathF.Round(r.Right * imageWidth);
        int bottom = (int)MathF.Round(r.Bottom * imageHeight);
        return new SKRectI(
            Math.Clamp(left, 0, imageWidth - 1),
            Math.Clamp(top, 0, imageHeight - 1),
            Math.Clamp(Math.Max(right, left + 1), 1, imageWidth),
            Math.Clamp(Math.Max(bottom, top + 1), 1, imageHeight));
    }
}

/// <summary>How auto-levels stretches (docs/M6-spec.md §3).</summary>
public enum AutoLevelsMode
{
    /// <summary>One stretch from the luma histogram, applied to all channels — never shifts tint.</summary>
    Luminance,

    /// <summary>Independent per-channel stretch: stronger, but can neutralise a warm room.</summary>
    PerChannel,
}

/// <summary>
/// The recipe as Imaging sees it — deliberately NOT <c>Core.Model.ImageRecipe</c>, so this project
/// keeps its SkiaSharp-only dependency (docs/M6-spec.md §0). Rendering maps between the two.
/// </summary>
public sealed record ImageRecipeSpec(
    NormalizedRect? Crop = null,
    int RotationSteps = 0,
    float Brightness = 0f,
    float Contrast = 0f,
    float Saturation = 0f,
    bool AutoLevels = false,
    AutoLevelsMode LevelsMode = AutoLevelsMode.Luminance)
{
    public static readonly ImageRecipeSpec Identity = new();

    /// <summary>True when the recipe changes nothing — the pipeline can then skip straight to resampling.</summary>
    public bool IsIdentity =>
        Crop is null
        && NormalizedRotation == 0
        && Brightness == 0f
        && Contrast == 0f
        && Saturation == 0f
        && !AutoLevels;

    public int NormalizedRotation => ((RotationSteps % 4) + 4) % 4;

    /// <summary>
    /// Stable hash for the render cache. Deliberately not <c>GetHashCode</c>: string/record hashes
    /// are randomised per process, and a cache that changes key between runs is not a cache.
    /// </summary>
    public ulong StableHash()
    {
        ulong hash = 14695981039346656037UL;
        NormalizedRect crop = Crop?.Clamped() ?? NormalizedRect.Full;
        MixFloat(ref hash, crop.X);
        MixFloat(ref hash, crop.Y);
        MixFloat(ref hash, crop.Width);
        MixFloat(ref hash, crop.Height);
        MixInt(ref hash, NormalizedRotation);
        MixFloat(ref hash, Brightness);
        MixFloat(ref hash, Contrast);
        MixFloat(ref hash, Saturation);
        MixInt(ref hash, AutoLevels ? 1 : 0);
        MixInt(ref hash, (int)LevelsMode);
        return hash;

        static void MixFloat(ref ulong hash, float value) =>
            MixInt(ref hash, BitConverter.SingleToInt32Bits(value));

        static void MixInt(ref ulong h, int value)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (byte)(value >> (i * 8));
                h *= 1099511628211UL;
            }
        }
    }

    public string CacheKeyPart() => StableHash().ToString("x16", CultureInfo.InvariantCulture);
}
