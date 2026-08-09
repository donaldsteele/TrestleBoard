using System.IO.Compression;
using System.Text;
using TrestleBoard.Core.Import;
using TrestleBoard.Core.Migrations;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// Reading writing out of somebody else's file (PLAN.md §11 M66).
///
/// <para>Every fixture here is a <c>.docx</c> built in the test out of fictional words (§0 rule 2)
/// — no committee member's article is committed to this repository, and a hand-built zip is also
/// the only way to write the damaged and password-protected cases at all.</para>
/// </summary>
public sealed class WritingImportTests
{
    // ---- the words ---------------------------------------------------------------------------------

    [Fact]
    public void TheWordsComeInAsParagraphs()
    {
        ImportedWriting read = Word(
            Paragraph("Lodge picnic", "Heading1"),
            Paragraph("The picnic is on the fourteenth."),
            Paragraph("Bring a chair."));

        Assert.Equal(3, read.Paragraphs.Count);
        Assert.Equal("The picnic is on the fourteenth.", read.Paragraphs[1].Text);
        Assert.Equal(11, read.WordCount);
    }

    /// <summary>
    /// Word's styles become this app's five, and nothing else survives. "Heading 1" and "Title" are
    /// the same thing in a four-page newsletter, and pretending otherwise would invent a level the
    /// stylesheet does not have.
    /// </summary>
    [Theory]
    [InlineData("Heading1", "heading")]
    [InlineData("Heading 1", "heading")]
    [InlineData("Title", "heading")]
    [InlineData("Heading2", "subheading")]
    [InlineData("Heading3", "subheading")]
    [InlineData("Subtitle", "subheading")]
    [InlineData("Quote", "quote")]
    [InlineData("IntenseQuote", "quote")]
    [InlineData("Caption", "caption")]
    [InlineData("BodyText", "body")]
    [InlineData("SomeStyleNobodyHasEverHeardOf", "body")]
    public void WordsStylesBecomeOurs(string wordStyle, string ours)
    {
        ImportedWriting read = Word(Paragraph("Some words.", wordStyle));

        Assert.Equal(ours, read.Paragraphs[0].StyleRef);
    }

    /// <summary>
    /// A sentence Word split across runs — which it does wherever the spell checker once stopped —
    /// arrives as one sentence rather than with gaps in it.
    /// </summary>
    [Fact]
    public void ASentenceSplitAcrossRunsComesBackWhole()
    {
        ImportedWriting read = Word(
            "<w:p><w:r><w:t xml:space=\"preserve\">The Worshipful </w:t></w:r>"
            + "<w:r><w:t>Master</w:t></w:r>"
            + "<w:r><w:t xml:space=\"preserve\"> will preside.</w:t></w:r></w:p>");

        Assert.Equal("The Worshipful Master will preside.", read.Paragraphs[0].Text);
    }

    [Fact]
    public void EmptyParagraphsAreDropped()
    {
        ImportedWriting read = Word(
            Paragraph("First."),
            "<w:p/>",
            "<w:p><w:r><w:t>   </w:t></w:r></w:p>",
            Paragraph("Second."));

        Assert.Equal(["First.", "Second."], read.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void ATabOrALineBreakBecomesASpaceRatherThanRunningWordsTogether()
    {
        ImportedWriting read = Word(
            "<w:p><w:r><w:t>Dinner</w:t><w:tab/><w:t>six o'clock</w:t>"
            + "<w:br/><w:t>Meeting at seven</w:t></w:r></w:p>");

        Assert.Equal("Dinner six o'clock Meeting at seven", read.Paragraphs[0].Text);
    }

    /// <summary>M61's lists: a numbered paragraph arrives as a list point rather than as prose.</summary>
    [Fact]
    public void AListPointArrivesAsAListPoint()
    {
        ImportedWriting read = Word(
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/></w:numPr></w:pPr>"
            + "<w:r><w:t>Bring a chair.</w:t></w:r></w:p>",
            Paragraph("Ordinary writing."));

        Assert.Equal(ListKinds.Bullet, read.Paragraphs[0].ListKind);
        Assert.Null(read.Paragraphs[1].ListKind);
    }

    // ---- the line the spec draws --------------------------------------------------------------------

    /// <summary>
    /// **Tables are skipped whole.** Their text arriving as a run of stray paragraphs would be
    /// worse than its absence: nobody would notice the columns had gone until it was printed.
    /// </summary>
    [Fact]
    public void ATableIsLeftBehindEntirelyRatherThanArrivingAsLooseWords()
    {
        ImportedWriting read = Word(
            Paragraph("Before the table."),
            "<w:tbl><w:tr><w:tc>" + Paragraph("Senior Warden") + "</w:tc>"
            + "<w:tc>" + Paragraph("555-0100") + "</w:tc></w:tr></w:tbl>",
            Paragraph("After the table."));

        Assert.Equal(["Before the table.", "After the table."], read.Paragraphs.Select(p => p.Text));
    }

    /// <summary>
    /// Field codes and tracked deletions read as ordinary text to anything that only looks at
    /// <c>w:t</c>, and both would arrive as gibberish in the middle of a sentence.
    /// </summary>
    [Fact]
    public void FieldCodesAndDeletedTextDoNotComeThrough()
    {
        ImportedWriting read = Word(
            "<w:p><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r>"
            + "<w:r><w:instrText>PAGE \\* MERGEFORMAT</w:instrText></w:r>"
            + "<w:r><w:t>1</w:t></w:r>"
            + "<w:del><w:r><w:delText> and this was struck out</w:delText></w:r></w:del></w:p>");

        Assert.Equal("Page 1", read.Paragraphs[0].Text);
    }

    // ---- the pictures ------------------------------------------------------------------------------

    /// <summary>
    /// Gate 7: the bytes that went into Word come out of it untouched, so a picture that arrived
    /// this way can still be re-cropped in three years.
    /// </summary>
    [Fact]
    public void PicturesComeOutByteForByte()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

        ImportedWriting read = ReadDocx(
            Document(Paragraph("With a picture.")),
            ("word/media/image1.jpeg", jpeg));

        Assert.Single(read.Pictures);
        Assert.Equal("image1.jpeg", read.Pictures[0].Name);
        Assert.Equal(jpeg, read.Pictures[0].Bytes);
    }

    /// <summary>
    /// Only the two kinds the app's own decoder reads. Offering a WMF and refusing it a moment
    /// later is a worse experience than never offering it.
    /// </summary>
    [Fact]
    public void OnlyPicturesThisAppCanActuallyReadAreOffered()
    {
        ImportedWriting read = ReadDocx(
            Document(Paragraph("Words.")),
            ("word/media/image1.png", [0x89, 0x50, 0x4E, 0x47]),
            ("word/media/image2.wmf", [0x01, 0x02]),
            ("word/media/image3.emf", [0x03, 0x04]),
            ("word/media/image4.jpg", [0xFF, 0xD8]));

        Assert.Equal(["image1.png", "image4.jpg"], read.Pictures.Select(p => p.Name));
    }

    [Fact]
    public void ADocumentWithNoPicturesOffersNone() =>
        Assert.Empty(Word(Paragraph("Words.")).Pictures);

    // ---- refusing, in sentences ----------------------------------------------------------------------

    /// <summary>
    /// A protected document opens cleanly as a container and simply has no document.xml in it, so
    /// without this it would be reported as damaged and send somebody hunting a fault that is not
    /// there.
    /// </summary>
    [Fact]
    public void APasswordProtectedDocumentSaysSoAndSaysWhatToDo()
    {
        byte[] locked = Zip(("EncryptedPackage", [0x01, 0x02, 0x03]));

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => WritingImport.ReadWord(new MemoryStream(locked)));

        Assert.Contains("password", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("take the password off", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFileThatIsNotAZipAtAllIsADamagedDocument()
    {
        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => WritingImport.ReadWord(new MemoryStream(Encoding.UTF8.GetBytes("This is not a docx."))));

        Assert.Contains("could not read", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AZipWithNoDocumentInItIsADamagedDocument() =>
        Assert.Throws<UnsupportedFormatException>(
            () => WritingImport.ReadWord(new MemoryStream(Zip(("readme.txt", [0x41])))));

    /// <summary>
    /// Truncated XML throws <c>XmlException</c>, which used to be the shape of bug M25 was written
    /// about: the corrupt file, the one case the plain-language contract exists for, is the one
    /// case that produces an unhandled-exception dialog.
    /// </summary>
    [Fact]
    public void TruncatedXmlIsADamagedDocumentAndNotARawXmlException()
    {
        byte[] docx = Zip(("word/document.xml", Encoding.UTF8.GetBytes("<w:document><w:body><w:p>")));

        Assert.Throws<UnsupportedFormatException>(
            () => WritingImport.ReadWord(new MemoryStream(docx)));
    }

    /// <summary>
    /// A document that wants to add hundreds of thousands of characters to a six-page newsletter is
    /// a mistake or an attack, and either way refusing beats hanging the app laying it out.
    /// </summary>
    [Fact]
    public void AnAbsurdlyLongDocumentIsRefusedRatherThanLaidOut()
    {
        string huge = new('x', 5_000);
        string[] many = [.. Enumerable.Range(0, 100).Select(_ => Paragraph(huge))];

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(() => Word(many));
        Assert.Contains("too long", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An XML file cannot talk this reader into fetching anything from anywhere.</summary>
    [Fact]
    public void ADocumentCannotTalkTheReaderIntoFetchingSomething()
    {
        byte[] docx = Zip(("word/document.xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><!DOCTYPE w:document [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body><w:p><w:r><w:t>&x;</w:t></w:r></w:p></w:body></w:document>")));

        // Prohibited outright: the reader is given no DTD and no resolver, so this is a damaged
        // document as far as the app is concerned, and that is exactly the right answer.
        Assert.Throws<UnsupportedFormatException>(
            () => WritingImport.ReadWord(new MemoryStream(docx)));
    }

    // ---- plain text ------------------------------------------------------------------------------------

    [Fact]
    public void APlainTextFileComesInAsBodyParagraphs()
    {
        ImportedWriting read = WritingImport.ReadPlainText(
            new MemoryStream(Encoding.UTF8.GetBytes("Lodge picnic\n\nThe picnic is on the fourteenth.\n")));

        Assert.Equal(["Lodge picnic", "The picnic is on the fourteenth."], read.Paragraphs.Select(p => p.Text));

        // Deliberately no heading guessing: a short first line is not evidence, and being wrong
        // about it would be worse than leaving the committee to press the heading button.
        Assert.All(read.Paragraphs, p => Assert.Equal("body", p.StyleRef));
    }

    [Fact]
    public void ATextFileSavedByNotepadInUtf16StillReads()
    {
        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Brother Placeholder writes.")];

        ImportedWriting read = WritingImport.ReadPlainText(new MemoryStream(utf16));

        Assert.Equal("Brother Placeholder writes.", read.Paragraphs[0].Text);
    }

    [Fact]
    public void AnEmptyFileIsEmptyRatherThanAnError() =>
        Assert.True(WritingImport.ReadPlainText(new MemoryStream([])).IsEmpty);

    // ---- plumbing ----------------------------------------------------------------------------------------

    private static string Paragraph(string text, string? style = null) =>
        "<w:p>"
        + (style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")
        + $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Document(params string[] body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
        + "<w:body>" + string.Join("", body) + "</w:body></w:document>";

    private static ImportedWriting Word(params string[] body) => ReadDocx(Document(body));

    private static ImportedWriting ReadDocx(string documentXml, params (string Name, byte[] Bytes)[] extras)
    {
        (string, byte[])[] entries =
        [
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
            .. extras.Select(e => (e.Name, e.Bytes)),
        ];

        return WritingImport.ReadWord(new MemoryStream(Zip(entries)));
    }

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using Stream s = entry.Open();
                s.Write(bytes);
            }
        }

        return ms.ToArray();
    }
}
