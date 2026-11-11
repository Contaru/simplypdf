using System.Globalization;
using Net.Codecrete.QrCodeGenerator;

namespace SimplyPdf.Samples.A4Invoice;

/// <summary>
/// Graphical representation of a Colombian electronic invoice on A4, ported from an HTML original
/// printed with wkhtmltopdf. The page is drawn in the original's CSS pixels under a single
/// <see cref="PdfPage.Scale"/>, so every number below can be read off the stylesheet: borders are
/// 1 unit wide and the fonts are 10, 11, 12, 14 and 16 px.
/// </summary>
public static class InvoiceRenderer
{
    /// <summary>wkhtmltopdf laid the page out 991 px wide and shrank it onto A4: 0.75 pt per px × 793 / 991.</summary>
    private const double PointsPerPixel = 0.75 * 0.800201833;

    private static readonly PdfColor Border = "#cccccc";
    private static readonly PdfColor Band = "#e3e3e3";
    private static readonly PdfColor FooterInk = "#333333";
    private static readonly NumberFormatInfo DotGroups = new() { NumberGroupSeparator = "." };

    public static PdfDocument Render(Invoice invoice, Typeface face, PdfImage logo, PdfImage sideNote)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(face);

        var doc = new PdfDocument();
        doc.Info.Title = $"Factura {invoice.Prefix} {invoice.Number}";
        doc.Info.Author = invoice.Issuer.Name;

        // The original's MediaBox, A4 rounded to whole points.
        var page = doc.AddPage(new PdfPageSize(595, 842), PdfMargins.All(0));
        var text = new Typesetter(page, face);

        page.Save().Scale(PointsPerPixel, PointsPerPixel);

        // border: 1px solid; border-radius: 5px, stroked along the middle of the border.
        page.LineWidth(1).RoundedRect(8.5, 0.5, 974, 1314, 4.5).Stroke(Border);

        DrawHeader(page, text, invoice, logo);
        DrawCustomer(page, text, invoice.Customer);
        DrawDates(page, text, invoice);
        DrawLines(page, text, invoice);
        DrawSummary(text, invoice);
        DrawTotals(page, text, invoice);
        DrawFooter(page, text, invoice);

        // The software-provider legend runs up the right edge; the original embeds it as a picture.
        page.Image(sideNote, 969, 357, width: 15, height: 600);

        page.Restore();
        return doc;
    }

    private static void DrawHeader(PdfPage page, Typesetter text, Invoice invoice, PdfImage logo)
    {
        var issuer = invoice.Issuer;
        page.Image(logo, 102, 37, width: 74, height: 92);

        // The issuer block is centred on x = 296 and 180 px wide, which is what wraps the name.
        var baseline = text.Paragraph([text.Bold(issuer.Name)], left: 206, right: 386, baseline: 38, pitch: 14, size: 12);
        string[] lines =
        [
            $"NIT {Nit(issuer.Nit, issuer.CheckDigit)}", issuer.Address, $"Tel: {issuer.Phone}", issuer.City, issuer.Email, issuer.Website,
        ];
        foreach (var line in lines)
        {
            text.Centre(line, 206, 386, baseline);
            baseline += 14;
        }

        DrawQrCode(page, invoice, x: 458, y: 8, size: 150);

        page.Frame(665, 55, 304, 67).Fill(Border);
        text.Centre("Factura electrónica de venta", 665, 969, 86, size: 16);
        text.Centre($"No. {invoice.Prefix} {invoice.Number}", 665, 969, 103, bold: true, size: 14);
    }

    /// <summary>
    /// The DIAN QR payload as a bitmap of 3 px per module with a 4-module quiet zone, like the one the
    /// original embeds. Built from raw grey pixels, so no PNG encoder is involved.
    /// </summary>
    private static void DrawQrCode(PdfPage page, Invoice invoice, double x, double y, double size)
    {
        var payload = string.Join('\n',
            $"NumFac:{invoice.Prefix}{invoice.Number}",
            $"FecFac:{invoice.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"HorFac:{invoice.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}-05:00",
            $"NitFac:{invoice.Issuer.Nit}",
            $"DocAdq:{invoice.Customer.Nit}",
            $"ValFac:{invoice.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"ValIva:{invoice.Vat.ToString("0.00", CultureInfo.InvariantCulture)}",
            "ValOtroIm:0.00",
            $"ValTolFac:{invoice.Total.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"CUFE:{invoice.Cufe}",
            $"https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey={invoice.Cufe}");

        var qr = QrCode.EncodeText(payload, QrCode.Ecc.Low);
        const int quietZone = 4;
        const int pixelsPerModule = 3;
        var side = (qr.Size + 2 * quietZone) * pixelsPerModule;
        var pixels = new byte[side * side];
        Array.Fill(pixels, (byte)255);

        for (var row = 0; row < qr.Size; row++)
        {
            for (var col = 0; col < qr.Size; col++)
            {
                if (!qr.GetModule(col, row))
                {
                    continue;
                }

                var left = (quietZone + col) * pixelsPerModule;
                var top = (quietZone + row) * pixelsPerModule;
                for (var dy = 0; dy < pixelsPerModule; dy++)
                {
                    pixels.AsSpan((top + dy) * side + left, pixelsPerModule).Clear();
                }
            }
        }

        page.Image(PdfImage.FromGray(side, side, pixels), x, y, width: size, height: size);
    }

    private static void DrawCustomer(PdfPage page, Typesetter text, Customer customer)
    {
        page.Rect(20, 185, 119, 21).Rect(20, 206, 119, 21).Rect(20, 227, 119, 20)
            .Rect(367, 206, 71, 21).Rect(367, 227, 71, 20)
            .Fill(Band);
        page.Frame(19, 184, 599, 64)
            .Rect(139, 205, 478, 1).Rect(139, 226, 228, 1).Rect(438, 226, 179, 1)
            .Fill(Border);

        text.Left("Señores", 23, 199, bold: true);
        text.Left(customer.Name, 142, 199, maxWidth: 470);
        text.Left("NIT", 23, 220, bold: true);
        text.Left(Nit(customer.Nit, customer.CheckDigit), 142, 220);
        text.Left("Teléfono", 370, 220, bold: true);
        text.Left(customer.Phone, 441, 220, maxWidth: 171);
        text.Left("Dirección", 23, 241, bold: true);
        text.Left(customer.Address, 142, 241, maxWidth: 220);
        text.Left("Ciudad", 370, 241, bold: true);
        text.Left(customer.City, 441, 241, maxWidth: 171);
    }

    private static void DrawDates(PdfPage page, Typesetter text, Invoice invoice)
    {
        page.Rect(664, 167, 304, 25).Fill(Band);
        page.Frame(663, 166, 306, 101).Rect(664, 191, 304, 1).Rect(967, 167, 1, 25).Fill(Border);
        text.Centre("Fecha y hora Factura", 664, 968, 183, bold: true);

        var issued = $"{Date(invoice.Date)}, {invoice.Time.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        (string Label, string Value, double LabelSize, double LabelBaseline)[] rows =
        [
            ("Generación", issued, 12, 16),
            ("Expedición", issued, 12, 16),
            ("Vencimiento", Date(invoice.DueDate), 10, 15),
        ];

        var y = 192.0;
        for (var i = 0; i < rows.Length; i++)
        {
            var (label, value, labelSize, labelBaseline) = rows[i];
            var height = i < rows.Length - 1 ? 25 : 24; // the last row gives one pixel to the box's border

            page.Rect(664, y, 100, height).Fill(Band);
            page.Rect(664, y + height - 1, 100, 1).Fill(PdfColor.White);
            page.Rect(763, y, 1, height).Rect(967, y, 1, height).Fill(Border);

            text.Centre(label, 664, 763, y + labelBaseline, bold: true, size: labelSize);
            text.Centre(value, 764, 967, y + 16);
            y += height;
        }
    }

    private static void DrawLines(PdfPage page, Typesetter text, Invoice invoice)
    {
        (string Title, double X, double Width)[] columns =
        [
            ("Ítem", 21, 114), ("Nombre producto", 135, 378), ("Cantidad", 513, 198), ("Vr. Total", 711, 256),
        ];

        // A 25 px header and 30 px per line. The reserved area ends at y = 839: 17 lines fit, and the
        // reference has a single one, so anything longer needs a second page this sample does not draw.
        const double headerTop = 299;
        const double rowsTop = 324;
        const double rowHeight = 30;
        var rowsHeight = rowHeight * invoice.Lines.Count;

        foreach (var column in columns)
        {
            page.Rect(column.X, headerTop, column.Width, 25);
        }

        page.Fill(Band);

        page.Frame(19, 297, 950, 542)                   // the area reserved for the lines
            .Frame(20, 298, 948, 27 + rowsHeight);      // the table itself
        for (var i = 0; i < columns.Length; i++)
        {
            var (_, x, width) = columns[i];
            page.Rect(x, 323, width, 1).Rect(x + width - 1, headerTop, 1, 25);
            if (i < columns.Length - 1)
            {
                page.Rect(x + width - 1, rowsTop, 1, rowsHeight);
            }
        }

        page.Fill(Border);

        foreach (var (title, x, width) in columns)
        {
            text.Centre(title, x, x + width - 1, 315, bold: true);
        }

        // Each cell sits on its own baseline in the original; the offsets below are the row's.
        var y = rowsTop;
        var index = 0;
        foreach (var line in invoice.Lines)
        {
            index++;
            text.Centre(index.ToString(CultureInfo.InvariantCulture), 21, 134, y + 19);
            text.Left(line.Name, 141, y + 17, maxWidth: 365);
            text.Right(line.Quantity.ToString("N2", CultureInfo.InvariantCulture), 705, y + 16);
            text.Right(Money(line.Total), 959, y + 19);
            y += rowHeight;
        }
    }

    private static void DrawSummary(Typesetter text, Invoice invoice)
    {
        text.Left("Total items:", 19, 860, bold: true);
        text.Left(invoice.Lines.Count.ToString(CultureInfo.InvariantCulture), 89, 860);

        text.Left("Valor en Letras:", 19, 888, bold: true);
        text.Left(invoice.TotalInWords, 29, 902, maxWidth: 620);

        text.Left("Forma de pago:", 19, 930, bold: true);
        text.Left(invoice.PaymentForm, 29, 944);

        text.Left("Medio de pago:", 19, 972, bold: true);
        var dueDate = invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        text.Left($"{invoice.PaymentMeans} - {invoice.PaymentForm} - Cuota No. 001 vence el {dueDate} por", 32, 989);
        text.Left("$", 464, 989);
        text.Right(Money(invoice.Total), 595, 989);

        text.Left("Observaciones:", 19, 1034, bold: true);
        text.Left(invoice.Note, 29, 1048, maxWidth: 620);
    }

    private static void DrawTotals(PdfPage page, Typesetter text, Invoice invoice)
    {
        (string Label, decimal Value, bool LabelBold, bool Highlight, double Height)[] rows =
        [
            ("Total Bruto", invoice.Subtotal, true, false, 32),
            ($"IVA {invoice.VatRate}%", invoice.Vat, false, false, 31),
            ("Total a Pagar", invoice.Total, true, true, 31),
        ];

        const double left = 662;
        const double middle = 815;
        const double right = 968;
        const double top = 849;

        var y = top;
        foreach (var row in rows)
        {
            if (row.Highlight)
            {
                page.Rect(left, y, right - left + 1, row.Height).Fill(Band);
            }

            y += row.Height;
        }

        page.Rect(left, top, right - left + 1, 1);
        y = top;
        foreach (var row in rows)
        {
            page.Rect(left, y, 1, row.Height).Rect(middle, y, 1, row.Height).Rect(right, y, 1, row.Height)
                .Rect(left, y + row.Height - 1, right - left + 1, 1);
            y += row.Height;
        }

        page.Fill(Border);

        y = top;
        foreach (var row in rows)
        {
            var baseline = y + row.Height - 12;
            text.Left(row.Label, 668, baseline, bold: row.LabelBold);
            text.Right(Money(row.Value), 960, baseline, bold: row.Highlight);
            y += row.Height;
        }
    }

    private static void DrawFooter(PdfPage page, Typesetter text, Invoice invoice)
    {
        page.Rect(19, 1209, 950, 95).Fill(Band);
        text.Color = FooterInk;

        var r = invoice.Resolution;
        TextRun[] legal =
        [
            "A esta factura de venta aplican las normas relativas a la letra de cambio (artículo 5 Ley 1231 de 2008). " +
            "Con esta el Comprador declara haber recibido real y materialmente las mercancías o prestación de servicios " +
            "descritos en este título - Valor. ",
            text.Bold($"Número Autorización Electrónica {r.AuthorizationNumber} aprobado en {r.ApprovedOn} prefijo {r.Prefix} " +
                $"desde el número {r.RangeFrom} al {r.RangeTo} Vigencia: {r.ValidityMonths} Meses"),
        ];

        // The box is 950 px wide with 25 px of padding; every line is centred in it, 13 px apart.
        var baseline = text.Paragraph(legal, left: 44, right: 944, baseline: 1234, pitch: 13, size: 11);
        baseline = text.Paragraph([invoice.Issuer.TaxProfile], 44, 944, baseline, 13, 11);
        text.Paragraph([text.Bold("CUFE: "), invoice.Cufe + " "], 44, 944, baseline, 13, 11);
    }

    /// <summary>Appends a 1 px CSS border just inside the box as four strips, to be filled like the original's.</summary>
    private static PdfPage Frame(this PdfPage page, double x, double y, double width, double height) =>
        page.Rect(x, y, width, 1).Rect(x, y + height - 1, width, 1).Rect(x, y, 1, height).Rect(x + width - 1, y, 1, height);

    /// <summary>"901260162" and "8" become "901.260.162-8".</summary>
    private static string Nit(string number, string checkDigit) =>
        $"{long.Parse(number, CultureInfo.InvariantCulture).ToString("#,0", DotGroups)}-{checkDigit}";

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Places text the way the stylesheet does: by its baseline, with a fixed pitch between lines.</summary>
    private sealed class Typesetter(PdfPage page, Typeface face)
    {
        /// <summary>Colour of the text drawn from now on.</summary>
        public PdfColor Color { get; set; } = PdfColor.Black;

        public void Left(string text, double x, double baseline, bool bold = false, double size = 12, double? maxWidth = null) =>
            Draw(maxWidth is { } width ? Font(bold).Fit(text, size, width) : text, x, baseline, bold, size);

        public void Centre(string text, double left, double right, double baseline, bool bold = false, double size = 12) =>
            Draw(text, left, baseline, bold, size, right - left, TextAlign.Center);

        public void Right(string text, double right, double baseline, bool bold = false, double size = 12) =>
            Draw(text, 0, baseline, bold, size, right, TextAlign.Right);

        /// <summary>A run in the bold face; plain strings are runs in the regular one.</summary>
        public TextRun Bold(string text) => new(text, face.Bold);

        /// <summary>
        /// Draws runs of regular and bold text as one paragraph, wrapped and centred between
        /// <paramref name="left"/> and <paramref name="right"/> with its baselines
        /// <paramref name="pitch"/> apart. Returns the baseline that follows the last line.
        /// </summary>
        public double Paragraph(IReadOnlyList<TextRun> runs, double left, double right, double baseline, double pitch, double size)
        {
            var top = baseline - face.Ascent * size;
            var options = new TextOptions
            {
                Width = right - left,
                Align = TextAlign.Center,
                LineGap = pitch - face.Regular.LineHeight(size, includeGap: true), // the stylesheet's line pitch
            };

            page.FillColor(Color).Font(face.Regular, size).Text(runs, left, top, options);
            return baseline + Math.Round((page.Y - top) / pitch) * pitch;
        }

        private PdfFont Font(bool bold) => bold ? face.Bold : face.Regular;

        /// <summary>One line, never wrapped; with a <paramref name="width"/> it is aligned inside that box.</summary>
        private void Draw(string text, double x, double baseline, bool bold, double size, double? width = null, TextAlign align = TextAlign.Left)
        {
            if (text.Length > 0)
            {
                var options = new TextOptions { Width = width, Align = align, LineBreak = false };
                page.FillColor(Color).Font(Font(bold), size).Text(text, x, baseline - face.Ascent * size, options);
            }
        }
    }
}
