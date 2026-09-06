using Net.Codecrete.QrCodeGenerator;

namespace SimplyPdf.Samples.PosInvoice;

/// <summary>
/// A continuous 80 mm paper roll: lines are queued on a fixed character grid and the page is
/// created only when everything is known, exactly as tall as the content. Text is one font at
/// one size — a thermal printer has no other.
/// </summary>
public sealed class Roll
{
    private const double PointsPerMm = 72 / 25.4;

    private readonly PdfFont _font;
    private readonly double _paperWidth;
    private readonly double _printableWidth;
    private readonly double _left;
    private readonly double _size;
    private readonly double _lineHeight;
    private readonly List<(double Height, Action<PdfPage, double> Draw)> _blocks = [];

    /// <param name="columns">Characters per line; the font is sized so they span the printable width.</param>
    public Roll(PdfFont font, int columns, double paperWidthMm = 80, double printableWidthMm = 72)
    {
        ArgumentNullException.ThrowIfNull(font);
        _font = font;
        Columns = columns;
        _paperWidth = paperWidthMm * PointsPerMm;
        _printableWidth = printableWidthMm * PointsPerMm;
        _left = (_paperWidth - _printableWidth) / 2;
        _size = _printableWidth / columns / font.WidthOfString("0", 1);
        _lineHeight = font.LineHeight(_size, includeGap: true);
    }

    public int Columns { get; }

    /// <summary>Font size in points that makes <see cref="Columns"/> characters fill the printable width.</summary>
    public double FontSize => _size;

    public Roll Left(string text) => Line(text, TextAlign.Left);

    public Roll Right(string text) => Line(text, TextAlign.Right);

    /// <summary>Centres <paramref name="text"/>, wrapping at spaces when it exceeds the grid.</summary>
    public Roll Center(string text)
    {
        foreach (var line in WrapWords(text))
        {
            Line(line, TextAlign.Center);
        }

        return this;
    }

    /// <summary>Splits an unbroken string (a CUFE, a URL) into grid-sized pieces, each centred.</summary>
    public Roll CenterChunks(string text)
    {
        for (var i = 0; i < text.Length; i += Columns)
        {
            Line(text.Substring(i, Math.Min(Columns, text.Length - i)), TextAlign.Center);
        }

        return this;
    }

    public Roll Blank(int lines = 1)
    {
        for (var i = 0; i < lines; i++)
        {
            _blocks.Add((_lineHeight, (_, _) => { }));
        }

        return this;
    }

    public Roll Separator(char c = '-') => Left(new string(c, Columns));

    /// <summary>A centred QR code drawn as vector squares.</summary>
    public Roll Qr(string payload, double moduleSize = 1.5, double marginAbove = 4, double marginBelow = 4)
    {
        var qr = QrCode.EncodeText(payload, QrCode.Ecc.Medium);
        var side = qr.Size * moduleSize;
        var left = _left + (_printableWidth - side) / 2;

        _blocks.Add((marginAbove + side + marginBelow, (page, y) =>
        {
            var top = y + marginAbove;
            for (var row = 0; row < qr.Size; row++)
            {
                for (var col = 0; col < qr.Size; col++)
                {
                    if (qr.GetModule(col, row))
                    {
                        page.Rect(left + col * moduleSize, top + row * moduleSize, moduleSize, moduleSize);
                    }
                }
            }

            page.Fill(PdfColor.Black);
        }));

        return this;
    }

    /// <summary>Creates the single-page document, as tall as the queued content.</summary>
    public PdfDocument Build(string title, double topMarginMm = 4, double bottomMarginMm = 8)
    {
        var doc = new PdfDocument();
        doc.Info.Title = title;

        var top = topMarginMm * PointsPerMm;
        var height = top + _blocks.Sum(b => b.Height) + bottomMarginMm * PointsPerMm;
        var page = doc.AddPage(new PdfPageSize(_paperWidth, height), PdfMargins.All(0));
        page.Font(_font, _size).FillColor(PdfColor.Black);

        var y = top;
        foreach (var (blockHeight, draw) in _blocks)
        {
            draw(page, y);
            y += blockHeight;
        }

        return doc;
    }

    private Roll Line(string text, TextAlign align)
    {
        var fitted = text.Length <= Columns ? text : text[..Columns];
        var options = new TextOptions { Width = _printableWidth, Align = align, LineBreak = false };
        _blocks.Add((_lineHeight, (page, y) => page.Text(fitted, _left, y, options)));
        return this;
    }

    private List<string> WrapWords(string text)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (candidate.Length <= Columns)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            current = word;
        }

        lines.Add(current);
        return lines;
    }
}
