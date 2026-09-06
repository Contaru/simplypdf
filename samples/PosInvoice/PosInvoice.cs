namespace SimplyPdf.Samples.PosInvoice;

/// <summary>An electronic sales invoice in point-of-sale (receipt) form. Amounts in COP without decimals.</summary>
public sealed class PosInvoice
{
    public required string Website { get; init; }

    public required string IssuerName { get; init; }

    public required string IssuerNit { get; init; }

    public required string Number { get; init; }

    public required string InternalOrder { get; init; }

    public required string OrderNumber { get; init; }

    public required string Register { get; init; }

    public required string Date { get; init; }

    public required string Time { get; init; }

    public required string Cashier { get; init; }

    public required string Store { get; init; }

    public required string CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string CustomerPhone { get; init; }

    public required string PaymentForm { get; init; }

    public required string PaymentMeans { get; init; }

    public required string Seller { get; init; }

    public required string Observations { get; init; }

    public required IReadOnlyList<PosInvoiceLine> Lines { get; init; }

    public required long Subtotal { get; init; }

    public required long Vat { get; init; }

    public required long Total { get; init; }

    public required string PaymentLabel { get; init; }

    public required IReadOnlyList<VatSummary> VatTable { get; init; }

    public IReadOnlyList<DeliveryItem> Deliveries { get; init; } = [];

    public required IReadOnlyList<string> TaxStatus { get; init; }

    public required string Prefix { get; init; }

    public required long RangeFrom { get; init; }

    public required long RangeTo { get; init; }

    public required string Resolution { get; init; }

    public required string ResolutionDate { get; init; }

    public required IReadOnlyList<string> ReturnPolicy { get; init; }

    public required string SoftwareNotice { get; init; }

    public required long Savings { get; init; }

    public required string Cufe { get; init; }

    public string QrPayload => string.Join('\n',
        $"NumFac:{Number}",
        $"FecFac:{Date.Replace('/', '-')}",
        $"HorFac:{Time}-05:00",
        $"NitFac:{IssuerNit.Split('-')[0]}",
        $"DocAdq:{CustomerId}",
        $"ValFac:{Subtotal}.00",
        $"ValIva:{Vat}.00",
        "ValOtroIm:0.00",
        $"ValTolFac:{Total}.00",
        $"CUFE:{Cufe}",
        $"https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey={Cufe}");
}

public sealed class PosInvoiceLine
{
    public required string Code { get; init; }

    public required string Description { get; init; }

    public required int VatRate { get; init; }

    public int ConsumptionTax { get; init; }

    public required int Quantity { get; init; }

    public required long UnitPrice { get; init; }

    public decimal DiscountPercent { get; init; }

    public long Gross => Quantity * UnitPrice;

    public long Discount => (long)Math.Round(Gross * DiscountPercent / 100m, MidpointRounding.AwayFromZero);
}

public sealed record VatSummary(int Rate, long Base, long Vat);

public sealed record DeliveryItem(string Code, int Quantity, string Unit, string Date, string Description);
