using System.Buffers.Binary;
using System.Text.RegularExpressions;
using SimplyPdf.Fonts.TrueType;

namespace SimplyPdf.Tests;

public partial class TrueTypeTests
{
    private static string FontPath(string file) => Path.Combine(AppContext.BaseDirectory, "fonts", file);

    private static byte[] ShareTechMono() => File.ReadAllBytes(FontPath("ShareTechMono-Regular.ttf"));

    [Fact]
    public void Parses_metrics_names_and_character_map()
    {
        var font = TrueTypeFont.Parse(ShareTechMono());

        Assert.Equal("ShareTechMono-Regular", font.PostScriptName);
        Assert.True(font.UnitsPerEm is 1000 or 2048);
        Assert.True(font.NumGlyphs > 100);
        Assert.True(font.Ascender > 0 && font.Descender < 0);
        Assert.True(font.IsFixedPitch);
        Assert.NotEqual(0, font.GlyphFor('A'));
        Assert.True(font.HasGlyph('ñ'));
        Assert.True(font.HasGlyph('€'));
        Assert.Equal(font.AdvanceWidth(font.GlyphFor('A')), font.AdvanceWidth(font.GlyphFor('i'))); // monospace
        Assert.Equal(0, font.GlyphFor(0x1F600)); // no emoji -> .notdef
        Assert.Equal(0, font.EmbeddingFlags); // installable
    }

    [Theory]
    [InlineData("VT323-Regular.ttf", "VT323-Regular")]
    [InlineData("CourierPrime-Regular.ttf", "CourierPrime-Regular")]
    [InlineData("CourierPrime-Bold.ttf", "CourierPrime-Bold")]
    public void Parses_the_other_sample_fonts(string file, string expectedName)
    {
        var font = TrueTypeFont.Parse(File.ReadAllBytes(FontPath(file)));

        Assert.Equal(expectedName, font.PostScriptName);
        Assert.True(font.HasGlyph('á'));
        Assert.True(font.AdvanceWidth(font.GlyphFor('M')) > 0);
    }

    [Fact]
    public void Subset_keeps_used_glyphs_and_their_components_and_drops_the_rest()
    {
        var original = TrueTypeFont.Parse(ShareTechMono());
        var a = original.GlyphFor('A');
        var eAcute = original.GlyphFor('é');
        var z = original.GlyphFor('Z');

        var subset = TrueTypeFont.Parse(TrueTypeSubsetter.Subset(original, [a, eAcute]));

        Assert.Equal(original.NumGlyphs, subset.NumGlyphs); // ids preserved
        Assert.Equal(original.GlyphData(a).ToArray(), subset.GlyphData(a).ToArray());
        Assert.Equal(original.GlyphData(eAcute).ToArray(), subset.GlyphData(eAcute).ToArray());
        Assert.True(subset.GlyphData(z).IsEmpty, "unused glyph should be empty");
        Assert.Equal(a, subset.GlyphFor('A')); // cmap intact
        Assert.Equal(original.AdvanceWidth(z), subset.AdvanceWidth(z)); // hmtx intact
        Assert.True(subset.Data.Length < original.Data.Length / 2);

        foreach (var component in original.ComponentGlyphs(eAcute))
        {
            Assert.False(subset.GlyphData(component).IsEmpty, "composite components must survive");
        }
    }

    [Fact]
    public void Subset_file_checksum_matches_the_truetype_magic()
    {
        var original = TrueTypeFont.Parse(ShareTechMono());
        var subset = TrueTypeSubsetter.Subset(original, [original.GlyphFor('S')]);

        Assert.Equal(0xB1B0AFBAu, TrueTypeSubsetter.Checksum(subset));
        Assert.Equal(0x00010000u, BinaryPrimitives.ReadUInt32BigEndian(subset));
        Assert.Contains("post", TrueTypeFont.Parse(subset).Tables.Keys);
    }

    [Fact]
    public void Embedded_font_is_written_as_a_subsetted_truetype_simple_font()
    {
        var font = PdfFont.FromFile(FontPath("ShareTechMono-Regular.ttf"));
        var doc = new PdfDocument();
        doc.AddPage().Font(font, 9).Text("Recibo POS ñ á €");
        doc.AddPage().Font(font).Text("Segunda página"); // same instance -> same font object

        var probe = new PdfProbe(doc.ToArray());
        var objects = probe.XrefOffsets().Keys.Select(n => (Number: n, Body: probe.Object(n))).ToList();

        var fontDict = Assert.Single(objects, o => o.Body.Contains("/Subtype /TrueType", StringComparison.Ordinal));
        Assert.Matches(SubsetBaseFontRegex(), fontDict.Body);
        Assert.Contains("/FirstChar 32", fontDict.Body, StringComparison.Ordinal);
        Assert.Contains("/LastChar 255", fontDict.Body, StringComparison.Ordinal);
        Assert.Contains("/Encoding /WinAnsiEncoding", fontDict.Body, StringComparison.Ordinal);
        var widths = WidthsRegex().Match(fontDict.Body).Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(224, widths.Length);

        var descriptor = Assert.Single(objects, o => o.Body.Contains("/Type /FontDescriptor", StringComparison.Ordinal));
        Assert.Contains("/Flags 33", descriptor.Body, StringComparison.Ordinal); // nonsymbolic + fixed pitch
        Assert.Contains("/FontFile2", descriptor.Body, StringComparison.Ordinal);

        var fontFile = Assert.Single(objects, o => o.Body.Contains("/Length1", StringComparison.Ordinal));
        var program = probe.StreamData(fontFile.Number);
        var length1 = int.Parse(Length1Regex().Match(fontFile.Body).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(length1, program.Length);
        Assert.True(program.Length < ShareTechMono().Length / 2, "subset should be much smaller than the full font");

        var embedded = TrueTypeFont.Parse(program);
        Assert.NotEqual(0, embedded.GlyphFor('ñ'));
        Assert.False(embedded.GlyphData(embedded.GlyphFor('ñ')).IsEmpty);
        Assert.True(embedded.GlyphData(embedded.GlyphFor('Z')).IsEmpty);
    }

    [Fact]
    public void Text_measurement_uses_the_embedded_font_metrics()
    {
        var ttf = TrueTypeFont.Parse(ShareTechMono());
        var font = PdfFont.FromBytes(ShareTechMono());
        var page = new PdfDocument().AddPage().Font(font, 10);

        var expected = 2.0 * ttf.AdvanceWidth(ttf.GlyphFor('A')) * 1000.0 / ttf.UnitsPerEm * 10 / 1000.0;
        Assert.Equal(expected, page.WidthOfString("AB"), 2);
        Assert.Equal((ttf.Ascender - ttf.Descender) * 10.0 / ttf.UnitsPerEm, page.CurrentLineHeight(), 3);
        Assert.Equal("ShareTechMono-Regular", font.Name);
        Assert.True(font.IsEmbedded);
        Assert.False(PdfFont.Helvetica.IsEmbedded);
    }

    [Fact]
    public void Widths_follow_winansi_glyph_names_for_nbsp_and_soft_hyphen()
    {
        var font = PdfFont.FromBytes(ShareTechMono());

        Assert.Equal(font.WidthOfCode((byte)' '), font.WidthOfCode(0xA0));
        Assert.Equal(font.WidthOfCode((byte)'-'), font.WidthOfCode(0xAD));
        Assert.True(font.WidthOfCode((byte)'W') > 0);
    }

    [Fact]
    public void Restricted_licence_fonts_are_rejected()
    {
        var data = ShareTechMono();
        var os2 = TrueTypeFont.Parse(data).Tables["OS/2"].Offset;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(os2 + 8), 0x0002);

        var error = Assert.Throws<InvalidOperationException>(() => PdfFont.FromBytes(data));
        Assert.Contains("restricted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_subsetting_flag_embeds_the_whole_font()
    {
        var data = ShareTechMono();
        var os2 = TrueTypeFont.Parse(data).Tables["OS/2"].Offset;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(os2 + 8), 0x0200);

        var doc = new PdfDocument();
        doc.AddPage().Font(PdfFont.FromBytes(data)).Text("x");

        var probe = new PdfProbe(doc.ToArray());
        var objects = probe.XrefOffsets().Keys.Select(n => (Number: n, Body: probe.Object(n))).ToList();
        var fontFile = Assert.Single(objects, o => o.Body.Contains("/Length1", StringComparison.Ordinal));
        var fontDict = Assert.Single(objects, o => o.Body.Contains("/Subtype /TrueType", StringComparison.Ordinal));

        Assert.Equal(data, probe.StreamData(fontFile.Number));
        Assert.Contains("/BaseFont /ShareTechMono-Regular", fontDict.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Cff_and_collection_fonts_are_rejected_with_a_clear_message()
    {
        var otto = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(otto, 0x4F54544F);
        Assert.Throws<NotSupportedException>(() => PdfFont.FromBytes(otto));

        var ttcf = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(ttcf, 0x74746366);
        Assert.Throws<NotSupportedException>(() => PdfFont.FromBytes(ttcf));

        Assert.Throws<InvalidDataException>(() => PdfFont.FromBytes([1, 2, 3]));
    }

    [Fact]
    public void Standard_fonts_are_shared_singletons()
    {
        Assert.Same(PdfFont.Helvetica, PdfFont.Standard(StandardFont.Helvetica));
        Assert.Same(PdfFont.CourierBold, PdfFont.Standard(StandardFont.CourierBold));
        Assert.Equal("Helvetica-Bold", PdfFont.HelveticaBold.Name);
    }

    [GeneratedRegex(@"/BaseFont /[A-Z]{6}\+ShareTechMono-Regular")]
    private static partial Regex SubsetBaseFontRegex();

    [GeneratedRegex(@"/Widths \[([^\]]*)\]")]
    private static partial Regex WidthsRegex();

    [GeneratedRegex(@"/Length1 (\d+)")]
    private static partial Regex Length1Regex();
}
