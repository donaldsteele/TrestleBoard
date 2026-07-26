using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Model;

/// <summary>Reusable page geometry + background decoration (PLAN.md §2). US Letter default.</summary>
public sealed class PageMaster
{
    public required string Id { get; set; }

    public SizePt Size { get; set; } = new(612f, 792f);

    public float MarginLeftPt { get; set; } = 54f;

    public float MarginTopPt { get; set; } = 54f;

    public float MarginRightPt { get; set; } = 54f;

    public float MarginBottomPt { get; set; } = 54f;

    /// <summary>Background/decoration blocks drawn beneath page content.</summary>
    public List<Block> Blocks { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class Page
{
    public required string Id { get; set; }

    public required string MasterRef { get; set; }

    public List<Block> Blocks { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    public Block GetBlock(string blockId) =>
        Blocks.Find(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block not found on page {Id}: {blockId}");
}
