using System.Globalization;

namespace SimplyPdf.Samples.PosInvoice;

/// <summary>
/// The classic Colombian retail layout (the one Alkosto, Éxito and friends print): 44 columns,
/// dashed and double separators, one line per item plus description and discount lines, VAT
/// summary, delivery block, legal footer, savings, QR and CUFE.
/// </summary>
public static class PosInvoiceRenderer
{
    public const int Columns = 44;

    public static PdfDocument Render(PosInvoice invoice, PdfFont font)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var roll = new Roll(font, Columns);

        roll.Center(invoice.Website)
            .Center(invoice.IssuerName)
            .Center($"Nit. {invoice.IssuerNit}")
            .Blank()
            .Left($"Factura Electronica de Venta: {invoice.Number}")
            .Left($"Pedido Int.: {invoice.InternalOrder}")
            .Left(Pair($"Pedido No.: {invoice.OrderNumber}", $"Caja: {invoice.Register}"))
            .Left(Pair($"Fecha : {invoice.Date}", $"Hora: {invoice.Time}"))
            .Left(Pair($"Cajero : {invoice.Cashier}", $"Local: {invoice.Store}"))
            .Left($"N:      {invoice.CustomerId}")
            .Left($"Cliente: {invoice.CustomerName}")
            .Left($"Telefo.: {invoice.CustomerPhone}")
            .Left($"Forma de Pago: {invoice.PaymentForm}")
            .Left($"Medio de Pago: {invoice.PaymentMeans}")
            .Left($"Vendedor: {invoice.Seller}")
            .Left($"Observa: {invoice.Observations}")
            .Separator()
            .Left($"{"#",-3} {"Articulo",-13} {"IVA",4} {"Ipo",3} {"Cant",4} {"Total s/desc",12}")
            .Separator();

        var index = 0;
        foreach (var line in invoice.Lines)
        {
            index++;
            roll.Left($"{index,-3} {line.Code,-13} {line.VatRate,4} {line.ConsumptionTax,3} {line.Quantity,4} {Cop(line.Gross),12}")
                .Left("    " + line.Description);
            if (line.DiscountPercent > 0)
            {
                roll.Left($"Descuento {Percent(line.DiscountPercent),6} %{Cop(line.Discount) + "-",26}");
            }
        }

        roll.Left($"Total lineas factura: {invoice.Lines.Count}")
            .Separator()
            .Left($"{"",5}{"Subtotal",-25}{Cop(invoice.Subtotal),14}")
            .Left($"{"",5}{"Valor IVA",-25}{Cop(invoice.Vat),14}")
            .Left($"{"",5}{"Valor Total",-25}{Cop(invoice.Total),14}")
            .Left($"{invoice.PaymentLabel,-23}${Cop(invoice.Total),20}")
            .Left($"{"",5}{"Tarifa IVA",-12}{"Vr-Base",14}{"Vr-IVA",13}");

        foreach (var vat in invoice.VatTable)
        {
            roll.Left($"{"",7}{vat.Rate,-10}{Cop(vat.Base),14}{Cop(vat.Vat),13}");
        }

        roll.Separator();

        if (invoice.Deliveries.Count > 0)
        {
            roll.Separator('=')
                .Center("Para la entrega de su mercancia,")
                .Center("favor entregar al transportador")
                .Center("esta factura de compra")
                .Center("MERCANCIA PARA DESPACHAR")
                .Blank()
                .Separator()
                .Left($"{"Articulo",-14}{"Cant.",6}{"UN.",7}{"Fecha de Entrega",17}")
                .Separator();

            foreach (var item in invoice.Deliveries)
            {
                roll.Left($"{item.Code,-14}{item.Quantity,6}{item.Unit,7}{item.Date,17}")
                    .Left("    " + item.Description);
            }
        }

        roll.Separator('=');
        foreach (var line in invoice.TaxStatus)
        {
            roll.Center(line);
        }

        roll.Blank()
            .Center("Numeracion Autorizada y/o Habilitada por la DIAN")
            .Center($"Prefijo: {invoice.Prefix} del No. {invoice.RangeFrom} al {invoice.RangeTo}")
            .Center($"Resolucion No {invoice.Resolution} del {invoice.ResolutionDate}.")
            .Blank();

        foreach (var line in invoice.ReturnPolicy)
        {
            roll.Center(line);
        }

        roll.Center(invoice.SoftwareNotice)
            .Blank()
            .Center("USTED SE AHORRO")
            .Center($"${Cop(invoice.Savings)}")
            .Center("Adicionales a los ahorros que")
            .Center("Siempre tiene en Almacenes Universal")
            .Qr(invoice.QrPayload)
            .Center("CUFE:")
            .CenterChunks(invoice.Cufe)
            .Center("REPRESENTACION GRAFICA DE LA FACTURA ELECTRONICA")
            .Blank()
            .Center($"{invoice.Date} {invoice.Time[..5]}");

        return roll.Build($"Factura electronica de venta {invoice.Number}");
    }

    /// <summary>Left text and right text on one line, the right one flush with the last column.</summary>
    private static string Pair(string left, string right)
    {
        var padding = Math.Max(1, Columns - left.Length - right.Length);
        return left + new string(' ', padding) + right;
    }

    /// <summary>Colombian thousands separator, no decimals: 109.900.</summary>
    private static string Cop(long value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

    /// <summary>Colombian decimal comma: 20,00.</summary>
    private static string Percent(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
}
