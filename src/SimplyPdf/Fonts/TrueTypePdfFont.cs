using System.Globalization;
using System.Text;
using SimplyPdf.Fonts.TrueType;
using SimplyPdf.Internal;
using SimplyPdf.Text;

namespace SimplyPdf.Fonts;

/// <summary>
/// A TrueType font embedded as a composite font (<c>/Type0</c> over a <c>/CIDFontType2</c>, with
/// <c>/Identity-H</c> and <c>/FontFile2</c>). Text is written as glyph ids, two bytes each, so every
/// character the font has a glyph for can be drawn, whatever its script; a <c>/ToUnicode</c> map
/// lets viewers copy and search it. Characters map to glyphs one to one, left to right: there is
/// no shaping, so scripts that need it (Arabic, Indic) and right-to-left text do not come out right.
/// </summary>
internal sealed class TrueTypePdfFont : PdfFont
{
    // Characters a font may leave out although one of its glyphs draws them just as well.
    private static readonly (int Missing, int Instead)[] Fallbacks =
    [
        (0x00A0, ' '), (0x2007, ' '), (0x202F, ' '), // no-break, figure and narrow no-break spaces
        (0x00AD, '-'), (0x2010, '-'), (0x2011, '-'), (0x2212, '-'), // soft hyphen, hyphens, minus sign
    ];

    private readonly TrueTypeFont _font;
    private readonly double _scale;
    private readonly bool _subset;

    public TrueTypePdfFont(TrueTypeFont font)
    {
        _font = font;
        _scale = 1000.0 / font.UnitsPerEm;

        // OS/2 fsType: bit 1 restricted licence, bit 8 bitmap-only, bit 9 no subsetting.
        var permissions = font.EmbeddingFlags;
        if ((permissions & 0x000F) == 0x0002)
        {
            throw new InvalidOperationException($"Font '{font.PostScriptName}' has restricted-licence embedding (OS/2 fsType 2) and cannot be embedded in a PDF.");
        }

        if ((permissions & 0x0100) != 0)
        {
            throw new InvalidOperationException($"Font '{font.PostScriptName}' only allows bitmap embedding (OS/2 fsType bit 8); outline embedding is not permitted.");
        }

        _subset = (permissions & 0x0200) == 0;

        Ascender = font.Ascender * _scale;
        Descender = font.Descender * _scale;
        LineGap = font.LineGap * _scale;
    }

    public override string Name => _font.PostScriptName;

    public override bool IsEmbedded => true;

    internal override double Ascender { get; }

    internal override double Descender { get; }

    internal override double LineGap { get; }

    internal override bool WordSpacingOperatorApplies => false;

    /// <summary>One glyph id per code point; 0, the font's .notdef, for what it cannot draw.</summary>
    internal override TextCode[] Encode(string text)
    {
        var codes = new List<TextCode>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            codes.Add(new TextCode(GlyphFor(rune.Value), rune.Value == ' '));
        }

        return [.. codes];
    }

    internal override double WidthOfCode(ushort code) => Math.Round(_font.AdvanceWidth(code) * _scale, 3);

    internal override void AppendShow(StringBuilder content, ReadOnlySpan<TextCode> codes, double wordSpacing, double size)
    {
        if (wordSpacing == 0)
        {
            AppendGlyphs(content, codes);
            content.Append(" Tj\n");
            return;
        }

        // Tw does not reach two-byte codes, so the extra room goes in after every space instead.
        // TJ counts in thousandths of the font size and a positive number pulls the text back.
        var adjustment = PdfFormat.Num(-wordSpacing * 1000 / size);
        content.Append('[');
        var start = 0;
        for (var i = 0; i < codes.Length; i++)
        {
            if (codes[i].IsSpace && i < codes.Length - 1)
            {
                AppendGlyphs(content, codes[start..(i + 1)]);
                content.Append(' ').Append(adjustment).Append(' ');
                start = i + 1;
            }
        }

        AppendGlyphs(content, codes[start..]);
        content.Append("] TJ\n");
    }

    internal override PdfObjectRef WriteTo(PdfWriter writer, IReadOnlySet<ushort> usedCodes)
    {
        var glyphs = usedCodes.Order().ToArray();

        byte[] program;
        string baseFont;
        if (_subset)
        {
            program = TrueTypeSubsetter.Subset(_font, glyphs.Where(glyph => glyph != 0));
            baseFont = SubsetTag(glyphs) + "+" + _font.PostScriptName;
        }
        else
        {
            program = _font.Data;
            baseFont = _font.PostScriptName;
        }

        var fontFile = writer.Add(new PdfDictionary { ["Length1"] = program.Length }, program, compress: true);

        var flags = 4; // Symbolic: the glyphs are addressed directly, not through a standard encoding
        if (_font.IsFixedPitch)
        {
            flags |= 1;
        }

        if (_font.ItalicAngle != 0)
        {
            flags |= 64;
        }

        var missingWidth = WidthOfCode(0);
        var descriptor = writer.Add(new PdfDictionary
        {
            ["Type"] = new PdfName("FontDescriptor"),
            ["FontName"] = new PdfName(baseFont),
            ["Flags"] = flags,
            ["FontBBox"] = new object[] { Round(_font.XMin), Round(_font.YMin), Round(_font.XMax), Round(_font.YMax) },
            ["ItalicAngle"] = _font.ItalicAngle,
            ["Ascent"] = Round(_font.Ascender),
            ["Descent"] = Round(_font.Descender),
            ["CapHeight"] = Round(_font.CapHeight),
            ["XHeight"] = Round(_font.XHeight),
            ["StemV"] = EstimateStemV(_font.WeightClass),
            ["MissingWidth"] = missingWidth,
            ["FontFile2"] = fontFile,
        });

        var descendant = writer.Add(new PdfDictionary
        {
            ["Type"] = new PdfName("Font"),
            ["Subtype"] = new PdfName("CIDFontType2"),
            ["BaseFont"] = new PdfName(baseFont),
            ["CIDSystemInfo"] = new PdfDictionary { ["Registry"] = "Adobe", ["Ordering"] = "Identity", ["Supplement"] = 0 },
            ["FontDescriptor"] = descriptor,
            ["DW"] = missingWidth,
            ["W"] = Widths(glyphs),
            ["CIDToGIDMap"] = new PdfName("Identity"),
        });

        var toUnicode = writer.Add([], Encoding.ASCII.GetBytes(ToUnicodeMap(glyphs)), compress: true);

        return writer.Add(new PdfDictionary
        {
            ["Type"] = new PdfName("Font"),
            ["Subtype"] = new PdfName("Type0"),
            ["BaseFont"] = new PdfName(baseFont),
            ["Encoding"] = new PdfName("Identity-H"),
            ["DescendantFonts"] = new object[] { descendant },
            ["ToUnicode"] = toUnicode,
        });
    }

    private ushort GlyphFor(int codePoint)
    {
        var glyph = _font.GlyphFor(codePoint);
        if (glyph != 0)
        {
            return glyph;
        }

        foreach (var (missing, instead) in Fallbacks)
        {
            if (missing == codePoint)
            {
                return _font.GlyphFor(instead);
            }
        }

        return 0;
    }

    private static void AppendGlyphs(StringBuilder content, ReadOnlySpan<TextCode> codes)
    {
        content.Append('<');
        foreach (var code in codes)
        {
            content.Append(code.Value.ToString("X4", CultureInfo.InvariantCulture));
        }

        content.Append('>');
    }

    /// <summary>The <c>/W</c> array: each run of consecutive glyph ids followed by the widths of its glyphs.</summary>
    private List<object> Widths(ushort[] glyphs)
    {
        var entries = new List<object>();
        var i = 0;
        while (i < glyphs.Length)
        {
            var run = new List<object> { WidthOfCode(glyphs[i]) };
            var first = glyphs[i];
            i++;
            while (i < glyphs.Length && glyphs[i] == glyphs[i - 1] + 1)
            {
                run.Add(WidthOfCode(glyphs[i]));
                i++;
            }

            entries.Add((int)first);
            entries.Add(run);
        }

        return entries;
    }

    /// <summary>The CMap that tells a viewer which character each glyph id stands for, so text can be copied and searched.</summary>
    private string ToUnicodeMap(ushort[] glyphs)
    {
        var mappings = new List<string>();
        foreach (var glyph in glyphs)
        {
            var codePoint = _font.CodePointFor(glyph);
            if (codePoint < 0)
            {
                continue; // .notdef and glyphs no character maps to
            }

            var utf16 = new StringBuilder();
            foreach (var unit in char.ConvertFromUtf32(codePoint))
            {
                utf16.Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
            }

            mappings.Add($"<{glyph:X4}> <{utf16}>");
        }

        var map = new StringBuilder();
        map.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
            .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        // A block holds at most a hundred entries.
        for (var start = 0; start < mappings.Count; start += 100)
        {
            var count = Math.Min(100, mappings.Count - start);
            map.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            for (var i = start; i < start + count; i++)
            {
                map.Append(mappings[i]).Append('\n');
            }

            map.Append("endbfchar\n");
        }

        map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return map.ToString();
    }

    private double Round(double fontUnits) => Math.Round(fontUnits * _scale, 3);

    /// <summary>TrueType has no stem width; the usual weight-based estimate is enough for viewers.</summary>
    private static int EstimateStemV(int weightClass) => (int)Math.Round(50 + Math.Pow(weightClass / 65.0, 2));

    /// <summary>Six uppercase letters that identify this subset, derived deterministically from its contents.</summary>
    private string SubsetTag(ushort[] glyphs)
    {
        var hash = 2166136261u; // FNV-1a
        foreach (var glyph in glyphs)
        {
            hash = unchecked((hash ^ (uint)(glyph & 0xFF)) * 16777619u);
            hash = unchecked((hash ^ (uint)(glyph >> 8)) * 16777619u);
        }

        foreach (var c in _font.PostScriptName)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        Span<char> tag = stackalloc char[6];
        for (var i = 0; i < tag.Length; i++)
        {
            tag[i] = (char)('A' + hash % 26);
            hash /= 26;
        }

        return new string(tag);
    }
}
