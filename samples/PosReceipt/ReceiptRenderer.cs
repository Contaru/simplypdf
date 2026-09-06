using System.Globalization;
using Net.Codecrete.QrCodeGenerator;

namespace SimplyPdf.Samples.PosReceipt;

/// <summary>
/// Draws a receipt the way an 80 mm thermal printer would: a fixed character grid (42 columns,
/// like Epson's Font A), text-padded columns, dashed separators, everything black.
/// </summary>
public static class ReceiptRenderer
{
    private const double PointsPerMm = 72 / 25.4;

    /// <summary>80 mm paper.</summary>
    public const double PaperWidth = 80 * PointsPerMm;

    /// <summary>Most 80 mm printers cover 72 mm; the rest is the unprintable edge.</summary>
    public const double PrintableWidth = 72 * PointsPerMm;

    /// <summary>Characters per line of Epson Font A (12 x 24 dots at 203 dpi) on 80 mm paper.</summary>
    public const int Columns = 42;

    private const double SideMargin = (PaperWidth - PrintableWidth) / 2;
    private const double TopMargin = 4 * PointsPerMm;
    private const double BottomMargin = 6 * PointsPerMm;

    /// <summary>A single-page document at the real paper size, as tall as the content needs.</summary>
    public static PdfDocument RenderRealSize(Receipt receipt, PdfFont font)
    {
        // Measuring pass: the page height must be known before anything is drawn.
        var measure = new PdfDocument();
        var tall = measure.AddPage(new PdfPageSize(PaperWidth, 4000), PdfMargins.All(0));
        var contentBottom = Draw(tall, receipt, font, 0, 0);

        var doc = new PdfDocument();
        doc.Info.Title = $"Recibo {receipt.DocumentNumber}";
        var page = doc.AddPage(new PdfPageSize(PaperWidth, contentBottom + BottomMargin), PdfMargins.All(0));
        Draw(page, receipt, font, 0, 0);
        return doc;
    }

    /// <summary>Font size at which <see cref="Columns"/> characters of <paramref name="font"/> fill the printable width.</summary>
    public static double FontSizeFor(PdfPage page, PdfFont font)
    {
        page.Font(font, 1);
        return PrintableWidth / Columns / page.WidthOfString("0");
    }

    /// <summary>
    /// Draws the receipt with the paper's top-left corner at (<paramref name="originX"/>, <paramref name="originY"/>)
    /// and returns the y coordinate just below the last line.
    /// </summary>
    public static double Draw(PdfPage page, Receipt receipt, PdfFont font, double originX, double originY)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(receipt);

        var size = FontSizeFor(page, font);
        page.Font(font, size).FillColor(PdfColor.Black);

        var x = originX + SideMargin;
        page.X = x;
        page.Y = originY + TopMargin;
        var centred = new TextOptions { Width = PrintableWidth, Align = TextAlign.Center, LineBreak = false };
        var plain = new TextOptions { Width = PrintableWidth, LineBreak = false };
        var separator = new string('-', Columns);

        foreach (var line in receipt.Header)
        {
            page.Text(line, centred);
        }

        page.Text(separator, plain)
            .Text(Fit($"FACTURA DE VENTA POS {receipt.DocumentNumber}"), plain)
            .Text(Fit($"FECHA: {receipt.Date} {receipt.Time}  CAJA: {receipt.Register}"), plain)
            .Text(Fit($"CAJERO: {receipt.Cashier}"), plain)
            .Text(Fit($"CLIENTE: {receipt.CustomerName}"), plain)
            .Text(Fit($"NIT/CC: {receipt.CustomerId}"), plain)
            .Text(separator, plain)
            .Text("CANT DESCRIPCION                    VALOR", plain);

        foreach (var line in receipt.Lines)
        {
            // 4 + 1 + 26 + 1 + 10 = 42 columns; long descriptions wrap under themselves.
            var chunks = WrapWords(line.Description, 26);
            page.Text($"{line.Quantity,-4} {chunks[0],-26} {Cop(line.Total),10}", plain);
            foreach (var chunk in chunks.Skip(1))
            {
                page.Text($"     {chunk}", plain);
            }

            if (line.Quantity > 1)
            {
                page.Text($"     {line.Quantity} x {Cop(line.UnitPrice)}", plain);
            }
        }

        page.Text(separator, plain)
            .Text(Amount("SUBTOTAL", receipt.Subtotal), plain)
            .Text(Amount("IVA 19%", receipt.Vat), plain)
            .Text(Amount("TOTAL", receipt.Total), plain)
            .Text(Amount(receipt.PaymentMeans.ToUpperInvariant(), receipt.Received), plain)
            .Text(Amount("CAMBIO", receipt.Change), plain)
            .Text(separator, plain);

        DrawQrCode(page, receipt, x);

        page.Text("CUDE:", plain)
            .Text(receipt.Cude, new TextOptions { Width = PrintableWidth })
            .Text(separator, plain);

        foreach (var line in receipt.Footer)
        {
            page.Text(line, new TextOptions { Width = PrintableWidth, Align = TextAlign.Center });
        }

        return page.Y;
    }

    private static void DrawQrCode(PdfPage page, Receipt receipt, double x)
    {
        var qr = QrCode.EncodeText($"https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey={receipt.Cude}", QrCode.Ecc.Medium);
        const double module = 1.6;
        var side = qr.Size * module;
        var left = x + (PrintableWidth - side) / 2;
        var top = page.Y + 4;

        for (var row = 0; row < qr.Size; row++)
        {
            for (var col = 0; col < qr.Size; col++)
            {
                if (qr.GetModule(col, row))
                {
                    page.Rect(left + col * module, top + row * module, module, module);
                }
            }
        }

        page.Fill(PdfColor.Black);
        page.Y = top + side + 6;
    }

    private static string Amount(string label, long value) => $"{label,-31} {Cop(value),10}";

    /// <summary>Colombian thousands separator, no decimals: 40.000.</summary>
    private static string Cop(long value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

    private static string Fit(string text) => text.Length <= Columns ? text : text[..Columns];

    private static List<string> WrapWords(string text, int width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (candidate.Length <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            current = word.Length <= width ? word : word[..width];
        }

        lines.Add(current);
        return lines;
    }
}
