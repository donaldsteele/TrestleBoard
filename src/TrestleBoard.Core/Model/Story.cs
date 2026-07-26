using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Model;

/// <summary>A contiguous span of text sharing one character format inside a paragraph.</summary>
public sealed class StoryRun
{
    public required string Text { get; set; }

    /// <summary>Optional named character style; null inherits the paragraph default.</summary>
    public string? CharacterStyleRef { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    public StoryRun Clone() => new()
    {
        Text = Text,
        CharacterStyleRef = CharacterStyleRef,
        ExtraProperties = ExtraProperties is null ? null : new Dictionary<string, JsonElement>(ExtraProperties),
    };
}

public sealed class StoryParagraph
{
    public required string ParagraphStyleRef { get; set; }

    public List<StoryRun> Runs { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>Total character count across runs (paragraph-relative coordinates, matches Layout SourceSpan).</summary>
    [JsonIgnore]
    public int Length
    {
        get
        {
            int total = 0;
            foreach (StoryRun run in Runs)
            {
                total += run.Text.Length;
            }

            return total;
        }
    }

    public StoryParagraph Clone() => new()
    {
        ParagraphStyleRef = ParagraphStyleRef,
        Runs = Runs.ConvertAll(r => r.Clone()),
        ExtraProperties = ExtraProperties is null ? null : new Dictionary<string, JsonElement>(ExtraProperties),
    };
}

/// <summary>
/// A rich-text stream (InDesign/Publisher model, PLAN.md §2): TextBlocks reference a story and
/// display it; overflow flows along the block chain. Char offsets are paragraph-relative.
/// </summary>
public sealed class Story
{
    public required string Id { get; set; }

    public List<StoryParagraph> Paragraphs { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
