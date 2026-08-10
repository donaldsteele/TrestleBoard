using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// **Verification gate 27 — outcome honesty** (PLAN.md §12.27, §11 M73 (h)).
///
/// <para>The rule: <i>no method may announce an outcome that is not a function of a value returned
/// by the operation it announces.</i> The worst finding of M73 was one sentence — "3 pieces of text
/// were put back… Press Ctrl+Z to undo" — said after two operations had both returned early, both
/// bools discarded. Ctrl+Z would then have taken back an unrelated edit.</para>
///
/// <para>PLAN.md proposes checking it as "no discarded <c>bool</c>/<c>int</c> return on any path
/// reaching an <c>Announce</c>/<c>Tell</c>/<c>_status.Text</c> in the same method", and that is what
/// <see cref="Scan"/> does.</para>
///
/// <para><b>What this gate is, honestly: a ratchet, not a net.</b> It was run against the tree as it
/// stood before M73 — commit <c>60bd8d7</c> — and it found <b>nothing</b>. Not because the tree was
/// clean; it held every defect (b2), (e) and (f) name. It found nothing because the defining feature
/// of those defects was that the operation returned <i>nothing at all</i>:
/// <c>TextEditorController.SelectAll</c> was <c>void</c>, <c>AppSettings.Save</c> was <c>void</c>
/// with an empty catch, <c>IRecoveryStore.Delete</c> was <c>void</c>. A sentence cannot be a function
/// of a value that does not exist, and no static check over source text can see the absence of a
/// concept. M73 (e) and (f) created those return values; this gate is what stops them being
/// discarded again, and what makes the next operation that grows one obliged to be believed. It
/// cannot tell anybody that an operation ought to have one. **A hands-on pass is still worth more**
/// (PLAN.md M73, "what this milestone cannot do").</para>
///
/// <para><b>Its other limits, so a green run is not read as more than it is.</b> It reads text, not
/// a compilation: it resolves the receiver of a call only through field declarations, so a call on a
/// local, a parameter or a property is invisible; it matches methods by declared name within the
/// resolved type, so an overload that returns <c>void</c> and one that returns <c>bool</c> are the
/// same name to it; and "in the same method" is the whole body, not a real control-flow path, so it
/// can neither see a discard hidden behind a lambda nor prove that the announcement is downstream of
/// the discard.</para>
///
/// <para><b>What it must NOT flag.</b> A method that announces a refusal it was handed — the M11
/// shape — discards nothing and must sail through, and
/// <see cref="TheGateDoesNotObjectToAnHonestRefusal"/> asserts it does. So must a method that reads
/// the return and announces from it, however elaborately.</para>
/// </summary>
public sealed class OutcomeHonestyGateTests
{
    /// <summary>
    /// Discards the gate has looked at and accepted, each with the reason. This is an allow-list of
    /// **two entries at most** by intent: a long one would mean the rule is wrong, not that the code
    /// is fine.
    /// </summary>
    public static readonly (string Method, string Operation, string Why)[] Accepted =
    [
        ("ToggleActionPanel", "AppSettings.Save",
            "A false positive of the proxy rather than a violation of the rule. The sentence this "
            + "method says is about the panel — whether it is showing — and it IS a function of the "
            + "operation it announces (M70 (d) fixed that). The discarded bool belongs to a second, "
            + "unannounced operation: remembering the choice for next time. That silence is a real "
            + "question and it is recorded in docs/M73-spec.md as a follow-up, but it is not this "
            + "sentence lying about that operation."),
    ];

    // ---- the gate ---------------------------------------------------------------------------------

    /// <summary>
    /// **The gate.** Every method in the chrome and the editing layer that tells the user what
    /// happened, checked against every value-returning operation it throws away.
    /// </summary>
    [Fact]
    public void NoAnnouncementRestsOnADiscardedReturn()
    {
        ScanResult result = Scan(RepoSources());

        var unaccepted = result.Discards
            .Where(d => !Accepted.Any(a => a.Method == d.Method && a.Operation == d.Operation))
            .Select(d => $"{d.Where}: {d.Method}() throws away {d.ReturnType} {d.Operation} "
                         + $"and then announces something — “{d.Statement}”")
            .ToList();

        Assert.True(
            unaccepted.Count == 0,
            "these methods say what happened without reading what happened:\n  "
            + string.Join("\n  ", unaccepted)
            + "\n\nEither announce from the value the operation returned, or say less. The worst "
            + "version of this told the user to press Ctrl+Z after doing nothing, and Ctrl+Z then "
            + "took back an edit they had made an hour earlier. PLAN.md §12 gate 27.");
    }

    /// <summary>
    /// **That the gate can fail at all**, which is the assertion PLAN.md M73's acceptance asks for
    /// and which cannot be got the usual way: the defect it was written for no longer exists in the
    /// tree, and a pre-M73 tree has no return values for it to find (see the class comment). So the
    /// analyser is run against the defect itself, written out as it was.
    /// </summary>
    [Fact]
    public void TheAnalyserCatchesTheDefectThisGateWasWrittenFor()
    {
        // M73 (b2) as it stood: the footer offered "Put them all back" whenever the document held an
        // override, the two operations both returned early without a caret, both bools were thrown
        // away, and the sentence named a number that came from somewhere else entirely.
        const string TheDefect = """
            internal sealed class TextEditorController
            {
                public bool SelectAll() => false;
                public bool ClearFontOverride() => false;
            }

            internal sealed class Shell
            {
                private readonly TextEditorController _editor = new();

                private void ClearEveryFontOverride(int count)
                {
                    _editor.SelectAll();
                    _editor.ClearFontOverride();
                    Announce($"{count} pieces of text were put back. Press Ctrl+Z to undo.");
                }

                private void Announce(string message) { }
            }
            """;

        ScanResult result = Scan([("TheDefect.cs", TheDefect)]);

        Assert.Equal(2, result.Discards.Count);
        Assert.Contains(result.Discards, d => d.Operation == "TextEditorController.SelectAll");
        Assert.Contains(result.Discards, d => d.Operation == "TextEditorController.ClearFontOverride");
        Assert.All(result.Discards, d => Assert.Equal("ClearEveryFontOverride", d.Method));
    }

    /// <summary>
    /// The guard M71 (d) demands and gate 24 carries: **a control or a command that is refused, and
    /// says why, is M11 working correctly**. This gate is about announcements resting on nothing, and
    /// it must have no opinion at all about a refusal — nor about a method that does read what its
    /// operations returned, however many of them there are.
    /// </summary>
    [Fact]
    public void TheGateDoesNotObjectToAnHonestRefusal()
    {
        const string HonestCode = """
            internal sealed class Editor
            {
                public bool Apply() => true;
                public int PutBack() => 0;
            }

            internal sealed class Shell
            {
                private readonly Editor _editor = new();

                // The M11 shape: the catalog refused, and the reason it gave is what is said. There
                // is no operation here at all, so there is nothing for a return value to be about.
                private void RunRefused(string reason)
                {
                    Announce(reason);
                }

                // The corrected shape: every sentence is a function of what came back.
                private void PutThemAllBack()
                {
                    int put = _editor.PutBack();
                    if (put == 0)
                    {
                        Announce("There was nothing using a different font.");
                        return;
                    }

                    if (!_editor.Apply())
                    {
                        Announce("That could not be done just now.");
                        return;
                    }

                    Announce($"{put} pieces of text were put back.");
                }

                private void Announce(string message) { }
            }
            """;

        ScanResult result = Scan([("HonestCode.cs", HonestCode)]);

        Assert.True(
            result.Discards.Count == 0,
            "the gate objected to code that is doing exactly what the gate asks for: "
            + string.Join("; ", result.Discards.Select(d => $"{d.Method} / {d.Operation}"))
            + " — a gate that fails an honest refusal is switched off within a month and protects "
            + "nothing afterwards (PLAN.md M71 (d)).");

        // …and it did look: the honest method is one of the announcing methods it walked.
        Assert.True(result.MethodsThatAnnounce >= 2);
    }

    /// <summary>
    /// Anti-vacuity, as gate 24 has. Every part of this scan can silently empty: a renamed folder, a
    /// changed announcement helper, a field convention the resolver stops recognising. An empty scan
    /// passes the gate above without checking anything, which is how a test dies quietly.
    /// </summary>
    [Fact]
    public void TheMethodsBeingCheckedAreRealOnes()
    {
        ScanResult result = Scan(RepoSources());

        Assert.True(
            result.FilesRead >= 60,
            $"only {result.FilesRead} source files were read — the sweep has lost the source tree");

        Assert.True(
            result.ValueReturningMethods >= 100,
            $"only {result.ValueReturningMethods} value-returning methods were found, so there is "
            + "almost nothing the gate could ever object to");

        Assert.True(
            result.MethodsThatAnnounce >= 40,
            $"only {result.MethodsThatAnnounce} methods that announce were found — either the app has "
            + "stopped telling people what happened, or the announcement helpers have been renamed "
            + "and this gate is now watching an empty room");

        Assert.True(
            result.StatementCallsResolved >= 100,
            $"only {result.StatementCallsResolved} statement-level calls could be resolved to a "
            + "declared type; the receiver resolver has stopped working and every discard is "
            + "invisible to it");
    }

    // ---- the analyser ------------------------------------------------------------------------------

    /// <summary>One announcement resting on a value nobody read.</summary>
    public sealed record Discard(
        string Where, string Method, string Operation, string ReturnType, string Statement);

    /// <summary>What the scan found, and how much it looked at — the second half is the anti-vacuity.</summary>
    public sealed record ScanResult(
        IReadOnlyList<Discard> Discards,
        int FilesRead,
        int ValueReturningMethods,
        int MethodsThatAnnounce,
        int StatementCallsResolved);

    private static readonly Regex TypeDeclaration =
        new(@"\b(?:class|record|struct)\s+([A-Za-z_]\w*)");

    private static readonly Regex ValueReturning = new(
        @"\b(?:public|internal|private|protected)\s+"
        + @"(?:(?:static|sealed|override|virtual|async|new|partial|abstract)\s+)*"
        + @"(bool|int)\s+([A-Za-z_]\w*)\s*\(");

    private static readonly Regex FieldDeclaration = new(
        @"\b(?:private|internal|public|protected)\s+(?:(?:readonly|static|required)\s+)*"
        + @"([A-Z][\w<>,\. ]*?)\??\s+(_\w+)\s*[;=]");

    private static readonly Regex MethodDeclaration = new(
        @"^[ \t]*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)[^\n;=]*?"
        + @"\b([A-Za-z_]\w*)\s*\([^;]*?\)\s*(?:where[^\{]*)?\{",
        RegexOptions.Multiline | RegexOptions.Singleline);

    /// <summary>
    /// The four ways this app tells somebody what happened: the shell's status bar, the non-modal
    /// windows' own live regions, and a controller's <c>StatusMessage</c>, which the shell polls.
    /// </summary>
    private static readonly Regex Announcement = new(
        @"\bAnnounce\s*\(|\bTell\s*\(|_status\.Text\s*=|StatusLabel\.Text\s*=|StatusMessage\s*=");

    /// <summary>The announcers themselves are not operations; their own return is not an outcome.</summary>
    private static readonly string[] Announcers = ["Announce", "Tell", "Say"];

    private static readonly Regex StatementCall = new(
        @"^\s*(?:_\s*=\s*)?(?:await\s+)?(?:(this|_\w+)[\.\?!]+)?([A-Za-z_]\w*)\s*\(");

    /// <summary>
    /// Reads the source, then walks every method that announces and reports each statement-level
    /// call in it whose <c>bool</c> or <c>int</c> result is thrown away. An explicit <c>_ = …</c>
    /// counts as thrown away: M73 (e) found one written exactly that way.
    /// </summary>
    public static ScanResult Scan(IEnumerable<(string Name, string Source)> files)
    {
        var all = files.ToList();

        // (declaring type, method name) -> "bool" | "int"
        var returns = new Dictionary<(string, string), string>();
        // declaring type -> field name -> declared type
        var fields = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach ((string _, string source) in all)
        {
            foreach (Match m in ValueReturning.Matches(source))
            {
                if (EnclosingType(source, m.Index) is { } type)
                {
                    returns[(type, m.Groups[2].Value)] = m.Groups[1].Value;
                }
            }

            foreach (Match m in FieldDeclaration.Matches(source))
            {
                if (EnclosingType(source, m.Index) is not { } type)
                {
                    continue;
                }

                string declared = m.Groups[1].Value.Trim().Split('<')[0].Split('.').Last();
                if (!fields.TryGetValue(type, out Dictionary<string, string>? forType))
                {
                    forType = new Dictionary<string, string>(StringComparer.Ordinal);
                    fields[type] = forType;
                }

                forType[m.Groups[2].Value] = declared;
            }
        }

        List<Discard> discards = [];
        int announcing = 0;
        int resolved = 0;

        foreach ((string name, string source) in all)
        {
            foreach (Match declaration in MethodDeclaration.Matches(source))
            {
                string? type = EnclosingType(source, declaration.Index);
                string body = BodyOf(source, declaration);
                if (!Announcement.IsMatch(body))
                {
                    continue;
                }

                announcing++;

                foreach (string line in body.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith('*')
                        || !trimmed.EndsWith(");", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Match call = StatementCall.Match(line);
                    if (!call.Success)
                    {
                        continue;
                    }

                    string receiver = call.Groups[1].Value;
                    string called = call.Groups[2].Value;
                    if (Announcers.Contains(called, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    string? target = receiver is "" or "this"
                        ? type
                        : fields.TryGetValue(type ?? "", out Dictionary<string, string>? forType)
                          && forType.TryGetValue(receiver, out string? fieldType)
                            ? fieldType
                            : null;

                    if (target is null)
                    {
                        continue;
                    }

                    resolved++;
                    if (returns.TryGetValue((target, called), out string? returnType))
                    {
                        discards.Add(new Discard(
                            name,
                            declaration.Groups[1].Value,
                            $"{target}.{called}",
                            returnType,
                            trimmed));
                    }
                }
            }
        }

        return new ScanResult(discards, all.Count, returns.Count, announcing, resolved);
    }

    /// <summary>The last type declared before this point in the file — good enough for one type per file,
    /// which is this repository's convention, and the reason a nested helper resolves to its host.</summary>
    private static string? EnclosingType(string source, int position)
    {
        string? found = null;
        foreach (Match m in TypeDeclaration.Matches(source[..position]))
        {
            found = m.Groups[1].Value;
        }

        return found;
    }

    /// <summary>The braces belonging to a method declaration, matched rather than guessed.</summary>
    private static string BodyOf(string source, Match declaration)
    {
        int start = source.LastIndexOf('{', declaration.Index + declaration.Length - 1);
        int depth = 0;

        for (int i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..i];
                }
            }
        }

        return source[start..];
    }

    /// <summary>
    /// The chrome and the editing layer — the two projects M73 was scoped to. <c>Core</c>,
    /// <c>Layout</c>, <c>Rendering</c> and <c>Export.Pdf</c> announce nothing to anybody, having no
    /// user to announce to.
    /// </summary>
    private static IEnumerable<(string Name, string Source)> RepoSources()
    {
        foreach (string project in new[] { "TrestleBoard.App", "TrestleBoard.Editing" })
        {
            string root = Path.Combine(RepoRoot(), "src", project);
            foreach (string path in Directory
                         .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                         .Where(NotBuildOutput)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                yield return (project + "/" + Path.GetFileName(path), File.ReadAllText(path));
            }
        }

        static bool NotBuildOutput(string path) =>
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }
}
