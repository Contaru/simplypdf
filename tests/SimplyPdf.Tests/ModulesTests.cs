using System.Globalization;
using System.Text.RegularExpressions;

namespace SimplyPdf.Tests;

public partial class ModulesTests
{
    // Asymmetric on purpose: transposing it, or flipping either axis, changes the drawing.
    //   X X . X
    //   . . . .
    //   . X X X
    private static readonly bool[,] Sample =
    {
        { true, true, false, true },
        { false, false, false, false },
        { false, true, true, true },
    };

    [Fact]
    public void Each_run_of_dark_modules_is_one_rectangle_and_the_symbol_is_filled_once()
    {
        var drawn = Drawn(page => page.Modules(Sample, 10, 20, moduleSize: 2));

        // Seven dark modules, three runs: no rectangle per module, no fill per row.
        Assert.Equal("10 20 4 2 re\n16 20 2 2 re\n12 24 6 2 re\nf\n", drawn);
    }

    [Fact]
    public void One_module_is_one_point_unless_a_size_is_given()
    {
        var drawn = Drawn(page => page.Modules(Sample, 10, 20));

        Assert.Equal("10 20 2 1 re\n13 20 1 1 re\n11 22 3 1 re\nf\n", drawn);
    }

    [Fact]
    public void Callback_takes_column_then_row_and_draws_what_the_array_does()
    {
        var calls = new List<(int Column, int Row)>();

        var drawn = Drawn(page => page.Modules(3, (column, row) =>
        {
            calls.Add((column, row));
            return column >= row; // upper triangle: wrong if the two were swapped
        }, 0, 0));

        bool[,] upperTriangle = { { true, true, true }, { false, true, true }, { false, false, true } };
        Assert.Equal("0 0 3 1 re\n1 1 2 1 re\n2 2 1 1 re\nf\n", drawn);
        Assert.Equal(drawn, Drawn(page => page.Modules(upperTriangle, 0, 0)));
        Assert.Equal(9, calls.Distinct().Count());
        Assert.Equal(9, calls.Count); // once per module: a generator may compute them on demand
    }

    [Fact]
    public void Quiet_zone_shifts_the_modules_by_whole_modules_and_paints_nothing()
    {
        var drawn = Drawn(page => page.Modules(Sample, 10, 20, moduleSize: 2, quietZone: 4));

        // (x, y) is the corner of the quiet zone: the first module starts 4 x 2 pt further in.
        Assert.Equal("18 28 4 2 re\n24 28 2 2 re\n20 32 6 2 re\nf\n", drawn);
    }

    [Fact]
    public void Width_covers_the_quiet_zone_and_sets_the_module_size()
    {
        // 4 columns + 2 x 3 modules of quiet zone = 10 modules across 25 pt: 2.5 pt each.
        var drawn = Drawn(page => page.Modules(Sample, 100, 200, width: 25, quietZone: 3));

        Assert.Equal("107.5 207.5 5 2.5 re\n115 207.5 2.5 2.5 re\n110 212.5 7.5 2.5 re\nf\n", drawn);

        // The rightmost module ends 3 modules (7.5 pt) short of x + width: the quiet zone again.
        Assert.Equal(100m + 25m - 7.5m, Rects(drawn).Max(r => r.X + r.Width));
    }

    [Fact]
    public void Modules_of_a_fractional_size_tile_without_gaps_or_overlaps()
    {
        // 10 pt across 3 modules is 3.333… pt. Written naively (position and size rounded on their
        // own) the second row would end at 3.333 + 3.333 = 6.666 and the third start at 6.667.
        bool[,] checker = { { true, false, true }, { false, true, false }, { true, false, true } };

        var rects = Rects(Drawn(page => page.Modules(checker, 0, 0, width: 10)));

        Assert.Equal(5, rects.Count);
        decimal[] edges = [0m, 3.333m, 6.667m, 10m];
        Assert.All(rects, r =>
        {
            Assert.Contains(r.X, edges);
            Assert.Contains(r.X + r.Width, edges);
            Assert.Contains(r.Y, edges);
            Assert.Contains(r.Y + r.Height, edges);
        });
        Assert.Equal([(0m, 0m), (6.667m, 0m), (3.333m, 3.333m), (0m, 6.667m), (6.667m, 6.667m)], rects.Select(r => (r.X, r.Y)));
    }

    [Fact]
    public void Colour_applies_to_the_symbol_only()
    {
        var drawn = Drawn(page => page.FillColor("red").Modules(Sample, 10, 20, color: PdfColor.Black));

        // q … Q puts the page's fill colour (the text colour) back to red.
        Assert.Equal("1 0 0 rg\nq\n0 0 0 rg\n10 20 2 1 re\n13 20 1 1 re\n11 22 3 1 re\nf\nQ\n", drawn);
    }

    [Fact]
    public void Without_a_colour_the_current_fill_colour_is_used_and_no_state_is_saved()
    {
        var drawn = Drawn(page => page.FillColor("red").Modules(Sample, 10, 20));

        Assert.Equal("1 0 0 rg\n10 20 2 1 re\n13 20 1 1 re\n11 22 3 1 re\nf\n", drawn);
    }

    [Fact]
    public void Path_is_consumed_so_a_later_fill_has_nothing_to_paint()
    {
        var drawn = Drawn(page => page.Modules(Sample, 10, 20).Fill());

        Assert.Single(Regex.Matches(drawn, @"^f$", RegexOptions.Multiline));
    }

    [Theory]
    [InlineData(0, 0)] // no modules at all
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(5, 5)] // modules, none of them dark
    public void Matrix_without_dark_modules_writes_nothing(int rows, int columns)
    {
        var drawn = Drawn(page => page
            .Modules(new bool[rows, columns], 10, 20, quietZone: 4, color: "navy")
            .Modules(new bool[rows, columns], 10, 20, width: 80)
            .Modules(columns, (_, _) => false, 10, 20));

        Assert.Equal(string.Empty, drawn);
    }

    [Fact]
    public void Arguments_are_validated()
    {
        var page = new PdfDocument().AddPage();
        Func<int, int, bool> dark = (_, _) => true;

        Assert.Throws<ArgumentNullException>(() => page.Modules(null!, 0, 0));
        Assert.Throws<ArgumentNullException>(() => page.Modules(21, null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(-1, dark, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(21, dark, 0, 0, quietZone: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(Sample, 0, 0, quietZone: -1));

        Assert.Throws<ArgumentException>(() => page.Modules(21, dark, 0, 0, width: 80, moduleSize: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Sizes_must_be_positive_and_finite(double size)
    {
        var page = new PdfDocument().AddPage();

        Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(Sample, 0, 0, width: size));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(Sample, 0, 0, moduleSize: size));
    }

    [Fact]
    public void Failure_leaves_the_page_untouched()
    {
        // A position that cannot be written, or a generator that throws half-way, must not leave
        // an open q or a few orphan rectangles behind for the next painting call to pick up.
        var drawn = Drawn(page =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(Sample, double.NaN, 0, color: "navy"));
            Assert.Throws<ArgumentOutOfRangeException>(() => page.Modules(Sample, 0, double.NegativeInfinity));
            Assert.Throws<InvalidOperationException>(() => page.Modules(
                5, (column, row) => row < 3 ? true : throw new InvalidOperationException("generator failed"), 0, 0, color: "navy"));
            page.Fill();
        });

        Assert.Equal(string.Empty, drawn);
    }

    /// <summary>What <paramref name="draw"/> adds to the content stream of an empty page.</summary>
    private static string Drawn(Action<PdfPage> draw)
    {
        var blank = Content(_ => { });
        var content = Content(draw);
        Assert.StartsWith(blank, content, StringComparison.Ordinal);
        return content[blank.Length..];
    }

    private static string Content(Action<PdfPage> draw)
    {
        var doc = new PdfDocument { CompressStreams = false };
        draw(doc.AddPage());
        return new PdfProbe(doc.ToArray()).FirstPageContent();
    }

    private static List<(decimal X, decimal Y, decimal Width, decimal Height)> Rects(string content) =>
        RectRegex().Matches(content)
            .Select(m => (Number(m, 1), Number(m, 2), Number(m, 3), Number(m, 4)))
            .ToList();

    private static decimal Number(Match match, int group) => decimal.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\S+) (\S+) (\S+) (\S+) re$", RegexOptions.Multiline)]
    private static partial Regex RectRegex();
}
