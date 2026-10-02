using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public class ReleaseNotesTests
{
    [Fact]
    public void Summarize_SdkNotes_TakesBulletsAndSkipsAnnouncement()
    {
        const string notes = "NVIDIA DLSS SDK 310.9.1 is now available for all developers:\r\n" +
                             "  - Added DLSS Ray Reconstruction Transformer Mode (Preset F)\r\n" +
                             "  - Bug Fixes & Stability Improvements";

        var s = ReleaseNotes.Summarize(notes, "v310.9.1");

        Assert.Null(s.Headline);
        Assert.Equal(["Added DLSS Ray Reconstruction Transformer Mode (Preset F)", "Bug Fixes & Stability Improvements"], s.Points);
        Assert.False(s.More);
    }

    [Fact]
    public void Summarize_HeadingWithVersion_StripsVersionAndUsesFirstParagraph()
    {
        const string notes = """
            # v0.8.91 prerelease - compressed model input preview

            Adds **Debug view → Compressed model input** and a `Preview` checkbox directly below **Peripheral compression**. Both control the same saved setting.
            Second line of the same paragraph.

            Retains v0.8.9's spatial compression.
            """;

        var s = ReleaseNotes.Summarize(notes, "v0.8.91");

        Assert.Equal("compressed model input preview", s.Headline);
        var text = Assert.Single(s.Points);
        Assert.StartsWith("Adds Debug view → Compressed model input and a Preview checkbox", text);
        Assert.Contains("Second line", text);
        Assert.DoesNotContain("Retains", text);
    }

    [Fact]
    public void Summarize_SkipsCalloutsAndNestedBullets_LimitsPoints()
    {
        const string notes = """
            > [!IMPORTANT]
            > Restart the game after updating.

            ## Adaptive Quality V3.4 — Temporal Inpaint Stability

            - Added **V2 Compatibility** and [Temporal V3](https://example.com) modes.
              - nested detail
            - Second
            * Third
            1. Fourth
            - Fifth

            ```
            - not a point
            ```
            """;

        var s = ReleaseNotes.Summarize(notes, "1.4.0");

        Assert.Equal("Adaptive Quality V3.4 — Temporal Inpaint Stability", s.Headline);
        Assert.Equal(["Added V2 Compatibility and Temporal V3 modes.", "Second", "Third", "Fourth"], s.Points);
        Assert.True(s.More);
    }

    [Fact]
    public void Summarize_KeepsSnakeCaseAndShortensLongPoints()
    {
        var notes = "- Replaces nvngx_dlss_dll files\n- " + string.Join(" ", Enumerable.Repeat("word", 60));

        var s = ReleaseNotes.Summarize(notes, "1.0");

        Assert.Equal("Replaces nvngx_dlss_dll files", s.Points[0]);
        Assert.EndsWith("…", s.Points[1]);
        Assert.True(s.Points[1].Length <= 161);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NVIDIA Streamline SDK 2.12.0 is now available for all developers.")]
    public void Summarize_NothingToSay_IsEmpty(string? notes) =>
        Assert.True(ReleaseNotes.Summarize(notes, "v2.12.0").IsEmpty);

    [Fact]
    public void IsUnseen_FirstSightingIsRecordedNotFlagged()
    {
        var seen = new Dictionary<string, string>();

        Assert.False(ReleaseNotes.IsUnseen(seen, Component.Dlss, "v310.7.0"));
        Assert.Equal("v310.7.0", seen["Dlss"]);
        Assert.False(ReleaseNotes.IsUnseen(seen, Component.Dlss, "V310.7.0"));
        Assert.True(ReleaseNotes.IsUnseen(seen, Component.Dlss, "v310.9.1"));
        // Turning prereleases off can fall back to an older release; that isn't news.
        Assert.False(ReleaseNotes.IsUnseen(seen, Component.Dlss, "v310.5.0"));
        // Tags without a version still count as new when they change.
        Assert.False(ReleaseNotes.IsUnseen(seen, Component.MfgUnlock, "nightly-a"));
        Assert.True(ReleaseNotes.IsUnseen(seen, Component.MfgUnlock, "nightly-b"));

        ReleaseNotes.MarkSeen(seen, Component.Dlss, "v310.9.1");
        Assert.False(ReleaseNotes.IsUnseen(seen, Component.Dlss, "v310.9.1"));
    }

    [Fact]
    public void Url_PointsAtTheReleasePage()
    {
        Assert.Equal("https://github.com/NVIDIA/DLSS/releases/tag/v310.9.1", ReleaseNotes.Url(Component.Dlss, "v310.9.1"));
        Assert.Equal("https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/1.4.0", ReleaseNotes.Url(Component.MfgUnlock, "1.4.0"));
        Assert.Equal("https://reshade.me/releases", ReleaseNotes.Url(Component.ReShade, "6.8.0"));
    }
}
