using TrestleBoard.Layout.Breaking;
using Xunit;

namespace TrestleBoard.Layout.Tests;

public sealed class LineBreakAnalyzerTests
{
    // Invisible characters are built from code points so the source stays readable.
    private static readonly string Nbsp = ((char)0x00A0).ToString();
    private static readonly string SoftHyphen = ((char)0x00AD).ToString();
    private static readonly string NonBreakingHyphen = ((char)0x2011).ToString();

    [Fact]
    public void BreaksAfterSpace()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("alpha beta");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(6, op.TextIndex);
        Assert.Equal(BreakKind.Allowed, op.Kind);
    }

    [Fact]
    public void MaximalWhitespaceRunYieldsOneBreak()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("alpha   beta");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(8, op.TextIndex);
    }

    [Fact]
    public void TrailingWhitespaceProducesNoBreak()
    {
        Assert.Empty(LineBreakAnalyzer.Analyze("alpha  "));
    }

    [Fact]
    public void NoBreakStrings()
    {
        Assert.Empty(LineBreakAnalyzer.Analyze("alpha" + Nbsp + "beta"));
        Assert.Empty(LineBreakAnalyzer.Analyze("alpha" + SoftHyphen + "beta"));
        Assert.Empty(LineBreakAnalyzer.Analyze("alpha" + NonBreakingHyphen + "beta"));
    }

    [Fact]
    public void BreaksAfterHyphenKeepingItOnUpperLine()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("well-known");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(5, op.TextIndex);
        Assert.True(op.KeepHyphenBefore);
    }

    [Fact]
    public void HyphenBeforeSpaceDefersToSpaceRule()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("mid- word");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(5, op.TextIndex);
        Assert.False(op.KeepHyphenBefore);
    }

    [Fact]
    public void NewlineIsMandatory()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("one\ntwo");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(4, op.TextIndex);
        Assert.Equal(BreakKind.Mandatory, op.Kind);
    }

    [Fact]
    public void CrLfIsOneMandatoryBreak()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("one\r\ntwo");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(5, op.TextIndex);
        Assert.Equal(BreakKind.Mandatory, op.Kind);
    }

    [Fact]
    public void BreaksAfterClosingParenBeforeLetter()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("(a)b");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(3, op.TextIndex);
    }

    [Fact]
    public void ClosingParenBeforeSpaceDefersToSpaceRule()
    {
        IReadOnlyList<BreakOpportunity> ops = LineBreakAnalyzer.Analyze("(a) b");
        BreakOpportunity op = Assert.Single(ops);
        Assert.Equal(4, op.TextIndex);
    }

    [Fact]
    public void NoBreakBeforeOpeningPunctuation()
    {
        // ')' followed by '(' must not produce the narrow-rule break.
        Assert.Empty(LineBreakAnalyzer.Analyze("(a)(b"));
    }
}
