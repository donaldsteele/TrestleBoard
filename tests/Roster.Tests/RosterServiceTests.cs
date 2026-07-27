using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// The one level of undo the address book offers, and the snapshot ring behind it (PLAN.md §11 M12).
/// The rule these tests protect is stated in <see cref="RosterService"/>: <b>Ctrl+Z never crosses
/// the roster/document boundary</b>, which is why none of this touches <c>DocumentSession</c>.
/// </summary>
public sealed class RosterServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tb-roster-svc-").FullName;

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

    private RosterService NewService() => new(new RosterStore(Path.Combine(_folder, "roster.json")));

    private static Member Placeholder(string id, string name) =>
        new() { Id = id, DisplayName = name, Phone = "555-0100" };

    [Fact]
    public void AddingSomebodyIsWrittenStraightAway()
    {
        RosterService service = NewService();
        service.Save(Placeholder(service.NextMemberId(), "A Placeholder"), "Add A. Placeholder");

        Assert.Equal(1, service.Book.Count);
        Assert.Equal(1, new RosterStore(Path.Combine(_folder, "roster.json")).Load().Count);
    }

    [Fact]
    public void UndoPutsTheBookBackAsItWasAndSaysWhatItWillTakeBack()
    {
        RosterService service = NewService();
        service.Save(Placeholder("person-1", "A Placeholder"), "Add A. Placeholder");
        service.Save(Placeholder("person-1", "A Placeholder") with { Phone = "555-0199" }, "Change a phone number");

        Assert.True(service.CanUndo);
        Assert.Equal("Change a phone number", service.UndoDescription);

        Assert.True(service.Undo());
        Assert.Equal("555-0100", service.Book.Find("person-1")!.Phone);

        // One level only. Anything older is "Restore an earlier version…", which is the idiom this
        // audience already knows from photographs and documents.
        Assert.False(service.CanUndo);
        Assert.False(service.Undo());
    }

    [Fact]
    public void DeletingSomebodyIsUndoable()
    {
        RosterService service = NewService();
        service.Save(Placeholder("person-1", "A Placeholder"), "Add A. Placeholder");
        service.Delete("person-1", "Remove A. Placeholder");

        Assert.Empty(service.Book.Members);
        Assert.True(service.Undo());
        Assert.Equal("A Placeholder", service.Book.Find("person-1")!.DisplayName);
    }

    [Fact]
    public void RestoringAnEarlierVersionIsItselfUndoable()
    {
        var store = new RosterStore(Path.Combine(_folder, "roster.json"));
        var service = new RosterService(store);

        service.Save(Placeholder("person-1", "A Placeholder"), "Add A. Placeholder");
        service.Save(Placeholder("person-2", "B Placeholder"), "Add B. Placeholder");
        service.Save(Placeholder("person-3", "C Placeholder"), "Add C. Placeholder");

        RosterBackup oldest = service.Backups()[^1];
        Assert.Equal(1, oldest.MemberCount);

        service.Restore(oldest);
        Assert.Equal(1, service.Book.Count);

        Assert.True(service.Undo());
        Assert.Equal(3, service.Book.Count);
    }
}
