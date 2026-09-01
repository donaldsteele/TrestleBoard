using System.Globalization;

namespace TrestleBoard.App.Diagnostics;

/// <summary>One command, and when it was asked for.</summary>
internal readonly record struct TrailEntry(DateTimeOffset When, string ActionId)
{
    /// <summary>
    /// How the report prints it. The action id and nothing else — an id is a name this project
    /// chose, never a name a member has, which is what makes the whole trail safe to send.
    /// </summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{When:HH:mm:ss}  {ActionId}");
}

/// <summary>
/// The last few things the user asked for, kept in memory so a problem report can say what led up
/// to the trouble (PLAN.md §11 M77).
///
/// <para><b>In memory only, and deliberately so.</b> A log file on disk is a second copy of what
/// the committee did, sitting in AppData for as long as the installation lasts, and this project
/// has a hard rule about second copies of anything to do with real people (PLAN.md §0). What is
/// kept here is a ring of <i>action ids</i> — <c>newsletter.save</c>, <c>picture.replace</c> — and
/// nothing else: never a file name, never a word of the newsletter, never anything typed. It lives
/// as long as the process and goes when it goes.</para>
///
/// <para>Two hundred entries is roughly a long afternoon's work, and the report prints the last
/// twenty of them. The ring never grows, so a session left open for a week costs the same as one
/// opened a minute ago.</para>
/// </summary>
internal sealed class ActionTrail
{
    /// <summary>How many are kept. The report prints far fewer; the rest are headroom.</summary>
    internal const int Capacity = 200;

    private readonly TrailEntry[] _entries = new TrailEntry[Capacity];
    private readonly Lock _gate = new();
    private int _next;
    private int _count;

    /// <summary>The one every surface writes to. An instance exists so the tests need no reset.</summary>
    internal static ActionTrail Shared { get; } = new();

    /// <summary>How many are held right now, up to <see cref="Capacity"/>.</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Notes that an action was asked for. Called from the runner, which every surface goes
    /// through — the menu bar, the panel, the flyout and the keyboard table — so there is one place
    /// to add this and no way for a surface to be forgotten.
    /// </summary>
    internal void Record(string actionId, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        lock (_gate)
        {
            _entries[_next] = new TrailEntry(when, actionId);
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// The most recent entries, oldest first — which is the order somebody reading the report wants
    /// them in, because they are trying to follow what happened.
    /// </summary>
    internal IReadOnlyList<TrailEntry> Recent(int howMany)
    {
        lock (_gate)
        {
            int take = Math.Clamp(howMany, 0, _count);
            var taken = new TrailEntry[take];
            for (int i = 0; i < take; i++)
            {
                // _next points at the slot the NEXT write uses, so the newest entry is behind it.
                int index = ((_next - take + i) % Capacity + Capacity) % Capacity;
                taken[i] = _entries[index];
            }

            return taken;
        }
    }
}
