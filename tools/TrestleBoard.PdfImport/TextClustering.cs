using Docnet.Core.Models;

namespace TrestleBoard.PdfImport;

/// <summary>One PDF glyph, geometry only — no font name is available from Docnet.Core's public API,
/// so bold/italic cannot be detected; only a size-based heading/subheading/body heuristic is possible.</summary>
internal readonly record struct CharInfo(char Ch, float Left, float Top, float Right, float Bottom, double FontSize);

internal sealed record RawLine(List<CharInfo> Chars);

internal sealed record Segment(float Left, float Top, float Right, float Bottom, string Text, double AvgFontSize);

internal sealed record ParagraphText(string Text, string Style, int LineCount);

internal sealed record ColumnRegion(float X, float Y, float Width, float Height, List<ParagraphText> Paragraphs);

/// <summary>
/// Reconstructs reading columns/paragraphs from a flat bag of PDF characters. A PDF has no
/// paragraph/column structure, only positioned glyphs, so this is a geometric heuristic:
/// group characters sharing a row into lines, split each line into per-column segments at large
/// horizontal gaps, cluster segments by their left edge into column bands, then within a band
/// group consecutive lines into paragraphs at large vertical gaps.
/// </summary>
internal static class TextClustering
{
    private const float ColumnGutterPt = 24f;
    private const float SegmentGapMultiplier = 2.5f;
    private const float SegmentGapMinPt = 14f;
    private const float SpaceGapFraction = 0.15f;
    private const float ParagraphGapMultiplier = 1.5f;
    private const float ParagraphGapMinPt = 12f;

    public static List<ColumnRegion> BuildColumns(IReadOnlyList<Character> characters)
    {
        List<CharInfo> chars = characters
            .Where(c => !char.IsControl(c.Char))
            .Select(c => new CharInfo(c.Char, c.Box.Left, c.Box.Top, c.Box.Right, c.Box.Bottom, c.FontSize))
            .OrderBy(c => c.Top)
            .ThenBy(c => c.Left)
            .ToList();

        if (chars.Count == 0)
        {
            return [];
        }

        List<RawLine> rawLines = GroupRawLines(chars);
        List<Segment> segments = rawLines.SelectMany(SplitIntoSegments).ToList();
        if (segments.Count == 0)
        {
            return [];
        }

        (double p75, double p90) = FontSizePercentiles(segments);
        List<List<Segment>> buckets = ClusterColumns(segments);

        var result = new List<ColumnRegion>();
        foreach (List<Segment> bucket in buckets.OrderBy(b => b.Min(s => s.Left)))
        {
            List<Segment> ordered = bucket.OrderBy(s => s.Top).ToList();
            List<ParagraphText> paragraphs = SplitParagraphs(ordered, p75, p90);
            if (paragraphs.Count == 0)
            {
                continue;
            }

            float minX = ordered.Min(s => s.Left);
            float maxX = ordered.Max(s => s.Right);
            float minY = ordered.Min(s => s.Top);
            float maxY = ordered.Max(s => s.Bottom);
            result.Add(new ColumnRegion(minX, minY, maxX - minX, maxY - minY, paragraphs));
        }

        return result;
    }

    private static List<RawLine> GroupRawLines(List<CharInfo> sortedChars)
    {
        var lines = new List<RawLine>();
        var current = new List<CharInfo>();
        float currentTop = 0f;
        double currentFontSize = 0;

        foreach (CharInfo c in sortedChars)
        {
            if (current.Count == 0)
            {
                current.Add(c);
                currentTop = c.Top;
                currentFontSize = c.FontSize;
                continue;
            }

            float tolerance = (float)Math.Max(4.0, currentFontSize * 0.4);
            if (Math.Abs(c.Top - currentTop) <= tolerance)
            {
                current.Add(c);
                currentFontSize = (currentFontSize + c.FontSize) / 2.0;
            }
            else
            {
                lines.Add(new RawLine(current.OrderBy(x => x.Left).ToList()));
                current = [c];
                currentTop = c.Top;
                currentFontSize = c.FontSize;
            }
        }

        if (current.Count > 0)
        {
            lines.Add(new RawLine(current.OrderBy(x => x.Left).ToList()));
        }

        return lines;
    }

    private static List<Segment> SplitIntoSegments(RawLine line)
    {
        var segments = new List<Segment>();
        var currentChars = new List<CharInfo>();
        CharInfo? previous = null;

        foreach (CharInfo c in line.Chars)
        {
            if (previous is { } p)
            {
                float gap = c.Left - p.Right;
                float threshold = Math.Max(SegmentGapMinPt, (float)p.FontSize * SegmentGapMultiplier);
                if (gap > threshold)
                {
                    FlushSegment(currentChars, segments);
                    currentChars = [];
                }
            }

            currentChars.Add(c);
            previous = c;
        }

        FlushSegment(currentChars, segments);
        return segments;
    }

    private static void FlushSegment(List<CharInfo> chars, List<Segment> segments)
    {
        if (chars.Count == 0)
        {
            return;
        }

        var text = new System.Text.StringBuilder();
        CharInfo? prev = null;
        double sizeSum = 0;
        foreach (CharInfo c in chars)
        {
            if (prev is { } p)
            {
                float gap = c.Left - p.Right;
                if (gap > SpaceGapFraction * Math.Max(c.FontSize, 1.0) && text.Length > 0 && text[^1] != ' ')
                {
                    text.Append(' ');
                }
            }

            text.Append(c.Ch);
            sizeSum += c.FontSize;
            prev = c;
        }

        string trimmed = text.ToString().Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        float segLeft = chars.Min(c => c.Left);
        float segRight = chars.Max(c => c.Right);
        float segTop = chars.Min(c => c.Top);
        float segBottom = chars.Max(c => c.Bottom);
        segments.Add(new Segment(segLeft, segTop, segRight, segBottom, trimmed, sizeSum / chars.Count));
    }

    /// <summary>Greedy 1D clustering of segments by left edge, sorted ascending — works because a
    /// real newsletter's columns keep a consistent left start per column with a persistent gutter
    /// between them, so runs of nearby lefts naturally separate at the true column boundaries.</summary>
    private static List<List<Segment>> ClusterColumns(List<Segment> segments)
    {
        List<Segment> byLeft = segments.OrderBy(s => s.Left).ToList();
        var buckets = new List<List<Segment>>();
        float lastLeft = float.NegativeInfinity;

        foreach (Segment s in byLeft)
        {
            if (buckets.Count > 0 && s.Left - lastLeft <= ColumnGutterPt)
            {
                buckets[^1].Add(s);
            }
            else
            {
                buckets.Add([s]);
            }

            lastLeft = Math.Max(lastLeft, s.Left);
        }

        return buckets;
    }

    private static List<ParagraphText> SplitParagraphs(List<Segment> orderedLines, double p75, double p90)
    {
        if (orderedLines.Count == 0)
        {
            return [];
        }

        double medianHeight = Median(orderedLines.Select(l => (double)(l.Bottom - l.Top)).ToList());
        float gapThreshold = (float)Math.Max(ParagraphGapMinPt, medianHeight * ParagraphGapMultiplier);

        var paragraphs = new List<ParagraphText>();
        var currentLines = new List<Segment> { orderedLines[0] };

        for (int i = 1; i < orderedLines.Count; i++)
        {
            Segment prev = orderedLines[i - 1];
            Segment next = orderedLines[i];
            float gap = next.Top - prev.Bottom;
            if (gap > gapThreshold)
            {
                paragraphs.Add(BuildParagraph(currentLines, p75, p90));
                currentLines = [];
            }

            currentLines.Add(next);
        }

        if (currentLines.Count > 0)
        {
            paragraphs.Add(BuildParagraph(currentLines, p75, p90));
        }

        return paragraphs;
    }

    private static ParagraphText BuildParagraph(List<Segment> lines, double p75, double p90)
    {
        string text = string.Join(" ", lines.Select(l => l.Text));
        double avgSize = lines.Average(l => l.AvgFontSize);
        string style = avgSize >= p90 && lines.Count == 1
            ? "heading"
            : avgSize >= p75
                ? "subheading"
                : "body";
        return new ParagraphText(text, style, lines.Count);
    }

    private static (double P75, double P90) FontSizePercentiles(List<Segment> segments)
    {
        List<double> sizes = segments.Select(s => s.AvgFontSize).OrderBy(v => v).ToList();
        return (Percentile(sizes, 0.75), Percentile(sizes, 0.90));
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        int index = (int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[index];
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
