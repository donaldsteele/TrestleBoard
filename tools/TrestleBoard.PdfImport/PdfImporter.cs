using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using SharpPdfDocument = PdfSharpCore.Pdf.PdfDocument;
using SharpPdfPage = PdfSharpCore.Pdf.PdfPage;
using SharpPdfReader = PdfSharpCore.Pdf.IO.PdfReader;
using SharpPdfDocumentOpenMode = PdfSharpCore.Pdf.IO.PdfDocumentOpenMode;

namespace TrestleBoard.PdfImport;

/// <summary>
/// Converts one PDF into a best-effort editable .tboard package: Docnet.Core (PDFium) supplies
/// character positions for text-column/paragraph reconstruction, PdfSharpCore's content-stream
/// reader supplies embedded JPEG placement for picture frames. See the project doc comment for
/// what this deliberately does not attempt to reproduce.
/// </summary>
internal static class PdfImporter
{
    public static TboardPackage Convert(string pdfPath, string title)
    {
        var document = new Document
        {
            Metadata = new DocumentMetadata { Title = title },
        };
        StandardStyles.Add(document);

        var package = new TboardPackage { Document = document };

        using SharpPdfDocument sharpDocument = SharpPdfReader.Open(pdfPath, SharpPdfDocumentOpenMode.Import);
        using var docLib = DocLib.Instance;
        // scalingFactor 1.0 == 1 pixel per point, so Docnet's pixel space equals RectPt's points
        // exactly (no DPI conversion, only y stays already top-left/down like RectPt expects).
        using IDocReader docReader = docLib.GetDocReader(pdfPath, new PageDimensions(1.0));

        var masterCache = new Dictionary<(int Width, int Height), string>();
        int pageCount = docReader.GetPageCount();

        for (int i = 0; i < pageCount; i++)
        {
            using IPageReader pageReader = docReader.GetPageReader(i);
            int widthPt = pageReader.GetPageWidth();
            int heightPt = pageReader.GetPageHeight();
            int pageNumber = i + 1;

            string masterId = GetOrAddMaster(document, masterCache, widthPt, heightPt);
            var page = new Page { Id = $"page-{pageNumber}", MasterRef = masterId };

            AddTextColumns(document, page, pageReader, pageNumber);

            if (i < sharpDocument.PageCount)
            {
                SharpPdfPage sharpPage = sharpDocument.Pages[i];
                AddImages(package, page, sharpPage, pageNumber, heightPt);
            }

            document.Pages.Add(page);
        }

        return package;
    }

    private static string GetOrAddMaster(Document document, Dictionary<(int, int), string> cache, int widthPt, int heightPt)
    {
        (int widthPt, int heightPt) key = (widthPt, heightPt);
        if (cache.TryGetValue(key, out string? existingId))
        {
            return existingId;
        }

        string id = $"master-{widthPt}x{heightPt}";
        document.PageMasters.Add(new PageMaster
        {
            Id = id,
            Size = new SizePt(widthPt, heightPt),
            MarginLeftPt = 0,
            MarginTopPt = 0,
            MarginRightPt = 0,
            MarginBottomPt = 0,
        });
        cache[key] = id;
        return id;
    }

    private static void AddTextColumns(Document document, Page page, IPageReader pageReader, int pageNumber)
    {
        List<Character> characters;
        try
        {
            characters = pageReader.GetCharacters().ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  page {pageNumber}: text extraction failed ({ex.Message}), skipping text");
            return;
        }

        List<ColumnRegion> columns = TextClustering.BuildColumns(characters);
        int columnIndex = 0;
        foreach (ColumnRegion column in columns)
        {
            columnIndex++;
            string storyId = $"story-p{pageNumber}-{columnIndex}";
            var story = new Story { Id = storyId };
            foreach (ParagraphText paragraph in column.Paragraphs)
            {
                story.Paragraphs.Add(new StoryParagraph
                {
                    ParagraphStyleRef = paragraph.Style,
                    Runs = [new StoryRun { Text = paragraph.Text }],
                });
            }

            document.Stories.Add(story);
            page.Blocks.Add(new TextBlock
            {
                Id = $"frame-p{pageNumber}-{columnIndex}",
                StoryRef = storyId,
                FrameRect = new RectPt(column.X, column.Y, column.Width, column.Height),
                ZOrder = columnIndex,
            });
        }
    }

    private static void AddImages(TboardPackage package, Page page, SharpPdfPage sharpPage, int pageNumber, float pageHeightPt)
    {
        List<PlacedImage> placed;
        try
        {
            placed = ImageExtractor.ExtractPlacedImages(sharpPage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  page {pageNumber}: image extraction failed ({ex.Message}), skipping images");
            return;
        }

        int index = 0;
        foreach (PlacedImage placement in placed)
        {
            index++;
            byte[]? jpeg = ImageExtractor.TryGetJpegBytes(placement.XObject);
            if (jpeg is null || jpeg.Length == 0)
            {
                Console.WriteLine($"  page {pageNumber}: skipped embedded image #{index} (not a JPEG/DCTDecode image, the only format this tool extracts)");
                continue;
            }

            RectPt rect = ImageExtractor.ComputeRect(placement.Ctm, pageHeightPt);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            string assetName = $"img-page{pageNumber}-{index}.jpg";
            package.Assets[assetName] = jpeg;
            page.Blocks.Add(new ImageFrame
            {
                Id = $"img-p{pageNumber}-{index}",
                AssetRef = assetName,
                FrameRect = rect,
                ZOrder = 100 + index,
                Fit = ImageFit.Cover,
                AltText = "",
            });
        }
    }
}
