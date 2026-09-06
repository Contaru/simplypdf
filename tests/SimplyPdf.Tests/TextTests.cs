using SimplyPdf.Fonts;
using SimplyPdf.Text;

namespace SimplyPdf.Tests;

public class TextTests
{
    private static PdfPage NewPage(out PdfDocument doc, double margin = 72)
    {
        doc = new PdfDocument { CompressStreams = false };
        return doc.AddPage(PdfPageSize.Letter, PdfMargins.All(margin));
    }

    [Fact]
    public void Width_of_string_uses_afm_metrics()
    {
        var page = NewPage(out _).Font(StandardFont.Helvetica, 10);

        // H=722 e=556 l=222 l=222 o=556 -> 2278/1000 * 10
        Assert.Equal(22.78, page.WidthOfString("Hello"), 3);

        page.Font(StandardFont.HelveticaBold);
        Assert.Equal(24.45, page.WidthOfString("Hello"), 3); // H=722 e=556 l=278 l=278 o=611

        page.Font(StandardFont.Courier, 12);
        Assert.Equal(5 * 600 * 12 / 1000.0, page.WidthOfString("Hello"), 3);
    }

    [Fact]
    public void Accented_characters_are_measured_through_winansi()
    {
        var page = NewPage(out _).Font(StandardFont.Helvetica, 10);

        // ñ (0xF1) = 556, é (0xE9) = 556, ® (0xAE) = 737
        Assert.Equal((556 + 556 + 737) / 100.0, page.WidthOfString("ñé®"), 3);
    }

    [Fact]
    public void Line_height_matches_pdfkit_formula()
    {
        var page = NewPage(out _).Font(StandardFont.Helvetica, 10);

        Assert.Equal(9.25, page.CurrentLineHeight(), 3);                  // (718 + 207) / 1000 * 10
        Assert.Equal(11.56, page.CurrentLineHeight(includeGap: true), 3); // + bbox-derived gap of 231
    }

    [Fact]
    public void Text_is_placed_at_baseline_below_the_top_left_corner()
    {
        var page = NewPage(out var doc).Font(StandardFont.Helvetica, 10);
        page.Text("Hi", 100, 200);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // baseline = pageHeight - y - ascender*size/1000 = 792 - 200 - 7.18
        Assert.Contains("1 0 0 1 100 584.82 Tm\n(Hi) Tj", content, StringComparison.Ordinal);
        Assert.Contains("q\n1 0 0 -1 0 792 cm\nBT\n/F1 10 Tf", content, StringComparison.Ordinal);
        Assert.Equal(211.56, page.Y, 3); // cursor advanced by one line
    }

    [Fact]
    public void Right_and_center_alignment_offset_the_line_start()
    {
        var page = NewPage(out var doc).Font(StandardFont.Helvetica, 10);
        page.Text("A", 0, 0, width: 100, TextAlign.Right);   // A = 6.67 wide
        page.Text("A", 0, 50, width: 100, TextAlign.Center);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        Assert.Contains("1 0 0 1 93.33 784.82 Tm", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 46.665 734.82 Tm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_box_runs_from_x_to_the_right_margin_like_pdfkit()
    {
        var page = NewPage(out var doc, margin: 25).Font(StandardFont.Helvetica, 10);
        page.Text("A", 0, 0, new TextOptions { Align = TextAlign.Center });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // box = 612 - 0 - 25 = 587; x = (587 - 6.67) / 2
        Assert.Contains("1 0 0 1 290.165 784.82 Tm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Consecutive_text_calls_flow_downwards()
    {
        var page = NewPage(out var doc).Font(StandardFont.Helvetica, 10);
        page.Text("one", 50, 100).Text("two").Text("three", new TextOptions { LineGap = 4 });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        Assert.Contains("1 0 0 1 50 684.82 Tm\n(one) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 50 673.26 Tm\n(two) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 50 661.7 Tm\n(three) Tj", content, StringComparison.Ordinal);
        Assert.Equal(100 + 3 * 11.56 + 4, page.Y, 3);
    }

    [Fact]
    public void Special_characters_are_escaped_in_literals()
    {
        var page = NewPage(out var doc);
        page.Text("a(b)c\\d ñ €", 0, 0);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        Assert.Contains(@"(a\(b\)c\\d \361 \200) Tj", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_text_wraps_at_spaces_within_the_box()
    {
        var font = StandardFontMetrics.Get(StandardFont.Helvetica);
        var lines = TextLayout.Wrap("the quick brown fox jumps over the lazy dog", 60, font, 10);

        Assert.All(lines, l => Assert.True(font.WidthOf(l.Bytes, 10) <= 60));
        Assert.Equal("the quick", Latin1(lines[0].Bytes));
        Assert.Equal("brown fox", Latin1(lines[1].Bytes));
        Assert.True(lines[^1].EndsParagraph);
        Assert.All(lines.SkipLast(1), l => Assert.False(l.EndsParagraph));
    }

    [Fact]
    public void Word_wider_than_the_box_is_broken_by_character()
    {
        var font = StandardFontMetrics.Get(StandardFont.Helvetica);
        var lines = TextLayout.Wrap("abcdefghijklmnopqrstuvwxyz", 30, font, 10);

        Assert.True(lines.Count > 1);
        Assert.Equal("abcdefghijklmnopqrstuvwxyz", string.Concat(lines.Select(l => Latin1(l.Bytes))));
        Assert.All(lines, l => Assert.True(font.WidthOf(l.Bytes, 10) <= 30));
    }

    [Fact]
    public void Explicit_newlines_always_break_and_empty_text_still_takes_a_line()
    {
        var font = StandardFontMetrics.Get(StandardFont.Helvetica);

        var lines = TextLayout.Wrap("a\r\nb\nc", double.PositiveInfinity, font, 10);
        Assert.Equal(new[] { "a", "b", "c" }, lines.Select(l => Latin1(l.Bytes)));

        Assert.Single(TextLayout.Wrap(string.Empty, 100, font, 10));
    }

    [Fact]
    public void Justified_lines_set_word_spacing_except_on_the_last_line()
    {
        var page = NewPage(out var doc).Font(StandardFont.Helvetica, 10);
        page.Text("aa bb cc dd ee ff gg hh", 0, 0, new TextOptions { Width = 50, Align = TextAlign.Justify });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();
        var tws = content.Split('\n').Where(l => l.EndsWith(" Tw", StringComparison.Ordinal)).ToList();

        Assert.True(tws.Count >= 2);
        Assert.NotEqual("0 Tw", tws[0]);
        Assert.Equal("0 Tw", tws[^1]);
    }

    [Fact]
    public void Height_of_string_counts_wrapped_lines()
    {
        var page = NewPage(out _).Font(StandardFont.Helvetica, 10);
        var single = page.HeightOfString("short", new TextOptions { Width = 200 });
        var wrapped = page.HeightOfString("the quick brown fox jumps over the lazy dog", new TextOptions { Width = 60 });

        Assert.Equal(11.56, single, 3);
        Assert.Equal(4 * 11.56, wrapped, 3); // "the quick" / "brown fox" / "jumps over" / "the lazy dog"
    }

    private static string Latin1(byte[] bytes) => System.Text.Encoding.Latin1.GetString(bytes);
}
