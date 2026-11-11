using System.Globalization;
using System.Text.RegularExpressions;
using SimplyPdf.Text;

namespace SimplyPdf.Tests;

public partial class TextRunTests
{
    private static readonly PdfFont Regular = PdfFont.Helvetica;
    private static readonly PdfFont Bold = PdfFont.HelveticaBold;

    private static PdfPage NewPage(out PdfDocument doc)
    {
        doc = new PdfDocument { CompressStreams = false };
        return doc.AddPage(PdfPageSize.Letter, PdfMargins.All(0)).Font(Regular, 10);
    }

    [Fact]
    public void Runs_follow_one_another_on_the_line_each_in_its_own_font()
    {
        var page = NewPage(out var doc);
        page.Text([new TextRun("A", Bold), "A"], 100, 200);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // Bold A = 7.22 wide, so the regular A starts at 107.22; baseline = 792 - 200 - 7.18.
        Assert.Contains("BT\n/F1 10 Tf\n1 0 0 1 100 584.82 Tm\n(A) Tj\nET\n", content, StringComparison.Ordinal);
        Assert.Contains("BT\n/F2 10 Tf\n1 0 0 1 107.22 584.82 Tm\n(A) Tj\nET\n", content, StringComparison.Ordinal);

        // The line is as tall as its tallest run, here the bold one.
        Assert.Equal(200 + Bold.LineHeight(10, includeGap: true), page.Y, 6);
        Assert.True(Bold.LineHeight(10, includeGap: true) > Regular.LineHeight(10, includeGap: true));
    }

    [Fact]
    public void Drawing_runs_leaves_the_current_font_and_size_alone()
    {
        var page = NewPage(out _);
        page.Text([new TextRun("label", Bold, Size: 14), "value"], 0, 0);

        Assert.Same(Regular, page.CurrentFont);
        Assert.Equal(10, page.CurrentFontSize);
    }

    [Fact]
    public void Alignment_treats_the_runs_as_one_line()
    {
        var page = NewPage(out var doc);
        var centred = new TextOptions { Width = 100, Align = TextAlign.Center };
        var right = new TextOptions { Width = 100, Align = TextAlign.Right };
        page.Text([new TextRun("A", Bold), "A"], 0, 0, centred);
        page.Text([new TextRun("A", Bold), "A"], 0, 50, right);

        var segments = Segments(new PdfProbe(doc.ToArray()).FirstPageContent());

        // 7.22 + 6.67 = 13.89 wide: centred it starts at (100 - 13.89) / 2, flush right at 100 - 13.89.
        Assert.Equal([43.055, 50.275, 86.11, 93.33], segments.Select(s => s.X));
    }

    [Fact]
    public void Lines_break_inside_any_run()
    {
        var page = NewPage(out var doc);
        TextRun[] runs = [new TextRun("the quick ", Bold), "brown fox jumps over ", new TextRun("the lazy dog", Bold)];
        page.Text(runs, 0, 0, new TextOptions { Width = 60 });

        var segments = Segments(new PdfProbe(doc.ToArray()).FirstPageContent());
        var lines = segments.GroupBy(s => s.Y).Select(line => line.ToList()).ToList();

        Assert.True(lines.Count > 2);
        Assert.All(lines, line => Assert.Equal(0, line[0].X)); // every line starts at the left edge
        Assert.All(lines, line => Assert.True(line.Sum(s => s.Width) <= 60));
        Assert.Equal("the quick", lines[0][0].Text);           // the space at the break is dropped
        Assert.Equal("F1", lines[0][0].Font);
        Assert.Equal("F2", lines[1][0].Font);
        Assert.Equal(
            "the quick brown fox jumps over the lazy dog",
            string.Join(' ', lines.Select(line => string.Concat(line.Select(s => s.Text)))));
    }

    [Theory]
    [InlineData("the quick brown fox jumps over the lazy dog", 60)]
    [InlineData("abcdefghijklmnopqrstuvwxyz", 30)]
    [InlineData("two  spaces and a trailing one ", 55)]
    [InlineData("a\r\nb\n\nc", double.PositiveInfinity)]
    [InlineData("", 100)]
    public void A_single_run_wraps_exactly_like_plain_text(string text, double width)
    {
        var plain = TextLayout.Wrap(text, width, Regular, 10);
        var styled = TextLayout.Wrap([new TextLayout.StyledRun(text, Regular, 10, null)], width);

        Assert.Equal(
            plain.Select(l => (Latin1(l.Bytes), l.EndsParagraph)),
            styled.Select(l => (string.Concat(l.Segments.Select(s => Latin1(s.Bytes))), l.EndsParagraph)));
        Assert.All(styled, l => Assert.Equal(Regular.LineHeight(10, includeGap: true), l.Height, 9));
    }

    [Fact]
    public void A_newline_in_any_run_ends_the_line_and_an_empty_line_keeps_its_height()
    {
        var lines = TextLayout.Wrap(
            [new TextLayout.StyledRun("a\n", Bold, 10, null), new TextLayout.StyledRun("\nb", Regular, 20, null)],
            double.PositiveInfinity);

        Assert.Equal(["a", "", "b"], lines.Select(l => string.Concat(l.Segments.Select(s => Latin1(s.Bytes)))));
        Assert.Equal(Regular.LineHeight(20, includeGap: true), lines[1].Height, 9); // the blank line sits in the second run
    }

    [Fact]
    public void Runs_of_different_sizes_share_a_baseline()
    {
        var page = NewPage(out var doc);
        page.Text([new TextRun("A", Size: 20), "A"], 0, 0);

        var segments = Segments(new PdfProbe(doc.ToArray()).FirstPageContent());

        // Both sit on the baseline of the 20 pt run: 792 - 14.36.
        Assert.Equal([777.64, 777.64], segments.Select(s => s.Y));
        Assert.Equal([20d, 10d], segments.Select(s => s.Size));
        Assert.Equal(Regular.LineHeight(20, includeGap: true), page.Y, 6);
    }

    [Fact]
    public void A_coloured_run_does_not_leak_its_colour()
    {
        var page = NewPage(out var doc);
        page.Text([new TextRun("red", Color: "#ff0000"), " plain"], 0, 0);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        Assert.Contains("q\n1 0 0 rg\nBT\n/F1 10 Tf\n1 0 0 1 0 784.82 Tm\n(red) Tj\nET\nQ\nBT\n", content, StringComparison.Ordinal);
        Assert.Single(content.Split('\n'), l => l.EndsWith(" rg", StringComparison.Ordinal));
    }

    [Fact]
    public void Justified_runs_stretch_to_the_box_except_on_the_last_line()
    {
        var page = NewPage(out var doc);
        TextRun[] runs = [new TextRun("aa bb ", Bold), "cc dd ee ff gg hh"];
        page.Text(runs, 0, 0, new TextOptions { Width = 50, Align = TextAlign.Justify });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();
        var spacings = content.Split('\n')
            .Where(l => l.EndsWith(" Tw", StringComparison.Ordinal))
            .Select(l => double.Parse(l[..^3], CultureInfo.InvariantCulture))
            .ToList();
        var lines = Segments(content).GroupBy(s => s.Y).Select(line => line.ToList()).ToList();

        Assert.Equal(lines.Count, spacings.Count);
        Assert.True(spacings[0] > 0);
        Assert.Equal(0, spacings[^1]);

        // The first line holds both styles and still ends flush with the right edge of the box.
        var first = lines[0];
        Assert.Equal(2, first.Count);
        var last = first[^1];
        Assert.Equal(50, last.X + last.Width + spacings[0] * last.Text.Count(c => c == ' '), 2);
    }

    [Fact]
    public void Measuring_runs_matches_what_drawing_them_takes()
    {
        var page = NewPage(out _);
        TextRun[] runs = [new TextRun("Valor en letras: ", Bold), "doscientos cuarenta y siete mil doscientos diez pesos"];
        var options = new TextOptions { Width = 120, LineGap = 2 };

        var height = page.HeightOfString(runs, options);
        page.Text(runs, 0, 30, options);

        Assert.Equal(30 + height, page.Y, 6);
        Assert.True(height > 2 * Regular.LineHeight(10, includeGap: true)); // it did wrap
        Assert.Equal(Bold.WidthOfString("AB", 10) + Regular.WidthOfString("AB", 10), page.WidthOfString([new TextRun("AB", Bold), "AB"]), 6);
    }

    [Fact]
    public void No_runs_take_one_line_like_an_empty_string()
    {
        var page = NewPage(out _);
        page.Text(Array.Empty<TextRun>(), 0, 0);

        Assert.Equal(Regular.LineHeight(10, includeGap: true), page.Y, 6);
    }

    [Fact]
    public void Invalid_runs_are_rejected()
    {
        var page = NewPage(out _);

        Assert.Throws<ArgumentNullException>(() => page.Text((IEnumerable<TextRun>)null!));
        Assert.Throws<ArgumentException>(() => page.Text([null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Text([new TextRun("x", Size: 0)]));
    }

    /// <summary>The text objects of a content stream in drawing order, with the width each one advances.</summary>
    private static List<(string Font, double Size, double X, double Y, string Text, double Width)> Segments(string content)
    {
        var fonts = new Dictionary<string, PdfFont> { ["F1"] = Bold, ["F2"] = Regular };
        return SegmentRegex().Matches(content).Select(m =>
        {
            var size = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var text = m.Groups[5].Value;
            return (
                m.Groups[1].Value,
                size,
                double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                text,
                fonts[m.Groups[1].Value].WidthOfString(text, size));
        }).ToList();
    }

    private static string Latin1(byte[] bytes) => System.Text.Encoding.Latin1.GetString(bytes);

    [GeneratedRegex(@"BT\n/(F\d+) ([\d.]+) Tf\n1 0 0 1 ([\d.-]+) ([\d.-]+) Tm\n\((.*)\) Tj\nET")]
    private static partial Regex SegmentRegex();
}
