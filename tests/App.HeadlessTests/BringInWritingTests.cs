using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Bringing an emailed article into the newsletter (PLAN.md §11 M66).
///
/// <para>The reader is held by <c>Core.Tests/WritingImportTests</c>. What is left for here is the
/// half that is about the newsletter: that the writing lands as an ordinary frame in <b>one undo
/// step</b>, that its paragraphs wear this app's styles, and that a picture inside the document is
/// offered rather than taken.</para>
///
/// <para>Every fixture is a <c>.docx</c> built in the test out of fictional words (§0 rule 2).</para>
/// </summary>
public sealed class BringInWritingTests
{
    /// <summary>The acceptance, in one test: <i>import is one undo step</i>.</summary>
    [Fact]
    public async Task TheWritingArrivesAsOneFrameAndOneUndoTakesItBack()
    {
        using var file = new TemporaryFile(".docx", Docx(
            Paragraph("Lodge picnic", "Heading1"),
            Paragraph("The picnic is on the fourteenth."),
            Paragraph("Bring a chair.")));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = FramesOn(window);

                window.WritingPathForTest = file.Path;
                window.BringItInAnswerForTest = true;
                await window.BringInWritingAsync();

                Assert.Equal(before + 1, FramesOn(window));

                window.Undo();
                Assert.Equal(before, FramesOn(window));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The words arrive wearing this newsletter's lettering: Word's "Heading 1" becomes the app's
    /// heading style, and everything else becomes body. Not Word's fonts, not its margins.
    /// </summary>
    [Fact]
    public async Task WordsStylesBecomeThisNewslettersOwn()
    {
        using var file = new TemporaryFile(".docx", Docx(
            Paragraph("Lodge picnic", "Heading1"),
            Paragraph("The picnic is on the fourteenth."),
            Paragraph("What to bring", "Heading2")));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.WritingPathForTest = file.Path;
                window.BringItInAnswerForTest = true;
                await window.BringInWritingAsync();

                Story story = NewestStory(window);
                Assert.Equal(["heading", "body", "subheading"], story.Paragraphs.Select(p => p.ParagraphStyleRef));
                Assert.Equal("The picnic is on the fourteenth.", story.Paragraphs[1].Runs[0].Text);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Cancelling at the card leaves the newsletter exactly as it was.</summary>
    [Fact]
    public async Task SayingNoAtTheCardChangesNothing()
    {
        using var file = new TemporaryFile(".docx", Docx(Paragraph("Some words.")));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = FramesOn(window);

                window.WritingPathForTest = file.Path;
                window.BringItInAnswerForTest = false;
                await window.BringInWritingAsync();

                Assert.Equal(before, FramesOn(window));
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A picture inside the document is <b>offered</b>, never taken: a Word file's media folder
    /// holds the author's letterhead and their signature scan as readily as the photograph they
    /// meant to send.
    /// </summary>
    [Fact]
    public async Task APictureInTheDocumentIsOfferedAndCanBeLeftOut()
    {
        using var file = new TemporaryFile(".docx", Docx(
            [Paragraph("With a picture.")],
            ("word/media/image1.png", OnePixelPng)));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int pictures = PicturesOn(window);

                window.WritingPathForTest = file.Path;
                window.BringItInAnswerForTest = true;
                window.UseEachPictureForTest = false;
                await window.BringInWritingAsync();

                Assert.Equal(0, window.PicturesTakenForTest);
                Assert.Equal(pictures, PicturesOn(window));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A file that is not a Word document at all refuses in a sentence rather than throwing — the
    /// M25 standard, and the case a committee member is most likely to reach.
    /// </summary>
    [Fact]
    public async Task AFileThatIsNotADocumentRefusesQuietlyAndChangesNothing()
    {
        using var file = new TemporaryFile(".docx", Encoding.UTF8.GetBytes("this is not a docx"));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();
                int before = FramesOn(window);

                window.WritingPathForTest = file.Path;
                window.BringItInAnswerForTest = true;
                await window.BringInWritingAsync();

                // A sentence, not an exception — and the newsletter untouched.
                Assert.Contains("could not read", window.LastErrorForTest ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, FramesOn(window));
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task APlainTextFileComesInToo()
    {
        using var file = new TemporaryFile(
            ".txt", Encoding.UTF8.GetBytes("Brother Placeholder writes.\n\nThe lodge meets on Tuesday.\n"));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.WritingPathForTest = file.Path;
                window.BringItInAnswerForTest = true;
                await window.BringInWritingAsync();

                Story story = NewestStory(window);
                Assert.Equal(2, story.Paragraphs.Count);
                Assert.All(story.Paragraphs, p => Assert.Equal("body", p.ParagraphStyleRef));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void BringingWritingInNeedsANewsletterAndSaysSoWhenThereIsNone()
    {
        Assert.False(ActionCatalog.Evaluate(ActionId.BringInWriting, new ActionContext()).IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(
            ActionCatalog.Evaluate(ActionId.BringInWriting, new ActionContext()).Reason));
        Assert.True(ActionCatalog
            .Evaluate(ActionId.BringInWriting, new ActionContext { HasDocument = true }).IsAvailable);
    }

    // ---- plumbing --------------------------------------------------------------------------------

    /// <summary>A one-pixel PNG. Fictional in the only sense a picture can be.</summary>
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static int FramesOn(MainWindow window) =>
        window.PackageForTest!.Document.Pages.Sum(p => p.Blocks.Count);

    private static int PicturesOn(MainWindow window) =>
        window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>().Count();

    private static Story NewestStory(MainWindow window) =>
        window.PackageForTest!.Document.Stories[^1];

    private static string Paragraph(string text, string? style = null) =>
        "<w:p>"
        + (style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")
        + $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static byte[] Docx(params string[] body) => Docx(body, []);

    private static byte[] Docx(string[] body, params (string Name, byte[] Bytes)[] extras)
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>" + string.Join("", body) + "</w:body></w:document>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml", Encoding.UTF8.GetBytes(xml));
            foreach ((string name, byte[] bytes) in extras)
            {
                Add(zip, name, bytes);
            }
        }

        return ms.ToArray();

        static void Add(ZipArchive zip, string name, byte[] bytes)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            using Stream s = entry.Open();
            s.Write(bytes);
        }
    }

    private sealed class TemporaryFile : IDisposable
    {
        internal TemporaryFile(string extension, byte[] bytes)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TrestleBoard-import-tests",
                Guid.NewGuid().ToString("N") + extension);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, bytes);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temporary file that will not go is the operating system's business.
            }
        }
    }
}
