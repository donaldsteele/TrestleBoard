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

/// <summary>
/// What kind of list a paragraph belongs to (PLAN.md §11 M61). Null — the overwhelmingly common
/// case — is ordinary writing, so a document written before M61 is unchanged on disk.
/// </summary>
public static class ListKinds
{
    /// <summary>A point in a list of points.</summary>
    public const string Bullet = "bullet";

    /// <summary>A numbered point. The number itself is never stored — see the layout adapter.</summary>
    public const string Number = "number";

    public static bool IsKnown(string? kind) => kind is null or Bullet or Number;
}

public sealed class StoryParagraph
{
    public required string ParagraphStyleRef { get; set; }

    /// <summary>
    /// One of <see cref="ListKinds"/>, or null for ordinary writing (M61).
    ///
    /// <para><b>The number of a numbered point is deliberately not stored.</b> It is worked out at
    /// layout time from the run of numbered paragraphs it belongs to, so inserting a point in the
    /// middle renumbers everything after it with no edit to the document at all. A stored number
    /// would be a second copy of a fact the order already carries, and the two would disagree the
    /// first time somebody dragged a paragraph.</para>
    /// </summary>
    public string? ListKind { get; set; }

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
        ListKind = ListKind,
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
