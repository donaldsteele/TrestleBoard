using System.IO.Compression;
using System.Text;
using System.Xml;
using TrestleBoard.Core.Migrations;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Import;

/// <summary>
/// Reads writing out of a file somebody emailed the committee (PLAN.md §11 M66).
///
/// <para><b>Text only, and the line is drawn here rather than defended later.</b> A Word document
/// can carry tables, text boxes, footnotes, fields, tracked changes, embedded spreadsheets and
/// three kinds of numbering. Chasing any of that is how an import feature becomes a word processor:
/// each piece of fidelity looks small on its own, and the end of that road is a second layout
/// engine that disagrees with the first. What comes in is <b>paragraphs of words, mapped to this
/// app's own five paragraph styles</b>, and the pictures. Everything else is deliberately dropped,
/// and the user is told how much arrived so they can see for themselves.</para>
///
/// <para><b>Pure managed, no Word, no COM.</b> A <c>.docx</c> is a zip with XML in it, which the
/// BCL already opens — this class is a walk over <c>word/document.xml</c> with an
/// <see cref="XmlReader"/> and nothing else. That is also what lets it run on the Linux and macOS
/// builds, where there is no Word to automate even if automating Word were a good idea.</para>
///
/// <para><b>A file from outside is not trusted.</b> Sizes are capped, the reader is given no DTD
/// and no external resolver, and every failure comes out as an
/// <see cref="UnsupportedFormatException"/> carrying a sentence for the user — the M25 standard.
/// The one thing this code must never do is throw something raw at a committee member who opened
/// the wrong file.</para>
/// </summary>
public static class WritingImport
{
    /// <summary>
    /// The most text one file may bring in. A newsletter is four to six pages; a document that
    /// wants to add two million characters to one is a mistake or an attack, and either way the
    /// honest answer is to refuse rather than to hang the app laying it out.
    /// </summary>
    public const int MaximumCharacters = 400_000;

    /// <summary>The most pictures offered from one file, for the same reason.</summary>
    public const int MaximumPictures = 60;

    /// <summary>The largest single picture taken out of a document.</summary>
    public const int MaximumPictureBytes = 40 * 1024 * 1024;

    private const string DamagedWord =
        "TrestleBoard could not read that Word document. It may be damaged, or it may have been "
        + "saved in an older format — in Word, choose Save As and pick \"Word Document (.docx)\", "
        + "then try again.";

    /// <summary>Which of this app's paragraph styles a Word style becomes.</summary>
    private static readonly Dictionary<string, string> StyleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Word's "Title" is a cover heading and its "Heading 1" is a section heading; in a
        // four-page newsletter they are the same thing, and pretending otherwise would invent a
        // level the app's stylesheet does not have.
        ["Title"] = "heading",
        ["Heading1"] = "heading",
        ["Heading"] = "heading",
        ["Heading2"] = "subheading",
        ["Heading3"] = "subheading",
        ["Heading4"] = "subheading",
        ["Subtitle"] = "subheading",
        ["Quote"] = "quote",
        ["IntenseQuote"] = "quote",
        ["BlockText"] = "quote",
        ["Caption"] = "caption",
    };

    /// <summary>Reads whatever the file is, by its extension.</summary>
    public static ImportedWriting Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string extension = Path.GetExtension(path).ToLowerInvariant();
        using FileStream file = File.OpenRead(path);

        return extension switch
        {
            ".docx" => ReadWord(file),
            ".txt" => ReadPlainText(file),
            ".doc" => throw new UnsupportedFormatException(
                "That is an older Word file, which TrestleBoard cannot read. Open it in Word, "
                + "choose Save As, pick \"Word Document (.docx)\", and bring in the new file."),
            _ => throw new UnsupportedFormatException(
                "TrestleBoard can bring in Word documents (.docx) and plain text files (.txt). "
                + "That file is neither."),
        };
    }

    /// <summary>
    /// A plain text file. Blank lines separate paragraphs, and a line that looks like a heading is
    /// left as body text — guessing at headings from a text file's shape would be wrong often
    /// enough to be worse than not guessing.
    /// </summary>
    public static ImportedWriting ReadPlainText(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string text = ReadCapped(stream);
        var paragraphs = new List<ImportedParagraph>();

        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                paragraphs.Add(new ImportedParagraph(trimmed, "body"));
            }
        }

        return new ImportedWriting(paragraphs, []);
    }

    /// <summary>
    /// A <c>.docx</c>: paragraphs from <c>word/document.xml</c>, pictures from <c>word/media/</c>.
    /// </summary>
    public static ImportedWriting ReadWord(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException e)
        {
            throw new UnsupportedFormatException(DamagedWord, e);
        }

        using (zip)
        {
            // A password-protected document is a valid OLE container with one encrypted part in it,
            // so it opens cleanly and then has no document.xml — which would otherwise be reported
            // as "damaged" and send somebody looking for a fault that is not there.
            if (zip.GetEntry("EncryptedPackage") is not null)
            {
                throw new UnsupportedFormatException(
                    "That Word document is protected with a password, so TrestleBoard cannot read "
                    + "it. Open it in Word, take the password off, save it, and try again.");
            }

            ZipArchiveEntry body = zip.GetEntry("word/document.xml")
                ?? throw new UnsupportedFormatException(DamagedWord);

            List<ImportedParagraph> paragraphs;
            try
            {
                using Stream xml = body.Open();
                paragraphs = ReadParagraphs(xml);
            }
            catch (XmlException e)
            {
                throw new UnsupportedFormatException(DamagedWord, e);
            }

            return new ImportedWriting(paragraphs, ReadPictures(zip));
        }
    }

    /// <summary>
    /// Walks the body, one <c>w:p</c> at a time.
    ///
    /// <para><b>Tables are skipped whole.</b> A table's text arriving as a run of stray paragraphs
    /// would be worse than its absence: the committee would not notice the columns had gone until
    /// the newsletter was printed. The app has its own tables, and they are the answer.</para>
    /// </summary>
    private static List<ImportedParagraph> ReadParagraphs(Stream xml)
    {
        var paragraphs = new List<ImportedParagraph>();
        var text = new StringBuilder();
        int total = 0;

        string style = "body";
        string? listKind = null;
        int tableDepth = 0;
        bool inParagraph = false;
        bool skipRunText = false;

        using XmlReader reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = false,
            CloseInput = false,
        });

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "tbl":
                        tableDepth++;
                        break;

                    case "p" when tableDepth == 0:
                        inParagraph = true;
                        text.Clear();
                        style = "body";
                        listKind = null;
                        break;

                    case "pStyle" when inParagraph:
                        style = MapStyle(reader.GetAttribute("val", Namespaces.Word)
                            ?? reader.GetAttribute("val"));
                        break;

                    // A numbering reference is all the document says about whether a point is
                    // bulleted or numbered — the difference lives in numbering.xml, several
                    // indirections away. Bulleted is the honest guess: it is the commoner of the
                    // two, and a bullet where a number belonged is a smaller wrong than a number
                    // starting again at one in the middle of somebody's list.
                    case "numPr" when inParagraph:
                        listKind = ListKinds.Bullet;
                        break;

                    // Field instructions and deleted (tracked-change) text look exactly like
                    // ordinary text to a w:t reader, and both would arrive as gibberish in the
                    // middle of a sentence.
                    case "instrText":
                    case "delText":
                        skipRunText = true;
                        break;

                    case "tab" when inParagraph:
                        text.Append(' ');
                        break;

                    case "br" when inParagraph:
                        text.Append(' ');
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "tbl":
                        tableDepth = Math.Max(0, tableDepth - 1);
                        break;

                    case "instrText":
                    case "delText":
                        skipRunText = false;
                        break;

                    case "p" when inParagraph:
                        inParagraph = false;
                        string finished = Tidy(text.ToString());
                        if (finished.Length > 0)
                        {
                            total += finished.Length;
                            if (total > MaximumCharacters)
                            {
                                throw new UnsupportedFormatException(
                                    "That document is far too long to bring into a newsletter. "
                                    + "Bring in the part you need instead — open it, copy the "
                                    + "section you want into a new file, and use that.");
                            }

                            paragraphs.Add(new ImportedParagraph(finished, style, listKind));
                        }

                        break;
                }
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace
                && inParagraph && !skipRunText)
            {
                text.Append(reader.Value);
            }
        }

        return paragraphs;
    }

    /// <summary>
    /// The pictures, in the order the file stored them, with their bytes untouched.
    ///
    /// <para>Read out of <c>word/media/</c> rather than followed through the document's relationship
    /// graph. The graph would give the order they appear in the writing, which sounds better and is
    /// not worth three more XML parts of code that can go wrong: the user is shown each picture and
    /// asked, so the order they are asked in is a matter of convenience and not correctness.</para>
    /// </summary>
    private static List<ImportedPicture> ReadPictures(ZipArchive zip)
    {
        var pictures = new List<ImportedPicture>();

        foreach (ZipArchiveEntry entry in zip.Entries
            .Where(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
            .Where(e => IsPicture(e.Name))
            .OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (pictures.Count >= MaximumPictures || entry.Length > MaximumPictureBytes)
            {
                continue;
            }

            try
            {
                using Stream s = entry.Open();
                using var buffer = new MemoryStream(
                    capacity: (int)Math.Clamp(entry.Length, 0, 8 * 1024 * 1024));
                s.CopyTo(buffer);
                pictures.Add(new ImportedPicture(entry.Name, buffer.ToArray()));
            }
            catch (InvalidDataException)
            {
                // One unreadable picture costs that picture. The writing is what they came for.
            }
        }

        return pictures;
    }

    /// <summary>
    /// JPEG and PNG only — the two the app's own decoder reads (M6). A WMF or an EMF from a Word
    /// document would be offered and then refused at the last moment, which is a worse experience
    /// than never being offered.
    /// </summary>
    private static bool IsPicture(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    private static string MapStyle(string? wordStyle) =>
        wordStyle is not null && StyleMap.TryGetValue(wordStyle.Replace(" ", "", StringComparison.Ordinal), out string? ours)
            ? ours
            : "body";

    /// <summary>
    /// Runs of whitespace become single spaces, and the ends are trimmed. Word splits a sentence
    /// across runs wherever the spell checker once stopped, and the joins arrive as stray gaps.
    /// </summary>
    private static string Tidy(string text)
    {
        var tidy = new StringBuilder(text.Length);
        bool lastWasSpace = false;

        foreach (char c in text)
        {
            bool space = char.IsWhiteSpace(c);
            if (space && lastWasSpace)
            {
                continue;
            }

            tidy.Append(space ? ' ' : c);
            lastWasSpace = space;
        }

        return tidy.ToString().Trim();
    }

    private static string ReadCapped(Stream stream)
    {
        // detectEncodingFromByteOrderMarks: a text file written by Notepad in UTF-16 is common
        // enough that reading it as UTF-8 would turn somebody's article into interleaved nulls.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[MaximumCharacters + 1];
        int read = 0;

        while (read < buffer.Length)
        {
            int got = reader.Read(buffer, read, buffer.Length - read);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        if (read > MaximumCharacters)
        {
            throw new UnsupportedFormatException(
                "That file is far too long to bring into a newsletter. Bring in the part you need "
                + "instead — copy the section you want into a new file, and use that.");
        }

        return new string(buffer, 0, read);
    }

    private static class Namespaces
    {
        internal const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    }
}
