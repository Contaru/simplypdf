using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SimplyPdf.Fonts.TrueType;
using SimplyPdf.Text;

namespace SimplyPdf.Tests;

public partial class UnicodeTextTests
{
    private static readonly string CourierPrimePath = Path.Combine(AppContext.BaseDirectory, "fonts", "CourierPrime-Regular.ttf");

    private static PdfFont CourierPrime() => PdfFont.FromFile(CourierPrimePath);

    /// <summary>The glyph ids of <paramref name="text"/> as the content stream writes them.</summary>
    private static string Glyphs(string text)
    {
        var font = TrueTypeFont.Parse(File.ReadAllBytes(CourierPrimePath));
        return string.Concat(text.EnumerateRunes().Select(rune => font.GlyphFor(rune.Value).ToString("X4", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void An_embedded_font_draws_characters_outside_winansi_as_glyph_ids()
    {
        var doc = new PdfDocument { CompressStreams = false };
        doc.AddPage().Font(CourierPrime(), 10).Text("Łódź Ω", 50, 100);

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();

        // Ł, ź and Ω have no WinAnsi code: before, each of them came out as a question mark.
        Assert.Contains($"<{Glyphs("Łódź Ω")}> Tj", content, StringComparison.Ordinal);
        Assert.All(CourierPrime().Encode("Łódź Ω"), code => Assert.NotEqual(0, code.Value));
    }

    [Fact]
    public void The_file_tells_viewers_which_character_each_glyph_is()
    {
        var doc = new PdfDocument();
        doc.AddPage().Font(CourierPrime(), 10).Text("Łódź Ω");

        var probe = new PdfProbe(doc.ToArray());
        var map = probe.XrefOffsets().Keys
            .Where(n => probe.Object(n).Contains("stream\n", StringComparison.Ordinal))
            .Select(n => Encoding.Latin1.GetString(probe.StreamData(n)))
            .Single(text => text.Contains("beginbfchar", StringComparison.Ordinal));

        Assert.Contains($"<{Glyphs("Ł")}> <0141>", map, StringComparison.Ordinal);
        Assert.Contains($"<{Glyphs("Ω")}> <03A9>", map, StringComparison.Ordinal);
        Assert.Contains($"<{Glyphs(" ")}> <0020>", map, StringComparison.Ordinal);
        Assert.Contains("6 beginbfchar", map, StringComparison.Ordinal); // Ł ó d ź, the space and Ω
    }

    [Fact]
    public void What_a_font_cannot_draw_is_its_notdef_glyph_or_a_question_mark()
    {
        var embedded = CourierPrime();

        Assert.Equal(0, Assert.Single(embedded.Encode("中")).Value);
        Assert.Equal(0, Assert.Single(embedded.Encode("😀")).Value); // one code point, one glyph, not two
        Assert.Equal((ushort)'?', Assert.Single(PdfFont.Helvetica.Encode("Ł")).Value);
        Assert.Equal((ushort)'?', Assert.Single(PdfFont.Helvetica.Encode("😀")).Value);
    }

    [Fact]
    public void Measuring_and_wrapping_use_the_glyphs_that_are_drawn()
    {
        var font = CourierPrime();
        var advance = font.WidthOfString("M", 10); // monospaced

        Assert.Equal(4 * advance, font.WidthOfString("Łódź", 10), 6);

        // Room for seven glyphs: the plain space breaks the line, the no-break space does not.
        var lines = TextLayout.Wrap("aaa bbb ccc", 7.5 * advance, font, 10);
        Assert.Equal([7, 3], lines.Select(line => line.Codes.Length));
    }

    [Fact]
    public void Justified_text_in_an_embedded_font_widens_its_spaces_itself()
    {
        var doc = new PdfDocument { CompressStreams = false };
        var font = CourierPrime();
        var advance = font.WidthOfString("M", 10);
        doc.AddPage().Font(font, 10).Text("aa bb cc dd ee ff gg hh", 0, 0, new TextOptions { Width = 100, Align = TextAlign.Justify });

        var content = new PdfProbe(doc.ToArray()).FirstPageContent();
        var shown = content.Split('\n').Where(l => l.EndsWith(" TJ", StringComparison.Ordinal) || l.EndsWith(" Tj", StringComparison.Ordinal)).ToList();

        // The word-spacing operator does not reach two-byte codes, so it must not be relied on.
        Assert.DoesNotContain(" Tw\n", content, StringComparison.Ordinal);
        Assert.True(shown.Count >= 2);
        Assert.EndsWith(" Tj", shown[^1], StringComparison.Ordinal); // the last line stays as it is

        // First line: its glyphs plus what was added after each space reach the right edge exactly.
        var first = shown[0];
        var glyphs = GlyphRunRegex().Matches(first).Sum(m => m.Groups[1].Length / 4);
        var added = AdjustmentRegex().Matches(first).Sum(m => -double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 10 / 1000);
        Assert.True(added > 0);
        Assert.Equal(100, glyphs * advance + added, 2);
    }

    [GeneratedRegex(@"<([0-9A-F]+)>")]
    private static partial Regex GlyphRunRegex();

    [GeneratedRegex(@"> (-?[\d.]+) <")]
    private static partial Regex AdjustmentRegex();
}
