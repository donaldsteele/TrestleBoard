using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace TrestleBoard.Roster.Tables;

/// <summary>
/// The second attempt at a spreadsheet, when the first one refuses it (M89).
///
/// <para><b>Why this exists.</b> An <c>.xlsx</c> is a zip of XML parts, and a full-featured reader
/// has to understand nearly all of them. ClosedXML reads the whole workbook — pivot caches, charts,
/// tables, defined names — and throws if any part is not in the shape it expects. That is the right
/// behaviour for a program that means to write the file back, and the wrong one for a program that
/// wants nine columns of names and telephone numbers out of the first sheet.</para>
///
/// <para>The lodge's own member system exports a workbook with a <b>pivot table</b> in it, and its
/// pivot cache made ClosedXML throw before a single row was read. The file opens perfectly in Excel.
/// The committee would have been told to "save a copy in Excel and try again" for a file that was
/// never wrong — which is exactly the sort of advice that ends with a secretary retyping a hundred
/// names.</para>
///
/// <para>So this reader knows about four parts and ignores everything else: the workbook (for sheet
/// names and order), the workbook relationships (for where each sheet lives), the shared string
/// table, and the sheets themselves. It is deliberately incurious — an unknown part cannot break a
/// reader that never opens it.</para>
/// </summary>
internal static class XlsxRawReader
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace Rels =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace DocRels =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Excel's epoch and its famous leap-year mistake, matching <c>FieldValues.FromExcelSerial</c>.
    /// Kept here rather than shared because this project's Import and Tables folders do not depend
    /// on each other, and a reader that needed the importer would be the wrong way round.
    /// </summary>
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>The built-in number formats that mean a date or a time (ECMA-376 §18.8.30).</summary>
    private static readonly HashSet<int> DateFormats =
        [14, 15, 16, 17, 18, 19, 20, 21, 22, 27, 30, 36, 45, 46, 47, 50, 57];

    /// <summary>
    /// Reads what it can, or returns null if this is not a zip of XML parts at all. A null means
    /// "not my kind of file"; an exception in here would mask the first reader's better message.
    /// </summary>
    public static TableWorkbook? TryRead(string path)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);

            XElement? workbook = ReadXml(zip, "xl/workbook.xml");
            if (workbook is null)
            {
                return null;
            }

            Dictionary<string, string> targets = ReadRelationships(zip);
            IReadOnlyList<string> shared = ReadSharedStrings(zip);
            IReadOnlyList<bool> dateStyles = ReadDateStyles(zip);

            var sheets = new List<TableSheet>();
            foreach (XElement sheet in workbook.Descendants(Main + "sheet"))
            {
                string name = sheet.Attribute("name")?.Value ?? $"Sheet{sheets.Count + 1}";
                string? id = sheet.Attribute(DocRels + "id")?.Value;
                if (id is null || !targets.TryGetValue(id, out string? part))
                {
                    continue;
                }

                XElement? content = ReadXml(zip, part);
                if (content is not null)
                {
                    sheets.Add(new TableSheet(name, ReadRows(content, shared, dateStyles)));
                }
            }

            return sheets.Count > 0 ? new TableWorkbook(path, sheets) : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or System.Xml.XmlException
            or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    private static XElement? ReadXml(ZipArchive zip, string part)
    {
        ZipArchiveEntry? entry = zip.GetEntry(part) ?? zip.GetEntry(part.TrimStart('/'));
        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream).Root;
    }

    /// <summary>Relationship id to part path, for finding each sheet's XML.</summary>
    private static Dictionary<string, string> ReadRelationships(ZipArchive zip)
    {
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        XElement? rels = ReadXml(zip, "xl/_rels/workbook.xml.rels");
        if (rels is null)
        {
            return targets;
        }

        foreach (XElement relationship in rels.Elements(Rels + "Relationship"))
        {
            string? id = relationship.Attribute("Id")?.Value;
            string? target = relationship.Attribute("Target")?.Value;
            if (id is null || target is null)
            {
                continue;
            }

            targets[id] = target.StartsWith('/')
                ? target.TrimStart('/')
                : "xl/" + target.Replace("../", string.Empty, StringComparison.Ordinal);
        }

        return targets;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive zip)
    {
        XElement? table = ReadXml(zip, "xl/sharedStrings.xml");
        if (table is null)
        {
            return [];
        }

        // Every <t> under an <si>, run together: a string split across formatting runs is one value.
        return [.. table.Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))];
    }

    /// <summary>
    /// Which cell styles mean "this number is a date". Without this a birthday cell reads back as
    /// 30149 — the failure PLAN.md names as the most predictable one in the whole import.
    /// </summary>
    private static List<bool> ReadDateStyles(ZipArchive zip)
    {
        XElement? styles = ReadXml(zip, "xl/styles.xml");
        if (styles is null)
        {
            return [];
        }

        var custom = new Dictionary<int, string>();
        foreach (XElement format in styles.Descendants(Main + "numFmt"))
        {
            if (int.TryParse(format.Attribute("numFmtId")?.Value, CultureInfo.InvariantCulture, out int id))
            {
                custom[id] = format.Attribute("formatCode")?.Value ?? string.Empty;
            }
        }

        XElement? cellFormats = styles.Element(Main + "cellXfs");
        if (cellFormats is null)
        {
            return [];
        }

        var isDate = new List<bool>();
        foreach (XElement xf in cellFormats.Elements(Main + "xf"))
        {
            int id = int.TryParse(xf.Attribute("numFmtId")?.Value, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
            isDate.Add(
                DateFormats.Contains(id)
                || (custom.TryGetValue(id, out string? code) && LooksLikeADateFormat(code)));
        }

        return isDate;
    }

    /// <summary>
    /// A custom format with day, month or year tokens in it, ignoring anything inside quotes — where
    /// a literal "d" is a letter in a word rather than a day.
    /// </summary>
    private static bool LooksLikeADateFormat(string code)
    {
        bool quoted = false;
        foreach (char c in code)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && (c is 'd' or 'D' or 'y' or 'Y' or 'm' or 'M') && !code.Contains("General", StringComparison.Ordinal))
            {
                // "m" is minutes as well as months; a format with a colon in it is a time, and a
                // time is still a date cell for our purposes — both are converted the same way.
                return true;
            }
        }

        return false;
    }

    private static List<IReadOnlyList<string>> ReadRows(
        XElement sheet, IReadOnlyList<string> shared, IReadOnlyList<bool> dateStyles)
    {
        var rows = new List<IReadOnlyList<string>>();
        XElement? data = sheet.Element(Main + "sheetData");
        if (data is null)
        {
            return rows;
        }

        foreach (XElement row in data.Elements(Main + "row"))
        {
            var cells = new Dictionary<int, string>();
            foreach (XElement cell in row.Elements(Main + "c"))
            {
                int column = ColumnOf(cell.Attribute("r")?.Value);
                if (column < 0)
                {
                    continue;
                }

                string value = ValueOf(cell, shared, dateStyles);
                if (value.Length > 0)
                {
                    cells[column] = value;
                }
            }

            if (cells.Count == 0)
            {
                rows.Add([]);
                continue;
            }

            int width = cells.Keys.Max() + 1;
            rows.Add([.. Enumerable.Range(0, width).Select(i => cells.GetValueOrDefault(i, string.Empty))]);
        }

        // Trailing empty rows say nothing and would only widen the sheet the user is shown.
        while (rows.Count > 0 && rows[^1].Count == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    private static string ValueOf(XElement cell, IReadOnlyList<string> shared, IReadOnlyList<bool> dateStyles)
    {
        string? type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));
        }

        string raw = cell.Element(Main + "v")?.Value ?? string.Empty;
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (type == "s")
        {
            return int.TryParse(raw, CultureInfo.InvariantCulture, out int index)
                && index >= 0 && index < shared.Count
                ? shared[index]
                : string.Empty;
        }

        if (type is "str" or "e" or "b")
        {
            return type == "b" ? (raw == "1" ? "TRUE" : "FALSE") : raw;
        }

        // A plain number. If its style says it is a date, it is written out as an ISO date here —
        // the same thing the first reader does, and for the same reason.
        int style = int.TryParse(cell.Attribute("s")?.Value, CultureInfo.InvariantCulture, out int s) ? s : 0;
        if (style >= 0 && style < dateStyles.Count && dateStyles[style]
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
            && serial is >= 1 and <= 2958465)
        {
            DateTime when = Epoch.AddDays(Math.Floor(serial));
            return when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    /// <summary>"B7" → 1. Returns -1 for a reference this does not understand.</summary>
    private static int ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return -1;
        }

        int column = 0;
        foreach (char c in reference)
        {
            if (char.IsAsciiLetter(c))
            {
                column = (column * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return column - 1;
    }
}
