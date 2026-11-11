using System.Globalization;
using System.Text.RegularExpressions;

namespace SimplyPdf.Tests;

public partial class LineHeightTests
{
    private static PdfPage NewPage(out PdfDocument doc)
    {
        doc = new PdfDocument { CompressStreams = false };
        return doc.AddPage(PdfPageSize.Letter, PdfMargins.All(0)).Font(PdfFont.Helvetica, 10);
    }

    [Fact]
    public void A_fixed_line_height_replaces_the_fonts_own()
    {
        var page = NewPage(out var doc);
        page.Text("one\ntwo\nthree", 50, 100, new TextOptions { LineHeight = 20 });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // First baseline at 792 - 100 - 7.18, then 20 pt apart instead of Helvetica's 11.56.
        Assert.Contains("1 0 0 1 50 684.82 Tm\n(one) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 50 664.82 Tm\n(two) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 50 644.82 Tm\n(three) Tj", content, StringComparison.Ordinal);
        Assert.Equal(160, page.Y, 6);
    }

    [Fact]
    public void Line_gap_is_added_to_a_fixed_line_height_and_measuring_agrees()
    {
        var page = NewPage(out _);
        var options = new TextOptions { LineHeight = 20, LineGap = 2 };

        var height = page.HeightOfString("one\ntwo", options);
        page.Text("one\ntwo", 0, 0, options);

        Assert.Equal(44, height, 6);
        Assert.Equal(44, page.Y, 6);
    }

    [Fact]
    public void Lines_that_mix_fonts_advance_evenly_under_a_fixed_line_height()
    {
        TextRun[] runs = [new TextRun("aa bb ", PdfFont.HelveticaBold), "cc dd ee ff gg hh ii jj kk ll mm"];

        // On their own the lines follow their fonts: the one with Helvetica-Bold is taller than the rest.
        var natural = NewPage(out var naturalDoc);
        natural.Text(runs, 0, 0, new TextOptions { Width = 50 });
        var uneven = Spacings(naturalDoc);
        Assert.True(uneven.Count >= 2);
        Assert.NotEqual(uneven[0], uneven[^1]);

        var page = NewPage(out var doc);
        var options = new TextOptions { Width = 50, LineHeight = 12 };
        var height = page.HeightOfString(runs, options);
        page.Text(runs, 0, 0, options);

        var even = Spacings(doc);
        Assert.Equal(uneven.Count, even.Count);
        Assert.All(even, spacing => Assert.Equal(12, spacing, 3));
        Assert.Equal((even.Count + 1) * 12, height, 6);
        Assert.Equal(height, page.Y, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_line_height_must_be_positive(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextOptions { LineHeight = value });
    }

    /// <summary>Distances between consecutive baselines of the first page, top to bottom.</summary>
    private static List<double> Spacings(PdfDocument doc)
    {
        var baselines = BaselineRegex().Matches(new PdfProbe(doc.ToArray()).FirstPageContent())
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        return baselines.Zip(baselines.Skip(1), (upper, lower) => upper - lower).ToList();
    }

    [GeneratedRegex(@"1 0 0 1 [\d.-]+ ([\d.-]+) Tm")]
    private static partial Regex BaselineRegex();
}
