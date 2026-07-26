using System.Globalization;
using System.Text;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Documents;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// M2 acceptance (PLAN.md §11): fixture document built in code → save → reload → identical
/// relayout. Compares full glyph-level layout output, not just geometry.
/// </summary>
public sealed class DocumentRelayoutTests
{
    private static Document BuildFixtureDocument()
    {
        var doc = new Document
        {
            Metadata = new DocumentMetadata { LodgeName = "Placeholder Lodge No. 000", IssueMonth = 7, IssueYear = 2026 },
        };
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = Fonts.BundledFonts.BodyFamily,
            SizePt = 12f,
        });
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body-bold",
            FontFamily = Fonts.BundledFonts.BodyFamily,
            Weight = FontWeightToken.Bold,
            SizePt = 12f,
        });
        doc.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body",
            CharacterStyleRef = "body",
            LineSpacing = 1.2f,
            SpaceAfterPt = 6f,
        });

        doc.PageMasters.Add(new PageMaster { Id = "master-1" });
        var story = new Story { Id = "story-1" };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs =
            [
                new StoryRun { Text = "A. Placeholder, Worshipful Master. ", CharacterStyleRef = "body-bold" },
                new StoryRun { Text = TestData.FictionalProse },
            ],
        });
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = [new StoryRun { Text = TestData.FictionalProse }],
        });
        doc.Stories.Add(story);

        var page1 = new Page { Id = "page-1", MasterRef = "master-1" };
        page1.Blocks.Add(new ImageFrame
        {
            Id = "img-1",
            AssetRef = "img-fixture.png",
            FrameRect = new RectPt(300f, 120f, 160f, 110f),
            ZOrder = 10,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 8f,
            AltText = "Sample photo placeholder",
        });
        page1.Blocks.Add(new TextBlock
        {
            Id = "text-1",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 90f, 504f, 160f),
            ZOrder = 1,
            LinkNext = "text-2",
        });
        doc.Pages.Add(page1);

        var page2 = new Page { Id = "page-2", MasterRef = "master-1" };
        page2.Blocks.Add(new TextBlock
        {
            Id = "text-2",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 504f, 300f),
            ZOrder = 1,
        });
        doc.Pages.Add(page2);
        return doc;
    }

    private static string LayoutFingerprint(Document doc)
    {
        var sb = new StringBuilder();
        foreach (StoryLayoutPlan plan in DocumentLayoutAdapter.BuildPlans(doc))
        {
            LayoutResult result = TestData.Engine().Layout(plan.Request);
            sb.Append(CultureInfo.InvariantCulture, $"story {plan.StoryId} overset={result.IsOverset}\n");
            for (int f = 0; f < result.Frames.Count; f++)
            {
                FrameLayout frame = result.Frames[f];
                FramePlacement placement = plan.Placements[f];
                sb.Append(CultureInfo.InvariantCulture, $"frame {placement.PageId}/{placement.BlockId}\n");
                foreach (LineBox line in frame.Lines)
                {
                    sb.Append(CultureInfo.InvariantCulture, $" line p{line.ParagraphIndex} y={line.BaselineY:F4}\n");
                    foreach (LineSegment segment in line.Segments)
                    {
                        foreach (PositionedGlyphRun run in segment.Runs)
                        {
                            sb.Append(CultureInfo.InvariantCulture, $"  run x={run.OriginX:F4} size={run.SizePt:F2} glyphs=");
                            sb.AppendJoin(',', run.Glyphs);
                            sb.Append('\n');
                        }
                    }
                }
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void SaveReloadProducesIdenticalRelayout()
    {
        var package = new TboardPackage { Document = BuildFixtureDocument() };
        string before = LayoutFingerprint(package.Document);
        Assert.Contains("glyphs=", before, StringComparison.Ordinal);

        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        ms.Position = 0;
        TboardPackage reloaded = TboardContainer.Load(ms);

        Assert.Equal(before, LayoutFingerprint(reloaded.Document));
    }

    [Fact]
    public void AdapterBuildsChainAcrossPagesWithExclusionsFromWrapBlocks()
    {
        Document doc = BuildFixtureDocument();
        StoryLayoutPlan plan = Assert.Single(DocumentLayoutAdapter.BuildPlans(doc));

        Assert.Equal("story-1", plan.StoryId);
        Assert.Equal(2, plan.Request.Frames.Count);
        Assert.Equal(["page-1", "page-2"], plan.Placements.Select(p => p.PageId));

        // Page-1 frame sees the wrapped image; page-2 frame sees nothing.
        Input.ExclusionRect exclusion = Assert.Single(plan.Request.Frames[0].Exclusions);
        Assert.Equal(8f, exclusion.WrapMargin);
        Assert.Empty(plan.Request.Frames[1].Exclusions);

        // Text must actually flow around the exclusion and spill to page 2.
        LayoutResult result = TestData.Engine().Layout(plan.Request);
        Assert.True(result.Frames[1].Lines.Count > 0, "Story should spill into the second frame.");
    }

    [Fact]
    public void AdapterLowerZWrapBlockDoesNotExclude()
    {
        Document doc = BuildFixtureDocument();
        (_, Block img) = doc.FindBlock("img-1");
        img.ZOrder = 0; // beneath the text frame now

        StoryLayoutPlan plan = Assert.Single(DocumentLayoutAdapter.BuildPlans(doc));
        Assert.Empty(plan.Request.Frames[0].Exclusions);
    }
}
