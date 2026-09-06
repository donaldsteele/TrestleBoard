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

    /// <summary>
    /// Whether pages on this master carry the line along the bottom (M78) — the lodge name, the
    /// issue, and "page 3 of 6".
    ///
    /// <para><b>On the master rather than in the metadata, because a master is what a repeating
    /// piece of page furniture belongs to.</b> Every fact the line prints comes from somewhere
    /// else — the lodge name and the issue date from the document, the numbers from counting — so
    /// what is stored here is only the decision to show it.</para>
    ///
    /// <para><b>False by default, true in the three templates.</b> A newsletter written before M78
    /// has no such property in its file, and it must open looking exactly as it did yesterday; a
    /// newsletter started after it gets the footer every issue from the old tool had. The
    /// difference is written in the templates, which is where a default that only applies to new
    /// work belongs.</para>
    /// </summary>
    public bool ShowFooter { get; set; }

    /// <summary>
    /// Whether the front page is left out of the line along the bottom (PLAN.md §11 M107).
    ///
    /// <para><b>The cover is a cover.</b> "Indian Land Lodge 414 · September 2026 · page 1 of 6"
    /// printed under a cover heading that already says the lodge and the month says all three facts
    /// twice, in a place the eye reads first — and no printed newsletter the committee has ever
    /// produced numbered its own front page.</para>
    ///
    /// <para><b>Phrased as HIDE, defaulting to false, so that a file without the property opens
    /// looking exactly as it did yesterday</b> — the same additive rule
    /// <see cref="ShowFooter"/> follows, and the reason this is not "FooterOnFirstPage" defaulting
    /// to true: a missing property must mean "carry on as before", and with the positive phrasing
    /// it would mean "stop printing the number on page 1" for every newsletter already written.</para>
    ///
    /// <para><b>It says nothing about the count.</b> Page 2 is still "page 2 of 6" — the cover is
    /// still a page of the newsletter, and a reader holding sheet 2 of 6 needs the number they can
    /// actually count to. Renumbering so the cover is page 0 would make the footer disagree with
    /// the sheets in the reader's hand.</para>
    /// </summary>
    public bool HideFooterOnFirstPage { get; set; }

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
