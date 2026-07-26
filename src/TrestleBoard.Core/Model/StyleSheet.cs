using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Model;

public enum TextAlignment
{
    Left,
    Center,
    Right,
}

public enum FontWeightToken
{
    Regular,
    Bold,
}

public enum FontSlantToken
{
    Normal,
    Italic,
}

/// <summary>Named character formatting. Referenced from runs and paragraph styles by name.</summary>
public sealed class CharacterStyleDef
{
    public required string Name { get; set; }

    /// <summary>Bundled font family name (PLAN.md §1: bundled OFL fonts only).</summary>
    public required string FontFamily { get; set; }

    public FontWeightToken Weight { get; set; } = FontWeightToken.Regular;

    public FontSlantToken Slant { get; set; } = FontSlantToken.Normal;

    public float SizePt { get; set; } = 12f;

    public uint ColorArgb { get; set; } = 0xFF000000;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>Named paragraph formatting; carries the default character style by reference.</summary>
public sealed class ParagraphStyleDef
{
    public required string Name { get; set; }

    /// <summary>Name of the <see cref="CharacterStyleDef"/> supplying the default run format.</summary>
    public required string CharacterStyleRef { get; set; }

    /// <summary>Multiplier over natural line height (ascent+descent+leading).</summary>
    public float LineSpacing { get; set; } = 1f;

    public float SpaceBeforePt { get; set; }

    public float SpaceAfterPt { get; set; }

    public float FirstLineIndentPt { get; set; }

    public TextAlignment Align { get; set; } = TextAlignment.Left;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>Named frame decoration (borders/fills for text frames and widgets). Minimal in M2.</summary>
public sealed class FrameStyleDef
{
    public required string Name { get; set; }

    public uint? FillArgb { get; set; }

    public uint? StrokeArgb { get; set; }

    public float StrokeWidthPt { get; set; }

    public float PaddingPt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>Named table appearance for widget tables. Minimal in M2; widgets flesh this out in M7.</summary>
public sealed class TableStyleDef
{
    public required string Name { get; set; }

    public string? HeaderCharacterStyleRef { get; set; }

    public string? BodyCharacterStyleRef { get; set; }

    public uint RuleArgb { get; set; } = 0xFF000000;

    public float RuleWidthPt { get; set; } = 0.5f;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class StyleSheet
{
    public List<ParagraphStyleDef> ParagraphStyles { get; set; } = [];

    public List<CharacterStyleDef> CharacterStyles { get; set; } = [];

    public List<FrameStyleDef> FrameStyles { get; set; } = [];

    public List<TableStyleDef> TableStyles { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    public ParagraphStyleDef GetParagraphStyle(string name) =>
        ParagraphStyles.Find(s => s.Name == name)
            ?? throw new KeyNotFoundException($"Paragraph style not found: {name}");

    public CharacterStyleDef GetCharacterStyle(string name) =>
        CharacterStyles.Find(s => s.Name == name)
            ?? throw new KeyNotFoundException($"Character style not found: {name}");
}

/// <summary>Document-wide design tokens (PLAN.md §2). Consumed by styles and widgets.</summary>
public sealed class Theme
{
    public Dictionary<string, uint> ColorTokens { get; set; } = [];

    public Dictionary<string, string> FontTokens { get; set; } = [];

    public List<float> SpacingScale { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
