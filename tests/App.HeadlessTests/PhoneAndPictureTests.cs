using Avalonia.Headless;
using Avalonia.LogicalTree;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Settings;
using TrestleBoard.Imaging;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M80: the phone photo opens, and the picture you used last month is one press away.
/// </summary>
public sealed class PhoneAndPictureTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    // ---- Knowing what a file is ----------------------------------------------------------------

    /// <summary>
    /// The format is read from the BYTES, never the file name. A photograph out of a phone-sync
    /// folder is a HEIC whatever it is called, and one renamed to .jpg by a well-meaning relative
    /// is still a HEIC.
    /// </summary>
    [Fact]
    public void AnIPhonePhotographIsRecognisedByItsOwnBytes()
    {
        Assert.Equal(PictureFormatKind.Heic, PictureFormat.Sniff(Heic("heic")));

        // A Live Photo writes mif1 as its major brand.
        Assert.Equal(PictureFormatKind.Heic, PictureFormat.Sniff(Heic("mif1", "heic", "hevc")));

        // And Apple's multi-image containers put a brand of their own in front, with heic only
        // among the compatible brands further along. This case is the reason the COMPATIBLE brands
        // are scanned and not just the major one — the first version of this test used mif1, which
        // is itself a listed brand, so it passed against a sniffer that read only the first four
        // bytes and proved nothing about the loop.
        Assert.Equal(PictureFormatKind.Heic, PictureFormat.Sniff(Heic("MiPr", "miaf", "heic")));
    }

    /// <summary>The ordinary formats keep working, which is what stops this being a regression.</summary>
    [Fact]
    public void TheOrdinaryFormatsAreStillWhatTheyWere()
    {
        Assert.Equal(PictureFormatKind.Jpeg, PictureFormat.Sniff([0xFF, 0xD8, 0xFF, 0xE0, .. new byte[16]]));
        Assert.Equal(
            PictureFormatKind.Png,
            PictureFormat.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[16]]));
        Assert.Equal(PictureFormatKind.Unknown, PictureFormat.Sniff([1, 2, 3]));
    }

    /// <summary>
    /// An ISO base-media file that is NOT an image — an MP4, say — must not be called an iPhone
    /// photograph, or the user is told to change a camera setting about a video.
    /// </summary>
    [Fact]
    public void AVideoIsNotCalledAPhotograph() =>
        Assert.Equal(PictureFormatKind.Unknown, PictureFormat.Sniff(Heic("isom", "mp42")));

    // ---- What the user is told -----------------------------------------------------------------

    /// <summary>
    /// The two answers, both testable apart from the machine — which is the point of the seam,
    /// because neither is reproducible on CI.
    /// </summary>
    [Fact]
    public void AComputerThatCanConvertDoesAndOneThatCannotSaysWhatToDo()
    {
        HeicResult worked = HeicConversion.Handle(new FakeConverter(canConvert: true, [1, 2, 3]), [0]);
        Assert.True(worked.Worked);
        Assert.Equal(HeicConversion.Converted, worked.WhatToTell);

        HeicResult cannot = HeicConversion.Handle(new FakeConverter(canConvert: false, null), [0]);
        Assert.False(cannot.Worked);
        Assert.Equal(HeicConversion.CannotConvert, cannot.WhatToTell);

        HeicResult tried = HeicConversion.Handle(new FakeConverter(canConvert: true, null), [0]);
        Assert.False(tried.Worked);
        Assert.Equal(HeicConversion.ConversionFailed, tried.WhatToTell);
    }

    /// <summary>
    /// <b>Every sentence names what the user can do next.</b> The card this replaces said
    /// "TrestleBoard could not read that file as a picture. JPEG and PNG files work best" — true,
    /// useless, and it leaves a committee member believing their photograph is broken.
    /// </summary>
    [Theory]
    [InlineData(nameof(HeicConversion.CannotConvert))]
    [InlineData(nameof(HeicConversion.ConversionFailed))]
    public void EveryRefusalSaysWhatToDoInstead(string which)
    {
        string sentence = which == nameof(HeicConversion.CannotConvert)
            ? HeicConversion.CannotConvert
            : HeicConversion.ConversionFailed;

        Assert.Contains("iPhone", sentence, StringComparison.Ordinal);
        Assert.Contains("email it to yourself", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HEIC", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codec", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("format is not supported", sentence, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shell's half: a HEIC dropped on the page is converted and the converted bytes are what
    /// reach the newsletter — never the original, or the package opens on the machine that
    /// converted it and fails on the secretary's.
    /// </summary>
    [Fact]
    public async Task TheConvertedPictureIsWhatGoesIntoTheNewsletter()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();
                window.HeicConverter = new FakeConverter(canConvert: true, PngBytes());

                byte[]? readable = await window.MakeReadableForTest(Heic("heic"));

                Assert.NotNull(readable);
                Assert.Equal(PictureFormatKind.Png, PictureFormat.Sniff(readable));
                Assert.Equal(HeicConversion.Converted, window.LastHeicMessageForTest);
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>A computer that cannot convert refuses, explains, and puts nothing on the page.</summary>
    [Fact]
    public async Task WhereItCannotBeDoneNothingGoesOnThePageAndTheReasonIsPlain()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();
                window.HeicConverter = new FakeConverter(canConvert: false, null);

                Assert.Null(await window.MakeReadableForTest(Heic("heic")));
                Assert.Equal(HeicConversion.CannotConvert, window.LastHeicMessageForTest);
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>An ordinary picture is handed straight back, untouched.</summary>
    [Fact]
    public async Task AnOrdinaryPictureIsNotTouched()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();
                byte[] png = PngBytes();

                Assert.Equal(png, await window.MakeReadableForTest(png));
                Assert.Null(window.LastHeicMessageForTest);
            },
            TestContext.Current.CancellationToken);
    }

    // ---- The pictures used before ---------------------------------------------------------------

    /// <summary>Newest first, each path once, and never more than the cap.</summary>
    [Fact]
    public void TheListPutsTheNewestFirstAndKeepsEachPictureOnce()
    {
        AppSettings settings = new AppSettings()
            .WithPictureUsed("a.jpg")
            .WithPictureUsed("b.jpg")
            .WithPictureUsed("a.jpg");

        Assert.Equal(["a.jpg", "b.jpg"], settings.RecentPictures);
    }

    /// <summary>The list never grows without bound.</summary>
    [Fact]
    public void TheListIsCapped()
    {
        var settings = new AppSettings();
        for (int i = 0; i < AppSettings.RecentPicturesKept + 5; i++)
        {
            settings = settings.WithPictureUsed($"photo-{i}.jpg");
        }

        Assert.Equal(AppSettings.RecentPicturesKept, settings.RecentPictures.Count);
        Assert.Equal($"photo-{AppSettings.RecentPicturesKept + 4}.jpg", settings.RecentPictures[0]);
    }

    /// <summary>It survives being written and read back, which is what makes it useful next month.</summary>
    [Fact]
    public void TheListSurvivesRestartingTheApp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tb-m80-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(new AppSettings().WithPictureUsed("lodge-front.jpg").Save(path));

            Assert.Equal(["lodge-front.jpg"], AppSettings.Load(path).RecentPictures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The tile shows the file's own NAME, never the whole path — which on this audience's machines
    /// usually contains their own name (PLAN.md §0).
    /// </summary>
    [Fact]
    public async Task ATileIsLabelledByFileNameAndNotByPath()
    {
        await Session.Dispatch(
            () =>
            {
                string path = Path.Combine("C", "Users", "Margaret", "Pictures", "lodge-front.jpg");
                var dialog = new Dialogs.RecentPicturesDialog([path]);

                string labels = string.Join(
                    " ",
                    dialog.GetLogicalDescendants()
                        .OfType<Avalonia.Controls.TextBlock>()
                        .Select(t => t.Text));

                Assert.Contains("lodge-front.jpg", labels, StringComparison.Ordinal);
                Assert.DoesNotContain("Margaret", labels, StringComparison.Ordinal);
            },
            TestContext.Current.CancellationToken);
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>An ISO base-media header with the given brands, which is all the sniffer looks at.</summary>
    private static byte[] Heic(params string[] brands)
    {
        var bytes = new List<byte>();
        int length = 8 + (brands.Length * 4);
        bytes.AddRange([(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]);
        bytes.AddRange("ftyp"u8);
        foreach (string brand in brands)
        {
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(brand));
        }

        // Padding, so the sniffer's twelve-byte minimum is met even for one brand.
        bytes.AddRange(new byte[24]);
        return [.. bytes];
    }

    private static byte[] PngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[16]];

    private sealed class FakeConverter(bool canConvert, byte[]? result) : IHeicConverter
    {
        public bool CanConvert { get; } = canConvert;

        public byte[]? ToJpeg(byte[] heic) => result;
    }
}
