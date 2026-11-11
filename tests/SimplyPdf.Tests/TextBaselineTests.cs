namespace SimplyPdf.Tests;

public class TextBaselineTests
{
    private static PdfPage NewPage(out PdfDocument doc)
    {
        doc = new PdfDocument { CompressStreams = false };
        return doc.AddPage(PdfPageSize.Letter, PdfMargins.All(0)).Font(PdfFont.Helvetica, 10);
    }

    [Fact]
    public void Ascent_and_descent_add_up_to_the_line_height()
    {
        var helvetica = PdfFont.Helvetica;
        Assert.Equal(7.18, helvetica.Ascent(10), 6);  // AFM ascender 718
        Assert.Equal(2.07, helvetica.Descent(10), 6); // AFM descender -207, reported as a distance
        Assert.Equal(helvetica.LineHeight(10), helvetica.Ascent(10) + helvetica.Descent(10), 9);

        var embedded = PdfFont.FromFile(Path.Combine(AppContext.BaseDirectory, "fonts", "ShareTechMono-Regular.ttf"));
        Assert.True(embedded.Ascent(10) > 0);
        Assert.True(embedded.Descent(10) > 0);
        Assert.Equal(embedded.LineHeight(10), embedded.Ascent(10) + embedded.Descent(10), 9);
    }

    [Theory]
    [InlineData(TextBaseline.Top, "584.82")]      // 792 - 200 - 7.18, as without the option
    [InlineData(TextBaseline.Alphabetic, "592")]  // 792 - 200
    [InlineData(TextBaseline.Middle, "589.445")]  // 592 - (7.18 - 2.07) / 2
    [InlineData(TextBaseline.Bottom, "594.07")]   // 592 + 2.07
    public void Baseline_option_says_what_y_refers_to(TextBaseline baseline, string expectedY)
    {
        var page = NewPage(out var doc);
        page.Text("Hi", 100, 200, new TextOptions { Baseline = baseline });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        Assert.Contains($"1 0 0 1 100 {expectedY} Tm\n(Hi) Tj", content, StringComparison.Ordinal);
        Assert.Equal(211.56, page.Y, 3); // the cursor advances by one line whatever y referred to
    }

    [Fact]
    public void Text_placed_by_its_baseline_flows_from_baseline_to_baseline()
    {
        var page = NewPage(out var doc);
        var options = new TextOptions { Baseline = TextBaseline.Alphabetic };
        page.Text("one\ntwo", 50, 100, options).Text("three", options);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // Baselines 100, 111.56 and 123.12 below the top of the page.
        Assert.Contains("1 0 0 1 50 692 Tm\n(one) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 50 680.44 Tm\n(two) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 50 668.88 Tm\n(three) Tj", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Runs_of_different_sizes_are_placed_by_the_line_they_share()
    {
        var page = NewPage(out var doc);
        TextRun[] runs = [new TextRun("A", Size: 20), "A"];
        page.Text(runs, 0, 200, new TextOptions { Baseline = TextBaseline.Alphabetic });
        page.Text(runs, 0, 300, new TextOptions { Baseline = TextBaseline.Bottom });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // Alphabetic: both runs on y itself. The 20 pt A is 13.34 wide.
        Assert.Contains("1 0 0 1 0 592 Tm", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 13.34 592 Tm", content, StringComparison.Ordinal);

        // Bottom: the line reaches 4.14 below its baseline, the descent of its 20 pt run.
        Assert.Contains("1 0 0 1 0 496.14 Tm", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 13.34 496.14 Tm", content, StringComparison.Ordinal);
    }
}
