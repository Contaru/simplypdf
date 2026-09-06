using System.Globalization;
using Net.Codecrete.QrCodeGenerator;

namespace SimplyPdf.Samples.DianInvoice;

/// <summary>
/// Graphical representation of a Colombian electronic invoice (DIAN). A one-to-one port of the
/// pdfkit layout used by the Node.js version: same page, same absolute coordinates, same fonts.
/// </summary>
public static class InvoiceRenderer
{
    private const string IssuerNit = "901260162";
    private static readonly PdfColor Grey = "#e3e3e3";

    public static PdfDocument Render(Invoice invoice, PdfImage logo)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var doc = new PdfDocument();
        doc.Info.Title = "Factura electronica de venta";
        doc.Info.Author = "Almacenes Universal S.A.S.";

        // pdfkit was given size [792, 612] *and* layout "landscape", which it resolves to portrait Letter.
        var page = doc.AddPage(PdfPageSize.Letter, PdfMargins.All(25));

        DrawFrame(page);
        DrawHeader(page, invoice, logo);
        DrawDates(page, invoice);
        DrawCustomer(page, invoice);
        DrawLines(page, invoice);
        DrawTotals(page, invoice);
        DrawPaymentAndNotes(page, invoice);
        DrawFooter(page, invoice);
        DrawSideNote(page);

        return doc;
    }

    private static void DrawFrame(PdfPage page)
    {
        // Grey backgrounds: notes box, customer labels, date labels, table header, totals labels.
        page.Rect(30, 690, 550, 60)
            .Rect(30, 130, 70, 54)
            .Rect(250, 148, 70, 36)
            .Rect(470, 69, 110, 20)
            .Rect(470, 108, 110, 20)
            .Rect(470, 147, 110, 20)
            .Rect(30, 190, 550, 20)
            .Rect(424, 555, 66, 80)
            .Fill(Grey);

        // Outlines: payment box, item table columns, page frame, customer rows, dates, totals.
        page.Rect(30, 555, 380, 80)
            .Rect(30, 190, 40, 355)
            .Rect(70, 190, 50, 355)
            .Rect(120, 190, 200, 355)
            .Rect(320, 190, 52, 355)
            .Rect(372, 190, 104, 355)
            .Rect(476, 190, 104, 355)
            .Rect(20, 20, 570, 750)
            .Rect(30, 130, 430, 18)
            .Rect(30, 148, 430, 18)
            .Rect(30, 166, 430, 18)
            .Rect(470, 70, 110, 114)
            .Rect(424, 555, 155, 80)
            .Rect(490, 555, 89, 20)
            .Rect(490, 555, 89, 40)
            .Rect(490, 555, 89, 60)
            .Rect(490, 555, 89, 80)
            .Stroke(Grey)
            .Fill(PdfColor.Black); // no path pending: only selects the text colour
    }

    private static void DrawHeader(PdfPage page, Invoice invoice, PdfImage logo)
    {
        page.Image(logo, 40, 45, width: 160);

        var centred = new TextOptions { Align = TextAlign.Center };
        page.Font(StandardFont.HelveticaBold, 10)
            .Text("ALMACENES UNIVERSAL S.A.S.", 0, 36, centred)
            .Font(StandardFont.Helvetica, 8)
            .Text("RESPONSABLES DE IVA", centred)
            .Text("Nit 901.260.162-8", centred)
            .Text("CARRERA 3 # 9 - 101", centred)
            .Text("Neiva - Tel. 318 000 00 00", centred)
            .Text("info@almacenesuniversal.com", centred)
            .Text("almacenesuniversal.com", centred);

        DrawQrCode(page, invoice, x: 375, y: 25);

        page.Font(StandardFont.HelveticaBold, 9)
            .Text("FACTURA ELECTRONICA DE VENTA", 465, 30, new TextOptions { Width = 125, Align = TextAlign.Center })
            .Font(StandardFont.Helvetica)
            .Text(invoice.Number, new TextOptions { Width = 125, Align = TextAlign.Center })
            .Font(StandardFont.HelveticaBold, 8)
            .Text("REPRESENTACIÓN GRÁFICA FACTURA ELECTRÓNICA", 0, 117, centred);
    }

    /// <summary>
    /// The DIAN QR payload, drawn as vector squares (1 pt per module, 4-module quiet zone) — the
    /// same geometry the PNG at scale 0.25 had, but crisper and without an image decoder.
    /// </summary>
    private static void DrawQrCode(PdfPage page, Invoice invoice, double x, double y)
    {
        var payload = string.Join('\n',
            $"NumFac:{invoice.Number}",
            $"FecFac:{invoice.Date}",
            $"HorFac:{invoice.Time}-05:00",
            $"NitFac:{IssuerNit}",
            $"DocAdq:{invoice.Customer.IdNumber}",
            $"ValFac:{invoice.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"ValIva:{invoice.Vat.ToString("0.00", CultureInfo.InvariantCulture)}",
            "ValOtroIm:0.00",
            $"ValTolFac:{invoice.Total.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"CUFE:{invoice.Cufe}",
            // Production catalogue. The habilitación host (catalogo-vpfe-hab) only knows test documents.
            $"https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey={invoice.Cufe}");

        var qr = QrCode.EncodeText(payload, QrCode.Ecc.Medium);
        const int quietZone = 4;
        for (var row = 0; row < qr.Size; row++)
        {
            for (var col = 0; col < qr.Size; col++)
            {
                if (qr.GetModule(col, row))
                {
                    page.Rect(x + quietZone + col, y + quietZone + row, 1, 1);
                }
            }
        }

        page.Fill(PdfColor.Black);
    }

    private static void DrawDates(PdfPage page, Invoice invoice)
    {
        page.Font(StandardFont.HelveticaBold, 8)
            .Text("Fecha de Generación", 485, 75)
            .Text("Fecha de Expedición", 485, 115)
            .Text("Fecha de Vencimiento", 483, 155)
            .Font(StandardFont.Helvetica)
            .Text($"{invoice.Date}, {invoice.Time}", 492, 95)
            .Text($"{invoice.Date}, {invoice.Time}", 492, 135)
            .Text(invoice.Date, 505, 173);
    }

    private static void DrawCustomer(PdfPage page, Invoice invoice)
    {
        var customer = invoice.Customer;
        page.Font(StandardFont.HelveticaBold, 9)
            .Text("Nombre", 40, 136)
            .Text(customer.IdType.ToUpperInvariant(), 40, 154)
            .Text("Dirección", 40, 171)
            .Text("Teléfono", 255, 154)
            .Text("Ciudad", 255, 171)
            .Font(StandardFont.Helvetica)
            .Text(customer.Name, 110, 136)
            .Text($"{customer.IdNumber}-{customer.CheckDigit}", 110, 154)
            .Text(Truncate(customer.Address, 35), 110, 171)
            .Text(customer.Phone, 330, 154)
            .Text(customer.City, 330, 171);
    }

    private static void DrawLines(PdfPage page, Invoice invoice)
    {
        page.Font(StandardFont.HelveticaBold, 9)
            .Text("Ítem", 40, 196)
            .Text("PLU", 85, 196)
            .Text("Descripción", 200, 196)
            .Text("Cant.", 328, 196)
            .Text("Vr. Unit", 383, 196)
            .Text("Vr. Total", 491, 196);

        // Rows are 20 pt apart from y = 220; the table fits 13 lines, as in the original.
        page.Font(StandardFont.Helvetica, 8);
        var index = 0;
        foreach (var line in invoice.Lines)
        {
            index++;
            var y = 200 + index * 20;
            page.Text(index.ToString(CultureInfo.InvariantCulture), 25, y, width: 50, TextAlign.Center)
                .Text(line.Plu, 70, y, width: 50, TextAlign.Center)
                .Text(Truncate(line.Name, 50), 130, y, width: 210)
                .Text(line.Quantity.ToString(CultureInfo.InvariantCulture), 320, y, width: 50, TextAlign.Center)
                .Text(Cop(line.UnitPriceWithVat), 375, y, width: 100, TextAlign.Right)
                .Text(Cop(line.TotalWithVat), 475, y, width: 100, TextAlign.Right);
        }
    }

    private static void DrawTotals(PdfPage page, Invoice invoice)
    {
        page.Font(StandardFont.HelveticaBold, 8)
            .Text("Total Bruto", 432, 563)
            .Font(StandardFont.Helvetica)
            .Text($"IVA {(invoice.VatFreeDay ? "0" : "19")}% ", 432, 583)
            .Text("Retencion", 432, 602)
            .Font(StandardFont.HelveticaBold)
            .Text("Total a Pagar ", 432, 622)
            .Font(StandardFont.Helvetica)
            .Text(Cop(invoice.Subtotal), 500, 563, width: 75, TextAlign.Right)
            .Text(Cop(invoice.Vat), 500, 583, width: 75, TextAlign.Right)
            .Text(Cop(0), 500, 602, width: 75, TextAlign.Right)
            .Text(Cop(invoice.Total), 500, 622, width: 75, TextAlign.Right);
    }

    private static void DrawPaymentAndNotes(PdfPage page, Invoice invoice)
    {
        page.Font(StandardFont.HelveticaBold, 8)
            .Text("Total Items: ", 35, 560)
            .Font(StandardFont.Helvetica)
            .Text(invoice.Lines.Count.ToString(CultureInfo.InvariantCulture), 85, 560)
            .Font(StandardFont.HelveticaBold)
            .Text("Condiciones de Pago", 35, 574)
            .Font(StandardFont.Helvetica)
            .Text($"FORMA DE PAGO: {invoice.PaymentForm} - MEDIO DE PAGO: {invoice.PaymentMeans}", 35, 588)
            .Font(StandardFont.HelveticaBold)
            .Text("Valor en Letras: ", 35, 602)
            .Font(StandardFont.Helvetica)
            .Text(invoice.TotalInWords, 35, 614, width: 350)
            .Text("OBSERVACIONES: ", 35, 640, width: 350)
            .Text("1. SOMOS AUTORRETENEDORES ICA NEIVA SEGÚN ACUERDO 028 DE 2018 ART. 637", 35, 655, width: 350)
            .Text(invoice.Note is null ? string.Empty : "2. " + Truncate(invoice.Note, 100), 35, 670, width: 550);
    }

    private static void DrawFooter(PdfPage page, Invoice invoice)
    {
        var r = invoice.Resolution;
        var centred = new TextOptions { Align = TextAlign.Center };
        page.Font(StandardFont.Helvetica, 7)
            .Text(
                "A esta factura de venta aplican las normas relativas a la letra de cambio (artículo 5 Ley 1231 de 2008). " +
                "Con esta el Comprador declara haber recibido real y materialmente las mercancías o prestación de servicios descritos en este título - Valor.",
                25, 700, centred)
            .Text(
                $"Número Autorización {r.AuthorizationNumber} aprobado en {r.ValidFrom} hasta {r.ValidTo} prefijo {r.Prefix} " +
                $"desde el número {r.RangeFrom} al {r.RangeTo} Vigencia: 12 Meses.",
                25, 718, centred)
            .Text(
                "Actividad Económica 4754 Comercio al por menor de electrodomésticos y gasodomesticos de uso doméstico, muebles y equipos de iluminación",
                25, 727, centred)
            .Text($"CUFE: {invoice.Cufe}", 25, 737, centred);
    }

    /// <summary>Vertical "generated by" note along the right edge, rotated 90° around (50, 50) like the original.</summary>
    private static void DrawSideNote(PdfPage page)
    {
        page.Save()
            .Rotate(90, 50, 50)
            .Font(StandardFont.Helvetica, 7)
            .Text("GENERADO POR: SOFTWARE PROPIO - ALMACENES UNIVERSAL S.A.S. NIT 901260162-8", 100, -500, new TextOptions { Align = TextAlign.Center })
            .Restore();
    }

    /// <summary>"COP 40,000.00" — what the Node.js build produced (its ICU lacked the es-CO locale). Swap for es-CO formatting when convenient.</summary>
    private static string Cop(decimal value) => "COP " + value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
