using System.Text;

namespace TrestleBoard.Roster.Tables;

/// <summary>
/// Reads a CSV the way the files a lodge secretary actually has are written, not the way the format
/// is described (PLAN.md §11 M12, "known import hazards").
///
/// Handled, each because a real file does it:
/// <list type="bullet">
/// <item><description>A UTF-8 byte-order mark, which Excel writes and which otherwise turns the
/// first header into "﻿Name" and stops it matching anything.</description></item>
/// <item><description>CRLF, LF, and a final line with no ending at all.</description></item>
/// <item><description>RFC4180 quoting: commas and line breaks inside quotes, and <c>""</c> for a
/// literal quote.</description></item>
/// <item><description><b>A semicolon delimiter</b>, which is what European Excel writes and which
/// otherwise reads as one enormous column. Sniffed from the header row.</description></item>
/// <item><description><b>Excel's <c>="555-0100"</c> text idiom</b>, which is how a spreadsheet says
/// "this really is text" and which would otherwise be stored complete with its equals sign and
/// quotes.</description></item>
/// </list>
/// </summary>
public static class CsvTableReader
{
    private static readonly char[] Candidates = [',', ';', '\t', '|'];

    public static TableWorkbook Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string text;
        try
        {
            // detectEncodingFromByteOrderMarks strips the BOM; a file WITH one is decoded by it.
            //
            // A file without one is read as UTF-8 first, which is right for anything a modern Excel
            // or Google Sheets writes — but an older Excel "Save as CSV" on Windows writes
            // Windows-1252 with no BOM at all, and decoding that as UTF-8 turns every accented
            // name into U+FFFD. Silently. The brother's name is simply wrong in the address book
            // from then on (review §14.2).
            //
            // So UTF-8 is tried STRICTLY: an invalid byte sequence throws rather than being
            // replaced, and that is the signal to fall back. A file that really is UTF-8 always
            // decodes, so the fallback can only be reached by a file that is not one.
            text = ReadStrictUtf8OrFallBack(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TableReadException(
                "TrestleBoard could not open that file. Check that it is not open in another "
                + "program, then try again.",
                e);
        }

        return new TableWorkbook(
            path,
            [new TableSheet(Path.GetFileNameWithoutExtension(path), Parse(text, SniffDelimiter(text)))]);
    }

    /// <summary>Reads text that is already in memory. The same parser; used by the tests.</summary>
    public static TableSheet ReadText(string text, string sheetName = "Pasted")
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TableSheet(sheetName, Parse(text.TrimStart('﻿'), SniffDelimiter(text)));
    }

    /// <summary>
    /// Which character separates the fields, decided from the first line only. Counting over the
    /// whole file would let a chatty Notes column outvote the real delimiter.
    /// </summary>
    internal static char SniffDelimiter(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int end = text.IndexOfAny(['\r', '\n']);
        ReadOnlySpan<char> header = end < 0 ? text : text.AsSpan(0, end);

        char best = ',';
        int bestCount = 0;
        foreach (char candidate in Candidates)
        {
            int count = 0;
            bool quoted = false;
            foreach (char c in header)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == candidate && !quoted)
                {
                    count++;
                }
            }

            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    private static List<IReadOnlyList<string>> Parse(string text, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool anyContent = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '=' when field.Length == 0 && i + 1 < text.Length && text[i + 1] == '"':
                    // Excel's ="555-0100" idiom: the equals sign is the spreadsheet saying "this is
                    // text", not part of the value. Drop it and let the quote below do its work.
                    break;

                case '"':
                    quoted = true;
                    anyContent = true;
                    break;

                case '\r':
                    // A lone CR ends the line too; CRLF is handled by skipping the LF.
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    EndRow();
                    break;

                case '\n':
                    EndRow();
                    break;

                default:
                    if (c == delimiter)
                    {
                        EndField();
                    }
                    else
                    {
                        field.Append(c);
                        anyContent = true;
                    }

                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0 || anyContent)
        {
            EndRow();
        }

        return rows;

        void EndField()
        {
            row.Add(Clean(field.ToString()));
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (row.Count > 0 && row.Any(v => v.Length > 0))
            {
                rows.Add(row.ToList());
            }
            else if (row.Count > 1)
            {
                // A row of empty fields is a blank line in the middle of a table; keep the shape.
                rows.Add(row.ToList());
            }

            row.Clear();
            anyContent = false;
        }
    }

    /// <summary>
    /// Trims, and unwraps Excel's <c>="…"</c> text idiom. Without the unwrapping a phone number
    /// exported as text arrives as <c>="555-0100"</c> and prints that way in the newsletter.
    /// </summary>
    private static string Clean(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 3
            && trimmed[0] == '='
            && trimmed[1] == '"'
            && trimmed[^1] == '"')
        {
            return trimmed[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal).Trim();
        }

        return trimmed;
    }
    /// <summary>
    /// The file's text, decoded as UTF-8 when it is UTF-8 and as Windows-1252 when it is not.
    ///
    /// <para>UTF-8 is tried with a throwing decoder, so an invalid byte sequence raises rather than
    /// being silently replaced with U+FFFD. That exception is the whole signal: a file that really
    /// is UTF-8 cannot produce it, so reaching the fallback means the file genuinely is not.</para>
    ///
    /// <para>Windows-1252 is the fallback rather than Latin-1 because it is what an older Excel on
    /// Windows actually writes, and it is a superset of Latin-1 over the bytes that differ. It also
    /// cannot fail — every byte maps to something — so there is no third case to handle.</para>
    /// </summary>
    private static string ReadStrictUtf8OrFallBack(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try
        {
            using var strict = new StreamReader(
                new MemoryStream(bytes),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            return strict.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            // CodePagesEncodingProvider is registered once in the static constructor below; without
            // it 1252 is not available on .NET outside Windows.
            using var legacy = new StreamReader(new MemoryStream(bytes), LegacyEncoding);
            return legacy.ReadToEnd();
        }
    }

    private static readonly Encoding LegacyEncoding = ResolveLegacyEncoding();

    private static Encoding ResolveLegacyEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            // Latin-1 is in the box everywhere and agrees with 1252 on every byte that matters for
            // a name; it differs only in the 0x80-0x9F range, which holds punctuation.
            return Encoding.Latin1;
        }
    }

}
