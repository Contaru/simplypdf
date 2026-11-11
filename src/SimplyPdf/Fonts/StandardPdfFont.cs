using System.Text;
using SimplyPdf.Internal;
using SimplyPdf.Text;

namespace SimplyPdf.Fonts;

/// <summary>One of the standard 14 fonts: metrics from the AFM tables, nothing embedded, WinAnsi codes.</summary>
internal sealed class StandardPdfFont : PdfFont
{
    private readonly FontMetricsData _metrics;

    public StandardPdfFont(StandardFont font)
    {
        Font = font;
        _metrics = StandardFontMetrics.Get(font);
    }

    public StandardFont Font { get; }

    public override string Name => _metrics.PostScriptName;

    public override bool IsEmbedded => false;

    internal override double Ascender => _metrics.Ascender;

    internal override double Descender => _metrics.Descender;

    internal override double LineGap => _metrics.LineGap;

    internal override bool WordSpacingOperatorApplies => true;

    internal override TextCode[] Encode(string text)
    {
        var bytes = WinAnsiEncoding.Encode(text);
        var codes = new TextCode[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            codes[i] = new TextCode(bytes[i], bytes[i] == (byte)' ');
        }

        return codes;
    }

    internal override double WidthOfCode(ushort code) => code < _metrics.Widths.Length ? _metrics.Widths[code] : 0;

    internal override void AppendShow(StringBuilder content, ReadOnlySpan<TextCode> codes, double wordSpacing, double size)
    {
        var bytes = new byte[codes.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            bytes[i] = (byte)codes[i].Value;
        }

        PdfFormat.AppendLiteral(content, bytes);
        content.Append(" Tj\n");
    }

    internal override PdfObjectRef WriteTo(PdfWriter writer, IReadOnlySet<ushort> usedCodes) =>
        writer.Add(new PdfDictionary
        {
            ["Type"] = new PdfName("Font"),
            ["Subtype"] = new PdfName("Type1"),
            ["BaseFont"] = new PdfName(_metrics.PostScriptName),
            ["Encoding"] = new PdfName("WinAnsiEncoding"),
        });
}
