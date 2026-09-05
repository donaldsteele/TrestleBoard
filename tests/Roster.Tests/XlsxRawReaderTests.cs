using System.IO.Compression;
using TrestleBoard.Roster.Tables;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// The second attempt at a spreadsheet (M89), and the workbook that made it necessary.
///
/// <para>The lodge's own member system exports an <c>.xlsx</c> with a pivot table in it. ClosedXML
/// reads every part of a workbook and threw on that pivot cache before a single row was read — so
/// TrestleBoard told the committee to open their file in Excel and save a copy, about a file that
/// opens perfectly in Excel and is not wrong. A hundred and twelve brethren would have been retyped.
/// </para>
///
/// <para>The workbooks here are built byte by byte rather than kept as fixtures: they carry no
/// people at all, and a test that writes its own input can say exactly which part is the awkward
/// one.</para>
/// </summary>
public sealed class XlsxRawReaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tb-raw-xlsx-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The whole point: a workbook ClosedXML refuses still reads, and reads correctly.
    ///
    /// <para>The pivot cache here is the shape that broke the real import — a definition claiming
    /// more records than the records part holds. If a future ClosedXML copes with it, this test
    /// still passes and still proves the fallback returns the right rows; the assertion is about the
    /// rows the user gets, not about which reader got them.</para>
    /// </summary>
    [Fact]
    public void AWorkbookWithABrokenPivotCacheStillReads()
    {
        string path = Write("pivot.xlsx", withBrokenPivotCache: true);

        TableWorkbook book = TableFileReader.Read(path);
        TableSheet sheet = book.Sheets[0];

        Assert.Equal("Members", sheet.Name);
        Assert.Equal(["Name", "Birthday", "Phone"], sheet.Rows[0]);
        Assert.Equal("A. Placeholder", sheet.Cell(1, 0));
        Assert.Equal("555-0100", sheet.Cell(1, 2));
    }

    /// <summary>
    /// A date cell is a number with a style, and reading it as a number is the failure PLAN.md names
    /// as the most predictable in the whole import. The fallback reads styles.xml for exactly this.
    /// </summary>
    [Fact]
    public void ADateCellComesBackAsADateAndNotAsANumber()
    {
        string path = Write("dates.xlsx", withBrokenPivotCache: true);

        TableSheet sheet = TableFileReader.Read(path).Sheets[0];

        // Serial 30149, styled as a date: 17 July 1982.
        Assert.Equal("1982-07-17", sheet.Cell(1, 1));
    }

    /// <summary>Both sheets, in the workbook's own order, with their own names.</summary>
    [Fact]
    public void EverySheetIsRead()
    {
        string path = Write("two-sheets.xlsx", withBrokenPivotCache: true);

        TableWorkbook book = TableFileReader.Read(path);

        Assert.Equal(["Members", "Reports"], book.Sheets.Select(s => s.Name));
    }

    /// <summary>
    /// An ordinary workbook is still read by the first reader. The fallback is a rescue, not a
    /// replacement — if it quietly took over, every future improvement to date handling, shared
    /// formulas or anything else in the main reader would stop reaching anybody.
    /// </summary>
    [Fact]
    public void AnOrdinaryWorkbookIsStillReadTheUsualWay()
    {
        string path = Write("ordinary.xlsx", withBrokenPivotCache: false);

        TableSheet sheet = TableFileReader.Read(path).Sheets[0];

        Assert.Equal("A. Placeholder", sheet.Cell(1, 0));
        Assert.Equal("1982-07-17", sheet.Cell(1, 1));
    }

    /// <summary>Something that is not a spreadsheet at all is still refused, in words.</summary>
    [Fact]
    public void AFileThatIsNotASpreadsheetIsStillRefused()
    {
        string path = Path.Combine(_folder, "not-a-spreadsheet.xlsx");
        File.WriteAllText(path, "This is a letter to the lodge, not a spreadsheet.");

        TableReadException refused = Assert.Throws<TableReadException>(() => TableFileReader.Read(path));

        Assert.Contains("could not read that spreadsheet", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A workbook with two sheets, three columns, one date-styled cell — and optionally a pivot
    /// cache whose definition promises more records than it holds, which is what the lodge's export
    /// carries and what ClosedXML refuses.
    /// </summary>
    private string Write(string name, bool withBrokenPivotCache)
    {
        string path = Path.Combine(_folder, name);
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);

        string pivotContentTypes = withBrokenPivotCache
            ? """
              <Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml"/>
              <Override PartName="/xl/pivotCache/pivotCacheRecords1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml"/>
              """
            : string.Empty;

        Add(zip, "[Content_Types].xml",
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
              <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
              {pivotContentTypes}
            </Types>
            """);

        Add(zip, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        // The pivotCaches element is the whole point of the awkward variant: without it ClosedXML
        // never opens the cache part, and a test built that way passes whether the fallback exists
        // or not. It was written that way first, and disabling the fallback did not fail it.
        string pivotCaches = withBrokenPivotCache
            ? """<pivotCaches><pivotCache cacheId="1" r:id="rId5"/></pivotCaches>"""
            : string.Empty;

        Add(zip, "xl/workbook.xml",
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Members" sheetId="1" r:id="rId1"/>
                <sheet name="Reports" sheetId="2" r:id="rId2"/>
              </sheets>
              {pivotCaches}
            </workbook>
            """);

        string pivotRelationship = withBrokenPivotCache
            ? """<Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition" Target="pivotCache/pivotCacheDefinition1.xml"/>"""
            : string.Empty;

        Add(zip, "xl/_rels/workbook.xml.rels",
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
              <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
              {pivotRelationship}
            </Relationships>
            """);

        Add(zip, "xl/sharedStrings.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="5" uniqueCount="5">
              <si><t>Name</t></si>
              <si><t>Birthday</t></si>
              <si><t>Phone</t></si>
              <si><t>A. Placeholder</t></si>
              <si><t>555-0100</t></si>
            </sst>
            """);

        // Style 1 is the date one: numFmtId 14 is the built-in short date.
        Add(zip, "xl/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
              <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
              <borders count="1"><border/></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="2">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
              </cellXfs>
            </styleSheet>
            """);

        Add(zip, "xl/worksheets/sheet1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="s"><v>1</v></c>
                  <c r="C1" t="s"><v>2</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="s"><v>3</v></c>
                  <c r="B2" s="1"><v>30149</v></c>
                  <c r="C2" t="s"><v>4</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Add(zip, "xl/worksheets/sheet2.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
            </worksheet>
            """);

        if (withBrokenPivotCache)
        {
            // A definition that promises two records over a records part holding none. This is the
            // shape ClosedXML's PivotTableCacheDefinitionPartReader threw on.
            Add(zip, "xl/pivotCache/pivotCacheDefinition1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                      r:id="rId1" recordCount="2">
                  <cacheSource type="worksheet"><worksheetSource ref="A1:C2" sheet="Members"/></cacheSource>
                  <cacheFields count="1">
                    <cacheField name="Name" numFmtId="0"><sharedItems count="1"><s v="A. Placeholder"/></sharedItems></cacheField>
                  </cacheFields>
                </pivotCacheDefinition>
                """);
            Add(zip, "xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords" Target="pivotCacheRecords1.xml"/>
                </Relationships>
                """);
            // Two records, each carrying TWO values, over a cache that declares ONE field. That
            // mismatch is what ClosedXML's records reader refuses: "the number of elements found is
            // not what was expected".
            Add(zip, "xl/pivotCache/pivotCacheRecords1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <pivotCacheRecords xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2">
                  <r><x v="0"/><x v="0"/></r>
                  <r><x v="0"/><x v="0"/></r>
                </pivotCacheRecords>
                """);
        }

        return path;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using StreamWriter writer = new(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
