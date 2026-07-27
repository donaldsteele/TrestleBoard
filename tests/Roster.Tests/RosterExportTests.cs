using TrestleBoard.Roster.Import;
using TrestleBoard.Roster.Tables;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// "Save as a spreadsheet…" and the round trip it exists for (PLAN.md §11 M12, §12 gate 9):
/// export → edit in Excel → re-import must move only the fields the user edited.
/// </summary>
public sealed class RosterExportTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tb-roster-export-").FullName;

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

    private static RosterBook Book() => RosterBook.Empty
        .With(new Member
        {
            Id = "person-1",
            DisplayName = "Aaron Placeholder",
            BirthMonth = 7,
            BirthDay = 4,
            Phone = "555-0100",
            Email = "aaron@example.invalid",
            Office = "Worshipful Master",
            DegreeKind = DegreeKind.Raised,
            DegreeDate = "1995-05-01",
        })
        .With(new Member
        {
            Id = "person-2",
            DisplayName = "Bertram Sample",
            BirthMonth = 12,
            BirthDay = 25,
            Phone = "555-0101",
        });

    [Fact]
    public void TheFileIsNamedForTheDayItWasMade() =>
        Assert.Equal(
            "Lodge-address-book-2026-07-27.xlsx",
            RosterExport.SuggestedFileName(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));

    /// <summary>
    /// The rule the whole file rests on. A real date cell would print a birthday as 7/4/1900, and a
    /// numeric phone number would come back as 8.03555E+09.
    /// </summary>
    [Fact]
    public void EveryCellIsWrittenAsTextSoNothingIsReinterpreted()
    {
        string path = Path.Combine(_folder, "book.xlsx");
        RosterExport.Save(Book(), path);

        TableSheet sheet = TableFileReader.Read(path).Sheets[0];

        Assert.Equal("People", sheet.Name);
        Assert.Equal(RosterExport.Headers, sheet.Rows[0]);

        // "7/4", not 45123 and not 1900-07-04.
        Assert.Equal("7/4", sheet.Cell(1, 2));
        Assert.Equal("555-0100", sheet.Cell(1, 3));
        Assert.Equal("1995-05-01", sheet.Cell(1, 7));
    }

    [Fact]
    public void PeopleAreWrittenInTheOrderTheyAreListedIn()
    {
        string path = Path.Combine(_folder, "book.xlsx");
        RosterExport.Save(Book(), path);

        TableSheet sheet = TableFileReader.Read(path).Sheets[0];
        Assert.Equal("Aaron Placeholder", sheet.Cell(1, 1));
        Assert.Equal("Bertram Sample", sheet.Cell(2, 1));
    }

    /// <summary>Export, re-import unchanged, and the address book is untouched.</summary>
    [Fact]
    public void ExportThenReImportChangesNothing()
    {
        string path = Path.Combine(_folder, "book.xlsx");
        RosterBook book = Book();
        RosterExport.Save(book, path);

        TableSheet sheet = TableFileReader.Read(path).Sheets[0];
        MergePlan plan = RosterMerge.Plan(book, sheet, 0, ColumnGuesser.GuessMapping(sheet, 0));

        Assert.False(plan.ChangesAnything);
        Assert.Equal(book.Members, plan.Result.Members);
    }

    /// <summary>
    /// PLAN §12 gate 9: edit one cell in Excel, re-import, and <em>only</em> that field moves —
    /// including when the edit was to the person's name, because the ID column matched them.
    /// </summary>
    [Fact]
    public void EditingOneCellAndReImportingMovesOnlyThatField()
    {
        string path = Path.Combine(_folder, "book.xlsx");
        RosterBook book = Book();
        RosterExport.Save(book, path);

        // Stand in for the user editing the sheet: change one phone number and one name.
        TableSheet original = TableFileReader.Read(path).Sheets[0];
        var rows = original.Rows.Select(r => r.ToList()).ToList();
        rows[1][3] = "555-9999";
        rows[2][1] = "Bertram Sample Junior";
        var edited = new TableSheet(original.Name, rows.Select(r => (IReadOnlyList<string>)r).ToList());

        MergePlan plan = RosterMerge.Plan(book, edited, 0, ColumnGuesser.GuessMapping(edited, 0));

        Member aaron = plan.Result.Find("person-1")!;
        Assert.Equal("555-9999", aaron.Phone);
        Assert.Equal("Aaron Placeholder", aaron.DisplayName);
        Assert.Equal(7, aaron.BirthMonth);
        Assert.Equal("Worshipful Master", aaron.Office);

        Member bertram = plan.Result.Find("person-2")!;
        Assert.Equal("Bertram Sample Junior", bertram.DisplayName);
        Assert.Equal("555-0101", bertram.Phone);
        Assert.Equal(12, bertram.BirthMonth);

        Assert.Equal(2, plan.Result.Count);
        Assert.Equal(2, plan.UpdatedCount);
    }
}
