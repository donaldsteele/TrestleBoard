using System.Text;

namespace TrestleBoard.Site;

/// <summary>Escaping, kept in one place so no page has to remember it.</summary>
internal static class Html
{
    /// <summary>
    /// Text into markup. Every catalog title and description passes through here.
    ///
    /// <para>None of them contains a bracket today. That is not a reason to concatenate them raw:
    /// the catalog is edited by whoever adds a command, this generator is edited by whoever changes
    /// the site, and the two will not be the same person on the day somebody writes a description
    /// with an ampersand in it.</para>
    /// </summary>
    internal static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// A value safe to put inside a double-quoted HTML attribute that JavaScript then reads — used
    /// for the reference page's search keys.
    /// </summary>
    internal static string Attribute(string? text) => Escape(text).Replace("'", "&#39;", StringComparison.Ordinal);
}
