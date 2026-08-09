using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.Editing.Help;

/// <summary>
/// The "How do I…?" index, generated from the action catalog (PLAN.md §11 M63).
///
/// <para><b>Why generated.</b> This audience will not read a README on GitHub, so the help has to
/// live where the confusion does — and help that lives beside the app rots the moment the app
/// changes. Hand-written help is a promise nobody can keep: the wording drifts, a command is
/// renamed, a shortcut moves, and the help goes on confidently describing the app of eighteen
/// months ago. Generating it from <see cref="ActionCatalog"/> makes that structurally impossible.
/// Every command has a topic because every command has a catalog entry, and the catalog is already
/// the thing the menu bar, the action panel, the keyboard table and the tests all read.</para>
///
/// <para>No Avalonia, no window, no file: the index is a pure function of the catalog, which is why
/// it can be tested exhaustively without a headless session. The window in the App layer is a
/// renderer over this — the same split M51's <c>ReviewChecklist</c> and M58's
/// <c>ReadAloudSession</c> use.</para>
///
/// <para><b>What is not here:</b> the menu path. See <see cref="HelpTopic.MenuPath"/> — it is a fact
/// about XAML, and the Editing layer cannot see XAML.</para>
/// </summary>
public static class HelpIndex
{
    /// <summary>
    /// One topic per catalog action, in the catalog's own declaration order — which is menu order,
    /// so browsing with an empty search box walks the app in the order the menus present it.
    /// </summary>
    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        .. ActionCatalog.All.Select(a => new HelpTopic(
            a.Id,
            a.Title,
            a.ShortDescription,
            a.Group,
            a.DisplayGesture,
            HelpSearchWords.For(a.Id))),
    ];

    private static readonly Dictionary<string, HelpTopic> ByAction =
        Topics.ToDictionary(t => t.ActionId, StringComparer.Ordinal);

    /// <summary>The topic for one action. Throws for an unknown id — that is a programming error.</summary>
    public static HelpTopic For(string actionId) =>
        ByAction.TryGetValue(actionId, out HelpTopic? topic)
            ? topic
            : throw new ArgumentOutOfRangeException(
                nameof(actionId), actionId, "No help topic; every catalog action has one, so this id is not one.");

    /// <summary>
    /// Answers for what the user typed, best first. An empty box returns everything, in menu order,
    /// because the window doubles as a way to browse what the app can do at all.
    ///
    /// <para>All the words must match something — typing two words narrows rather than widens.
    /// Someone who types "picture caption" means both, and a search that answered with every photo
    /// command and every caption command would bury the one thing they asked for.</para>
    /// </summary>
    public static IReadOnlyList<HelpTopic> Search(string? query)
    {
        string[] words = Words(query);
        if (words.Length == 0)
        {
            return Topics;
        }

        var hits = new List<(HelpTopic Topic, int Score, int Order)>();
        for (int i = 0; i < Topics.Count; i++)
        {
            HelpTopic topic = Topics[i];
            int total = 0;
            bool everyWordFound = true;

            foreach (string word in words)
            {
                int best = ScoreWord(topic, word);
                if (best == 0)
                {
                    everyWordFound = false;
                    break;
                }

                total += best;
            }

            if (everyWordFound)
            {
                // The whole query written out as the command's name beats any word-by-word total:
                // somebody who typed "bold" wants Bold, not every command mentioning bold text.
                if (topic.Title.Trim(' ', '…').Equals(query!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    total += 100;
                }

                hits.Add((topic, total, i));
            }
        }

        return
        [
            .. hits.OrderByDescending(h => h.Score).ThenBy(h => h.Order).Select(h => h.Topic),
        ];
    }

    /// <summary>
    /// How well one typed word matches one topic. The ordering of these numbers is the whole of the
    /// ranking policy: what the command is called beats what a confused user might call it, which
    /// beats the sentence describing it, which beats the part of the app it lives in.
    /// </summary>
    private static int ScoreWord(HelpTopic topic, string word) =>
        StartsAnyWord(topic.Title, word) ? 8
        : topic.OtherWords.Any(w => StartsAnyWord(w, word)) ? 6
        : StartsAnyWord(topic.Answer, word) ? 3
        : StartsAnyWord(topic.GroupLabel, word) ? 2
        : 0;

    /// <summary>
    /// Prefix rather than equality, so "align" finds "Aligns" and "photo" finds "photograph".
    /// Prefix rather than <i>contains</i>, so "ear" does not find "Year" — a substring match on
    /// short words turns a search box into a random-answer machine.
    /// </summary>
    private static bool StartsAnyWord(string text, string word) =>
        Words(text).Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    private static string[] Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split(
                    [' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '"', '(', ')', '…', '—', '-', '\''],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
