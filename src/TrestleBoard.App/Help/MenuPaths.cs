using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace TrestleBoard.App.Help;

/// <summary>
/// Where each command sits in the menu bar, read off the menu bar itself (PLAN.md §11 M63).
///
/// <para><b>Read, not written down.</b> M63's help has to tell somebody where to find a command,
/// and the obvious way to do that is to type "File ▸ Make the PDF…" into a help topic. That would
/// be wrong within a month: the menus have been reorganised twice already (M11 and M17), and a
/// hand-written path is a claim nobody re-checks. Every menu item already carries the action id it
/// runs in its <c>Tag</c> — that is the M11 discipline, and it means the menu bar can simply be
/// asked. Walking it costs a few milliseconds once per window.</para>
///
/// <para>The headers carry access-key underscores (<c>"_Help"</c>, <c>"F_ormat"</c>), which are
/// markup for the keyboard and not part of the name — they are stripped, and a doubled underscore
/// unescapes to the single literal one Avalonia renders.</para>
/// </summary>
internal static class MenuPaths
{
    /// <summary>The separator between the levels — the same arrow the wizards use for a next step.</summary>
    internal const string Arrow = " ▸ ";

    /// <summary>
    /// Every action that has a menu item, mapped to the path a person would read aloud.
    ///
    /// <para>Only <see cref="MenuItem"/>s are considered: two toolbar buttons carry a
    /// <c>Tag</c> too, and a toolbar button has no path.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, string> From(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MenuItem item in root.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (item.Tag is not string id || string.IsNullOrEmpty(id))
            {
                continue;
            }

            string path = PathTo(item);
            if (path.Length > 0)
            {
                // First one wins: an action reached from two places is described by the first the
                // walk meets, which is the topmost menu — and that is the one a person is told.
                paths.TryAdd(id, path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Climbs from a leaf to the menu bar, collecting headers on the way. The leaf's own header
    /// comes last, so the result reads the way somebody would say it.
    /// </summary>
    private static string PathTo(MenuItem leaf)
    {
        var parts = new List<string>();
        ILogical? at = leaf;
        while (at is not null)
        {
            if (at is MenuItem step && Name(step) is { Length: > 0 } header)
            {
                parts.Add(header);
            }

            at = at.LogicalParent;
        }

        parts.Reverse();
        return string.Join(Arrow, parts);
    }

    /// <summary>
    /// A header as a person reads it: no access-key markup, and no ellipsis. The ellipsis means
    /// "this one asks a question first", which is the answer's job to say and not the path's.
    ///
    /// <para>Removed wherever it appears rather than only at the end, because it is not always last
    /// — "How do I…?" ends with the question mark, and trimming from the right would have left that
    /// one path reading differently from every other.</para>
    /// </summary>
    private static string Name(MenuItem item) =>
        item.Header is string header
            ? Strip(header).Replace("…", "", StringComparison.Ordinal).Trim()
            : string.Empty;

    /// <summary>
    /// <c>"F_ormat"</c> is <c>"Format"</c>; <c>"__"</c> is one real underscore. Done by hand rather
    /// than with <c>Replace("_", "")</c>, which would eat the escaped one.
    /// </summary>
    private static string Strip(string header)
    {
        var text = new System.Text.StringBuilder(header.Length);
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] != '_')
            {
                text.Append(header[i]);
            }
            else if (i + 1 < header.Length && header[i + 1] == '_')
            {
                text.Append('_');
                i++;
            }
        }

        return text.ToString();
    }
}
