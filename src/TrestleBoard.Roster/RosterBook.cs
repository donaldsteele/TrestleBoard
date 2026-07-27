using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Roster;

/// <summary>
/// The whole address book as it sits on disk (PLAN.md §11 M12): a schema version and a list of
/// people. Immutable, like the document model — every edit produces a new book, which is what makes
/// the single-level undo in <see cref="RosterService"/> a matter of keeping one reference.
/// </summary>
public sealed record RosterBook
{
    public const int CurrentSchemaVersion = 1;

    public static readonly RosterBook Empty = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<Member> Members { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    [JsonIgnore]
    public int Count => Members.Count;

    /// <summary>
    /// Drops members with no name at all and normalises the rest. A book that has been hand-edited
    /// into nonsense loses the nonsense, not the app.
    /// </summary>
    public RosterBook Normalised() => this with
    {
        SchemaVersion = SchemaVersion <= 0 ? CurrentSchemaVersion : SchemaVersion,
        Members = Members
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.DisplayName))
            .Select(m => m.Normalised())
            .Where(m => !string.IsNullOrEmpty(m.Id))
            .ToList(),
    };

    public Member? Find(string id) =>
        Members.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary>Adds or replaces one person, keeping the existing order.</summary>
    public RosterBook With(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);

        var members = Members.ToList();
        int index = members.FindIndex(m => string.Equals(m.Id, member.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            members[index] = member;
        }
        else
        {
            members.Add(member);
        }

        return this with { Members = members };
    }

    /// <summary>
    /// Removes one person. Deletion is only ever a deliberate per-person act in the People window —
    /// importing a file never removes anybody (PLAN.md §11 M12, merge policy).
    /// </summary>
    public RosterBook Without(string id) => this with
    {
        Members = Members.Where(m => !string.Equals(m.Id, id, StringComparison.Ordinal)).ToList(),
    };

    /// <summary>
    /// The order the People window lists people in: by sort name if one was given, otherwise by the
    /// name as typed, case-insensitively and culture-invariantly so two machines agree.
    /// </summary>
    public IReadOnlyList<Member> InListOrder() => Members
        .OrderBy(m => m.SortName ?? m.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(m => m.Id, StringComparer.Ordinal)
        .ToList();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(RosterBook))]
internal sealed partial class RosterJsonContext : JsonSerializerContext;
