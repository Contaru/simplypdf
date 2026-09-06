using SimplyPdf.Internal;

namespace SimplyPdf.Fonts;

/// <summary>One of the standard 14 fonts: metrics from the AFM tables, nothing embedded.</summary>
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

    internal override double WidthOfCode(byte code) => _metrics.Widths[code];

    internal override PdfObjectRef WriteTo(PdfWriter writer, IReadOnlySet<byte> usedCodes) =>
        writer.Add(new PdfDictionary
        {
            ["Type"] = new PdfName("Font"),
            ["Subtype"] = new PdfName("Type1"),
            ["BaseFont"] = new PdfName(_metrics.PostScriptName),
            ["Encoding"] = new PdfName("WinAnsiEncoding"),
        });
}
