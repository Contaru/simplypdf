using SimplyPdf.Fonts.TrueType;
using SimplyPdf.Internal;

namespace SimplyPdf.Fonts;

/// <summary>
/// A TrueType font embedded as a simple font (<c>/Subtype /TrueType</c>, WinAnsiEncoding,
/// <c>/FontFile2</c>). Viewers resolve each code through the encoding's glyph names to Unicode
/// and then through the font's cmap, which is exactly how widths are computed here.
/// </summary>
internal sealed class TrueTypePdfFont : PdfFont
{
    private const int FirstChar = 32;
    private const int LastChar = 255;

    private readonly TrueTypeFont _font;
    private readonly double _scale;
    private readonly ushort[] _glyphs = new ushort[256];
    private readonly double[] _widths = new double[256];
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

        for (var code = FirstChar; code <= LastChar; code++)
        {
            var glyph = font.GlyphFor(WinAnsiEncoding.ToUnicode((byte)code));
            _glyphs[code] = glyph;
            _widths[code] = Math.Round(font.AdvanceWidth(glyph) * _scale, 3);
        }

        Ascender = font.Ascender * _scale;
        Descender = font.Descender * _scale;
        LineGap = font.LineGap * _scale;
    }

    public override string Name => _font.PostScriptName;

    public override bool IsEmbedded => true;

    internal override double Ascender { get; }

    internal override double Descender { get; }

    internal override double LineGap { get; }

    internal override double WidthOfCode(byte code) => _widths[code];

    internal override PdfObjectRef WriteTo(PdfWriter writer, IReadOnlySet<byte> usedCodes)
    {
        byte[] program;
        string baseFont;
        if (_subset)
        {
            var glyphs = usedCodes.Select(code => _glyphs[code]).Where(glyph => glyph != 0).Distinct();
            program = TrueTypeSubsetter.Subset(_font, glyphs);
            baseFont = SubsetTag(usedCodes) + "+" + _font.PostScriptName;
        }
        else
        {
            program = _font.Data;
            baseFont = _font.PostScriptName;
        }

        var fontFile = writer.Add(new PdfDictionary { ["Length1"] = program.Length }, program, compress: true);

        var flags = 32; // Nonsymbolic: codes map through a standard encoding
        if (_font.IsFixedPitch)
        {
            flags |= 1;
        }

        if (_font.ItalicAngle != 0)
        {
            flags |= 64;
        }

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
            ["MissingWidth"] = Math.Round(_font.AdvanceWidth(0) * _scale, 3),
            ["FontFile2"] = fontFile,
        });

        var widths = new object[LastChar - FirstChar + 1];
        for (var code = FirstChar; code <= LastChar; code++)
        {
            widths[code - FirstChar] = _widths[code];
        }

        return writer.Add(new PdfDictionary
        {
            ["Type"] = new PdfName("Font"),
            ["Subtype"] = new PdfName("TrueType"),
            ["BaseFont"] = new PdfName(baseFont),
            ["FirstChar"] = FirstChar,
            ["LastChar"] = LastChar,
            ["Widths"] = widths,
            ["Encoding"] = new PdfName("WinAnsiEncoding"),
            ["FontDescriptor"] = descriptor,
        });
    }

    private double Round(double fontUnits) => Math.Round(fontUnits * _scale, 3);

    /// <summary>TrueType has no stem width; the usual weight-based estimate is enough for viewers.</summary>
    private static int EstimateStemV(int weightClass) => (int)Math.Round(50 + Math.Pow(weightClass / 65.0, 2));

    /// <summary>Six uppercase letters that identify this subset, derived deterministically from its contents.</summary>
    private string SubsetTag(IReadOnlySet<byte> usedCodes)
    {
        var hash = 2166136261u; // FNV-1a
        foreach (var code in usedCodes.Order())
        {
            hash = unchecked((hash ^ code) * 16777619u);
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
