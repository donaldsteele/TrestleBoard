using Docnet.Core;
using Docnet.Core.Exceptions;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using SkiaSharp;

namespace TrestleBoard.PdfPages;

/// <summary>What went wrong reading a PDF, in words for the person who chose the file.</summary>
public sealed class PdfPageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One page of a chosen PDF, as the picker needs to show it.</summary>
/// <param name="Number">1-based, the number a person would say.</param>
/// <param name="WidthPt">Page width in points, so the picker can show the right shape.</param>
/// <param name="HeightPt">Page height in points.</param>
public sealed record PdfPageInfo(int Number, int WidthPt, int HeightPt);

/// <summary>
/// Renders a page of a PDF to a picture (PLAN.md §11 M67).
///
/// <para><b>Once, at import time, and never again.</b> The page becomes an ordinary image asset in
/// the <c>.tboard</c>, so the layout engine, the PDF export and the snapshot suite never see a PDF
/// — exactly the reasoning M65 used for the emblems, and for the same reason: determinism is a
/// property of there being one rendering path, and PDFium is not it. The original PDF bytes are
/// kept in the container beside the picture (gate 7), so the page can be re-rendered sharper by a
/// later version without the committee having to find the file again.</para>
///
/// <para><b>The rasterizer may not be there.</b> PDFium is a native library, shipped per RID
/// through Docnet.Core, and a platform where it will not load is a real possibility the plan
/// anticipated. <see cref="IsAvailable"/> answers that question once, without throwing, so the
/// action catalog can refuse the whole feature in plain language rather than letting somebody
/// choose a file and then meet a crash.</para>
/// </summary>
public static class PdfPageRasterizer
{
    /// <summary>
    /// The longest side of a rendered page, in pixels. A US Letter page at this size is about 260
    /// pixels to the inch — past what the committee's printer resolves, and the reason the original
    /// PDF is kept is that "past what this printer resolves" is a claim with a shelf life.
    /// </summary>
    public const int DefaultLongestSide = 2200;

    /// <summary>The most pages this app will look at in one file.</summary>
    public const int MaximumPages = 400;

    private static bool? _available;

    /// <summary>
    /// Whether this computer can read PDFs at all — asked once and remembered.
    ///
    /// <para>Answered by actually loading the library and rendering nothing, because "does the file
    /// exist on disk" is not the question: a native library can be present and refuse to load for a
    /// missing C runtime, a processor it was not built for, or a policy that blocks it.</para>
    /// </summary>
    public static bool IsAvailable => _available ??= Probe();

    /// <summary>The sentence to show when it is not. One fact, and what it means for them.</summary>
    public const string NotAvailableReason =
        "This computer cannot read PDFs into the newsletter. Everything else works as usual — to "
        + "use a page from a PDF, open it in another program, save the page as a picture, and bring "
        + "that in with \"A picture\".";

    /// <summary>
    /// What is in the file: one entry per page. Reads nothing but the page table, so a picker can
    /// open on a two-hundred-page district bulletin without rendering all of it.
    /// </summary>
    public static IReadOnlyList<PdfPageInfo> ReadPages(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        RequireAvailable();

        using IDocReader reader = Open(pdf);
        int count = reader.GetPageCount();
        if (count <= 0)
        {
            throw new PdfPageException(
                "There are no pages in that PDF, so there is nothing to bring in.");
        }

        var pages = new List<PdfPageInfo>();
        for (int i = 0; i < Math.Min(count, MaximumPages); i++)
        {
            using IPageReader page = reader.GetPageReader(i);
            pages.Add(new PdfPageInfo(i + 1, page.GetPageWidth(), page.GetPageHeight()));
        }

        return pages;
    }

    /// <summary>
    /// One page as PNG bytes, at <paramref name="longestSide"/> pixels on its longer edge.
    ///
    /// <para>White is painted underneath. A PDF page has no background of its own — it is ink on
    /// whatever it is printed on — and a page composited onto transparency would arrive with black
    /// text on nothing, which prints as an unreadable smear on a coloured panel.</para>
    /// </summary>
    public static byte[] RenderPage(byte[] pdf, int pageNumber, int longestSide = DefaultLongestSide)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestSide, 64);
        RequireAvailable();

        // Two passes: the page's own size in points, then a scale that lands on the size asked for.
        // Docnet takes a scale factor rather than a target size, so the size has to be known first.
        double scale;
        using (IDocReader sizing = Open(pdf))
        {
            if (pageNumber > sizing.GetPageCount())
            {
                throw new PdfPageException(
                    $"That PDF does not have a page {pageNumber} in it.");
            }

            using IPageReader page = sizing.GetPageReader(pageNumber - 1);
            int longest = Math.Max(page.GetPageWidth(), page.GetPageHeight());
            scale = longest > 0 ? (double)longestSide / longest : 1.0;
        }

        using IDocReader reader = Open(pdf, scale);
        using IPageReader rendering = reader.GetPageReader(pageNumber - 1);

        int width = rendering.GetPageWidth();
        int height = rendering.GetPageHeight();
        byte[] bgra = rendering.GetImage();

        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            throw new PdfPageException(
                "TrestleBoard could not draw that page. The PDF may be damaged — try opening it in "
                + "another program to check.");
        }

        return Encode(bgra, width, height);
    }

    /// <summary>
    /// BGRA from PDFium into a PNG, on white.
    ///
    /// <para><c>FromPixelCopy</c> rather than installing the array in place: the bytes belong to
    /// the caller, and handing Skia a pointer into a managed array it does not own is the kind of
    /// bug that shows up as one corrupt page on one machine, months later.</para>
    /// </summary>
    private static byte[] Encode(byte[] bgra, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using SKImage? page = SKImage.FromPixelCopy(info, bgra, info.RowBytes);
        if (page is null)
        {
            throw new PdfPageException(
                "TrestleBoard could not draw that page. The PDF may be damaged.");
        }

        using var surface = SKSurface.Create(new SKImageInfo(
            width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(page, 0, 0);

        using SKImage flattened = surface.Snapshot();
        using SKData data = flattened.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Opens the document, turning every way PDFium can say no into a sentence.
    ///
    /// <para>The file arrived by email from the Grand Lodge or the district secretary, which is the
    /// delivery route that ends in trouble; every refusal here says what to do next rather than
    /// what went wrong inside a native library.</para>
    /// </summary>
    private static IDocReader Open(byte[] pdf, double scale = 1.0)
    {
        if (pdf.Length == 0)
        {
            throw new PdfPageException("That file is empty.");
        }

        try
        {
            return DocLib.Instance.GetDocReader(pdf, new PageDimensions(scale));
        }
        catch (DocnetLoadDocumentException e)
        {
            throw new PdfPageException(
                "TrestleBoard could not open that PDF. It may be damaged, or it may be protected "
                + "with a password — if it is, open it in another program, save an unprotected "
                + "copy, and bring that in instead.",
                e);
        }
        catch (Exception e) when (e is DocnetException or ArgumentException or InvalidOperationException)
        {
            throw new PdfPageException(
                "TrestleBoard could not read that PDF. It may be damaged, or it may not be a PDF "
                + "at all.",
                e);
        }
    }

    private static void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new PdfPageException(NotAvailableReason);
        }
    }

    /// <summary>
    /// Loads the native library once and reports whether it came up, without ever throwing. A
    /// <see cref="DllNotFoundException"/> or a <see cref="BadImageFormatException"/> here is the
    /// answer, not a fault: this computer cannot read PDFs, and the app says so and carries on.
    /// </summary>
    private static bool Probe()
    {
        try
        {
            // Asking a valid but empty request is enough to force the load. The document itself is
            // nonsense, so this throws either way; what is being distinguished is "the library
            // rejected my bytes" (fine) from "there is no library" (not fine).
            using IDocReader _ = DocLib.Instance.GetDocReader([0x25, 0x50, 0x44, 0x46], new PageDimensions(1.0));
            return true;
        }
        catch (DocnetException)
        {
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException
            or TypeInitializationException or EntryPointNotFoundException
            or PlatformNotSupportedException or FileNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            // Anything else came from the document rather than the loader — the library is here.
            return true;
        }
    }
}
