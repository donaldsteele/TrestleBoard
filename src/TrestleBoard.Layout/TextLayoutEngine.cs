using SkiaSharp;
using TrestleBoard.Layout.Breaking;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Shaping;

namespace TrestleBoard.Layout;

public sealed record LayoutOptions
{
    /// <summary>Segments narrower than this many average char widths are discarded (PLAN.md §3).</summary>
    public float MinSegmentAvgCharMultiple { get; init; } = 4f;

    public bool EnableStandardLigatures { get; init; } = true;

    public bool EnableKerning { get; init; } = true;

    /// <summary>
    /// Fewest lines of a paragraph allowed to stay behind when it breaks across frames
    /// (docs/M8-spec.md §4). A single line stranded at the foot of a page is the most visible flaw
    /// in the printed examples. 0 restores the pre-M8 behaviour exactly.
    /// </summary>
    public int MinLinesAtBreak { get; init; } = 2;
}

/// <summary>
/// The line-layout engine (PLAN.md §3): shape → break → greedy first-fit against per-band
/// exclusion segments → stacked LineBoxes with frame spill. Deterministic by construction:
/// bundled fonts, explicit shaping properties, float-only arithmetic, fresh per-line metrics.
/// </summary>
public sealed class TextLayoutEngine
{
    private const float Epsilon = 1e-4f;

    /// <summary>
    /// The smallest multiple of natural line height the engine will lay out at. Tighter than any
    /// typographer would set and far tighter than anything this app's own styles use — it is a
    /// backstop against a corrupt file, not a design choice.
    /// </summary>
    private const float MinimumLineSpacing = 0.25f;

    /// <summary>
    /// And an absolute floor in points, for the degenerate case where the natural height is itself
    /// zero — a font with no metrics, or a run at size zero. Without it the multiple above is zero
    /// too, and zero is the value that hangs the band loops.
    /// </summary>
    private const float MinimumLineHeightPt = 0.5f;

    private readonly FontStore _fonts;
    private readonly LayoutOptions _options;
    private readonly Dictionary<(FontKey Key, float SizePt), FontMetrics> _metricsCache = new();

    public TextLayoutEngine(FontStore fonts, LayoutOptions? options = null)
    {
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _options = options ?? new LayoutOptions();
    }

    public LayoutResult Layout(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<ParagraphPlan> plans = request.Story.Paragraphs
            .Select((p, i) => PlanParagraph(request.Story.StoryId, i, p))
            .ToList();

        var frameLayouts = new List<FrameLayout>(request.Frames.Count);
        int paraIdx = 0;
        int wordIdx = 0;
        bool[] spaceBeforeConsumed = new bool[plans.Count];

        for (int frameIdx = 0; frameIdx < request.Frames.Count; frameIdx++)
        {
            LayoutFrame frame = request.Frames[frameIdx];
            var lines = new List<LineBox>();
            float y = frame.Rect.Top;
            bool frameFull = false;

            while (!frameFull && paraIdx < plans.Count)
            {
                ParagraphPlan para = plans[paraIdx];
                bool paraStart = wordIdx == 0;

                if (paraStart && !spaceBeforeConsumed[paraIdx])
                {
                    y += para.Style.SpaceBeforePt;
                    spaceBeforeConsumed[paraIdx] = true;
                }

                if (para.Words.Count == 0)
                {
                    // Empty paragraph: one line slot with an EMPTY LineSegment so the caret has
                    // geometry and a hit-test target (docs/M4-spec.md §1.1). Band/segment math is
                    // the same as a normal line, so an empty paragraph beside a photo still
                    // reports the narrowed interval.
                    float emptyMinWidth = _options.MinSegmentAvgCharMultiple
                        * GetMetrics(ToKey(para.Style.DefaultRun), para.Style.DefaultRun.SizePt).AverageCharWidthPt;
                    var emptySegments = new List<FloatInterval>();
                    while (true)
                    {
                        if (y + para.LineHeight > frame.Rect.Bottom + Epsilon)
                        {
                            frameFull = true;
                            break;
                        }

                        emptySegments = ComputeSegments(frame, y, y + para.LineHeight, emptyMinWidth);
                        if (emptySegments.Count > 0)
                        {
                            break;
                        }

                        y += para.LineHeight;
                    }

                    if (frameFull)
                    {
                        break;
                    }

                    lines.Add(new LineBox(
                        paraIdx,
                        isParagraphStart: true,
                        baselineY: y + para.MaxAscentPt,
                        bandTop: y,
                        bandBottom: y + para.LineHeight,
                        lineHeight: para.LineHeight,
                        maxAscentPt: para.MaxAscentPt,
                        maxDescentPt: para.MaxDescentPt,
                        segments: [new LineSegment(emptySegments[0], para.Style.Align, [])],
                        source: new SourceSpan(para.StoryId, paraIdx, 0, 0)));
                    y += para.LineHeight + para.Style.SpaceAfterPt;
                    paraIdx++;
                    wordIdx = 0;
                    continue;
                }

                // Find a band with usable segments, advancing past fully blocked bands.
                float minSegWidth = _options.MinSegmentAvgCharMultiple
                    * GetMetrics(para.Words[wordIdx].PrimaryFontKey, para.Words[wordIdx].PrimarySizePt).AverageCharWidthPt;
                var segments = new List<FloatInterval>();
                while (true)
                {
                    if (y + para.LineHeight > frame.Rect.Bottom + Epsilon)
                    {
                        frameFull = true;
                        break;
                    }

                    segments = ComputeSegments(frame, y, y + para.LineHeight, minSegWidth);
                    if (segments.Count > 0)
                    {
                        break;
                    }

                    y = y + para.LineHeight;
                }

                if (frameFull)
                {
                    break;
                }

                LineBox line = FillLine(para, paraIdx, paraStart, segments, y, ref wordIdx);
                lines.Add(line);
                y = line.BandBottom;

                if (wordIdx >= para.Words.Count)
                {
                    y += para.Style.SpaceAfterPt;
                    paraIdx++;
                    wordIdx = 0;
                }
            }

            // Orphan control runs after the frame is full and never on the last frame in the chain,
            // where there is nowhere to push text to and doing so would only manufacture overset.
            if (frameIdx < request.Frames.Count - 1)
            {
                PushOrphansForward(
                    lines, plans, spaceBeforeConsumed, ref paraIdx, ref wordIdx, _options.MinLinesAtBreak);
            }

            frameLayouts.Add(new FrameLayout(frameIdx, frame.Rect, lines));
        }

        OversetInfo? overflow = null;
        if (paraIdx < plans.Count)
        {
            int stopChar = wordIdx < plans[paraIdx].Words.Count ? plans[paraIdx].Words[wordIdx].StartChar : 0;
            overflow = new OversetInfo(request.Frames.Count - 1, paraIdx, stopChar);
        }

        return new LayoutResult(frameLayouts, overflow);
    }

    /// <summary>
    /// Moves a stranded tail of a paragraph to the next frame (docs/M8-spec.md §4). Only the
    /// "behind" side is enforced: the "forward" count is not known until the next frame is laid out,
    /// and fixing it after the fact would need a second pass over frames already emitted.
    /// </summary>
    private static void PushOrphansForward(
        List<LineBox> lines,
        List<ParagraphPlan> plans,
        bool[] spaceBeforeConsumed,
        ref int paraIdx,
        ref int wordIdx,
        int minLines)
    {
        // wordIdx > 0 means the frame stopped part-way through paragraph paraIdx.
        if (minLines <= 1 || wordIdx <= 0 || lines.Count == 0 || paraIdx >= plans.Count)
        {
            return;
        }

        int stranded = 0;
        for (int i = lines.Count - 1; i >= 0 && lines[i].ParagraphIndex == paraIdx; i--)
        {
            stranded++;
        }

        // Nothing to fix, or fixing it would empty the frame and simply move the problem along.
        if (stranded == 0 || stranded >= minLines || stranded >= lines.Count)
        {
            return;
        }

        // Rewind to the first word of the first stranded line and drop those lines.
        int startChar = lines[^stranded].Source.StartChar;
        List<WordCluster> words = plans[paraIdx].Words;
        int rewound = words.FindIndex(w => w.StartChar == startChar);
        if (rewound < 0)
        {
            return;
        }

        lines.RemoveRange(lines.Count - stranded, stranded);
        wordIdx = rewound;

        // Pushing the paragraph's FIRST line forward un-starts the paragraph, so its space-before is
        // owed again — otherwise it lands flush against the top of the next frame, unlike every
        // other paragraph that begins one.
        if (rewound == 0)
        {
            spaceBeforeConsumed[paraIdx] = false;
        }
    }

    // ---- Paragraph preparation -------------------------------------------------------------

    private ParagraphPlan PlanParagraph(string storyId, int paragraphIndex, LayoutParagraph paragraph)
    {
        string text = string.Concat(paragraph.Runs.Select(r => r.Text));
        var glyphs = new List<(ShapedGlyph Glyph, ShapedRun Run)>();
        var options = new ShapeOptions(_options.EnableStandardLigatures, _options.EnableKerning);

        int offset = 0;
        float maxAscent = 0f;
        float maxDescent = 0f;
        float maxLeading = 0f;
        foreach (LayoutRun run in paragraph.Runs)
        {
            ResolvedFont font = ResolveFont(run.Style);
            FontMetrics m = GetMetrics(ToKey(run.Style), run.Style.SizePt);
            maxAscent = MathF.Max(maxAscent, m.AscentPt);
            maxDescent = MathF.Max(maxDescent, m.DescentPt);
            maxLeading = MathF.Max(maxLeading, m.LeadingPt);

            if (run.Text.Length > 0)
            {
                ShapedRun shaped = HarfBuzzShaper.Shape(
                    font, run.Style.SizePt, run.Style.ColorArgb, text, offset, run.Text.Length, options);
                foreach (ShapedGlyph g in shaped.Glyphs)
                {
                    glyphs.Add((g, shaped));
                }
            }

            offset += run.Text.Length;
        }

        if (paragraph.Runs.Count == 0 || (maxAscent == 0f && maxDescent == 0f))
        {
            FontMetrics m = GetMetrics(ToKey(paragraph.Style.DefaultRun), paragraph.Style.DefaultRun.SizePt);
            maxAscent = m.AscentPt;
            maxDescent = m.DescentPt;
            maxLeading = m.LeadingPt;
        }

        // The line height has a FLOOR, and it is what stops the engine hanging.
        //
        // Two loops in LayOut advance by `para.LineHeight` while looking for a band with usable
        // segments, and both exit on `y + LineHeight > frame.Rect.Bottom`. At zero that test can
        // never become true and `y` never moves, so the app locks up inside its first paint with no
        // error and nothing to see. Negative is worse: `y` walks upward for ever.
        //
        // `LineSpacing` is a plain settable float on ParagraphStyleDef with no validation anywhere,
        // and it is deserialised straight out of the file — so a hand-edited or corrupted `.tboard`
        // carrying `"lineSpacing": 0` was enough (review §14.2). Clamping here rather than at the
        // model means every path into layout is covered by one line, including documents already
        // saved with a bad value.
        float naturalHeight = maxAscent + maxDescent + maxLeading;
        float lineHeight = Math.Max(
            paragraph.Style.LineSpacing * naturalHeight,
            Math.Max(naturalHeight * MinimumLineSpacing, MinimumLineHeightPt));
        List<WordCluster> words = BuildWords(text, glyphs);
        return new ParagraphPlan(storyId, paragraphIndex, paragraph.Style, text, glyphs, words,
            lineHeight, maxAscent, maxDescent);
    }

    private static List<WordCluster> BuildWords(string text, List<(ShapedGlyph Glyph, ShapedRun Run)> glyphs)
    {
        var words = new List<WordCluster>();
        if (glyphs.Count == 0)
        {
            return words;
        }

        // Cluster starts present in the shaped output (paragraph-relative char indices).
        var clusterStarts = new SortedSet<int>(glyphs.Select(g => g.Glyph.Cluster));

        IReadOnlyList<BreakOpportunity> opportunities = LineBreakAnalyzer.Analyze(text);
        var boundaries = new List<(int Index, BreakKind Kind)>();
        foreach (BreakOpportunity op in opportunities)
        {
            // Snap forward to the next cluster boundary (never break inside a ligature).
            int snapped = op.TextIndex;
            if (!clusterStarts.Contains(snapped))
            {
                snapped = clusterStarts.FirstOrDefault(c => c >= op.TextIndex, text.Length);
            }

            if (snapped >= text.Length)
            {
                continue;
            }

            if (boundaries.Count > 0 && boundaries[^1].Index == snapped)
            {
                if (op.Kind == BreakKind.Mandatory)
                {
                    boundaries[^1] = (snapped, BreakKind.Mandatory);
                }

                continue;
            }

            boundaries.Add((snapped, op.Kind));
        }

        int wordStart = 0;
        int glyphCursor = 0;
        foreach ((int index, BreakKind kind) in boundaries.Append((text.Length, BreakKind.Allowed)))
        {
            if (index <= wordStart)
            {
                continue;
            }

            var renderGlyphs = new List<int>();
            float totalAdvance = 0f;
            while (glyphCursor < glyphs.Count && glyphs[glyphCursor].Glyph.Cluster < index)
            {
                char sourceChar = text[glyphs[glyphCursor].Glyph.Cluster];
                if (!LineBreakAnalyzer.IsMandatoryBreak(sourceChar))
                {
                    renderGlyphs.Add(glyphCursor);
                    totalAdvance += glyphs[glyphCursor].Glyph.XAdvancePt;
                }

                glyphCursor++;
            }

            // Trailing whitespace hangs: excluded from the fit-width test.
            float trailingWs = 0f;
            for (int i = renderGlyphs.Count - 1; i >= 0; i--)
            {
                char sourceChar = text[glyphs[renderGlyphs[i]].Glyph.Cluster];
                if (LineBreakAnalyzer.IsBreakableSpace(sourceChar))
                {
                    trailingWs += glyphs[renderGlyphs[i]].Glyph.XAdvancePt;
                }
                else
                {
                    break;
                }
            }

            ShapedRun primary = renderGlyphs.Count > 0 ? glyphs[renderGlyphs[0]].Run : glyphs[0].Run;
            words.Add(new WordCluster(
                wordStart,
                index,
                kind == BreakKind.Mandatory,
                renderGlyphs,
                totalAdvance,
                totalAdvance - trailingWs,
                primary.Font.Key,
                primary.SizePt));
            wordStart = index;
        }

        return words;
    }

    // ---- Band segments (PLAN.md §3 exclusion → segment algorithm) --------------------------

    private static List<FloatInterval> ComputeSegments(LayoutFrame frame, float bandTop, float bandBottom, float minWidth)
    {
        var intervals = new List<FloatInterval> { new(frame.Rect.Left, frame.Rect.Right) };
        foreach (ExclusionRect exclusion in frame.Exclusions)
        {
            FrameRect r = exclusion.Rect;
            float left = r.Left - exclusion.WrapMargin;
            float top = r.Top - exclusion.WrapMargin;
            float right = r.Right + exclusion.WrapMargin;
            float bottom = r.Bottom + exclusion.WrapMargin;
            if (top >= bandBottom - Epsilon || bottom <= bandTop + Epsilon)
            {
                continue;
            }

            var next = new List<FloatInterval>();
            foreach (FloatInterval interval in intervals)
            {
                if (right <= interval.Left + Epsilon || left >= interval.Right - Epsilon)
                {
                    next.Add(interval);
                    continue;
                }

                if (left > interval.Left + Epsilon)
                {
                    next.Add(new FloatInterval(interval.Left, left));
                }

                if (right < interval.Right - Epsilon)
                {
                    next.Add(new FloatInterval(right, interval.Right));
                }
            }

            intervals = next;
        }

        intervals.RemoveAll(s => s.Width < minWidth);
        return intervals;
    }

    // ---- Line filling ----------------------------------------------------------------------

    private static LineBox FillLine(
        ParagraphPlan para,
        int paragraphIndex,
        bool isParagraphStart,
        List<FloatInterval> segments,
        float bandTop,
        ref int wordIdx)
    {
        float bandBottom = bandTop + para.LineHeight;
        float baselineY = bandTop + para.MaxAscentPt;
        int firstWord = wordIdx;
        var lineSegments = new List<LineSegment>();

        for (int segIdx = 0; segIdx < segments.Count && wordIdx < para.Words.Count; segIdx++)
        {
            FloatInterval segment = segments[segIdx];
            float contentLeft = segment.Left;
            if (isParagraphStart && segIdx == 0)
            {
                contentLeft += para.Style.FirstLineIndentPt;
            }

            var placed = new List<(WordCluster Word, float X)>();
            float xCursor = contentLeft;
            bool mandatoryStop = false;
            while (wordIdx < para.Words.Count)
            {
                WordCluster word = para.Words[wordIdx];
                if (xCursor + word.FitAdvancePt <= segment.Right + Epsilon)
                {
                    placed.Add((word, xCursor));
                    xCursor += word.TotalAdvancePt;
                    wordIdx++;
                    if (word.EndsMandatory)
                    {
                        mandatoryStop = true;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            if (placed.Count > 0)
            {
                lineSegments.Add(BuildSegment(para, segment, contentLeft, placed, baselineY, paragraphIndex));
            }

            if (mandatoryStop)
            {
                break;
            }
        }

        if (lineSegments.Count == 0 && wordIdx < para.Words.Count)
        {
            // Nothing fit anywhere: force-place the next word in the first segment (it overflows
            // the right edge) — documents the no-hyphenation behavior for over-wide tokens.
            FloatInterval segment = segments[0];
            float contentLeft = segment.Left + (isParagraphStart ? para.Style.FirstLineIndentPt : 0f);
            WordCluster word = para.Words[wordIdx];
            wordIdx++;
            lineSegments.Add(BuildSegment(
                para, segment, contentLeft, [(word, contentLeft)], baselineY, paragraphIndex));
        }

        int lastWord = wordIdx - 1;
        var source = new SourceSpan(
            para.StoryId,
            paragraphIndex,
            para.Words[firstWord].StartChar,
            para.Words[lastWord].EndChar);
        return new LineBox(
            paragraphIndex,
            isParagraphStart,
            baselineY,
            bandTop,
            bandBottom,
            para.LineHeight,
            para.MaxAscentPt,
            para.MaxDescentPt,
            lineSegments,
            source);
    }

    private static LineSegment BuildSegment(
        ParagraphPlan para,
        FloatInterval segment,
        float contentLeft,
        List<(WordCluster Word, float X)> placed,
        float baselineY,
        int paragraphIndex)
    {
        // Alignment shift: content width excludes the last placed word's trailing whitespace.
        (WordCluster lastWord, float lastX) = placed[^1];
        float contentWidth = lastX + lastWord.FitAdvancePt - contentLeft;
        float shift = para.Style.Align switch
        {
            TextAlign.Right => segment.Right - contentLeft - contentWidth,
            TextAlign.Center => (segment.Right - contentLeft - contentWidth) / 2f,
            _ => 0f,
        };

        var runs = new List<PositionedGlyphRun>();
        ShapedRun? currentRun = null;
        var glyphIds = new List<ushort>();
        var offsets = new List<SKPoint>();
        var clusters = new List<int>();
        var penXs = new List<float>();
        float originX = 0f;
        float advance = 0f;

        void Flush()
        {
            if (currentRun is null || glyphIds.Count == 0)
            {
                return;
            }

            runs.Add(new PositionedGlyphRun(
                currentRun.Font,
                currentRun.SizePt,
                currentRun.ColorArgb,
                originX,
                baselineY,
                glyphIds.ToArray(),
                offsets.ToArray(),
                clusters.ToArray(),
                penXs.ToArray(),
                new SourceSpan(para.StoryId, paragraphIndex, clusters[0], clusters[^1] + 1),
                advance));
            glyphIds = [];
            offsets = [];
            clusters = [];
            penXs = [];
            advance = 0f;
        }

        foreach ((WordCluster word, float wordX) in placed)
        {
            float x = wordX + shift;
            foreach (int glyphIndex in word.RenderGlyphIndices)
            {
                (ShapedGlyph glyph, ShapedRun run) = para.Glyphs[glyphIndex];
                if (!ReferenceEquals(run, currentRun))
                {
                    Flush();
                    currentRun = run;
                    originX = x;
                }

                glyphIds.Add(glyph.GlyphId);
                offsets.Add(new SKPoint(x - originX + glyph.XOffsetPt, -glyph.YOffsetPt));
                clusters.Add(glyph.Cluster);
                penXs.Add(x - originX);
                advance += glyph.XAdvancePt;
                x += glyph.XAdvancePt;
            }
        }

        Flush();
        return new LineSegment(segment, para.Style.Align, runs);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static FontKey ToKey(CharacterStyle style) =>
        new(style.FontFamily, style.Weight, style.Slant);

    private ResolvedFont ResolveFont(CharacterStyle style) => _fonts.Resolve(ToKey(style));

    private FontMetrics GetMetrics(FontKey key, float sizePt)
    {
        (FontKey, float) cacheKey = (key, sizePt);
        if (_metricsCache.TryGetValue(cacheKey, out FontMetrics cached))
        {
            return cached;
        }

        FontMetrics metrics = _fonts.Resolve(key).GetMetrics(sizePt);
        _metricsCache[cacheKey] = metrics;
        return metrics;
    }

    private sealed record ParagraphPlan(
        string StoryId,
        int ParagraphIndex,
        ParagraphStyle Style,
        string Text,
        List<(ShapedGlyph Glyph, ShapedRun Run)> Glyphs,
        List<WordCluster> Words,
        float LineHeight,
        float MaxAscentPt,
        float MaxDescentPt);

    private sealed record WordCluster(
        int StartChar,
        int EndChar,
        bool EndsMandatory,
        List<int> RenderGlyphIndices,
        float TotalAdvancePt,
        float FitAdvancePt,
        FontKey PrimaryFontKey,
        float PrimarySizePt);
}
