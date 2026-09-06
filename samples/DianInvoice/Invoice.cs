namespace SimplyPdf.Samples.DianInvoice;

/// <summary>The data the graphical representation needs. Mirrors the fields the Node.js version reads.</summary>
public sealed class Invoice
{
    public required string Number { get; init; }

    public required string Date { get; init; }

    public required string Time { get; init; }

    public required Customer Customer { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    public required decimal Subtotal { get; init; }

    public required decimal Vat { get; init; }

    public required decimal Total { get; init; }

    public bool VatFreeDay { get; init; }

    public required string PaymentForm { get; init; }

    public required string PaymentMeans { get; init; }

    public required string TotalInWords { get; init; }

    public string? Note { get; init; }

    public required Resolution Resolution { get; init; }

    public required string Cufe { get; init; }
}

public sealed class Customer
{
    public required string IdType { get; init; }

    public required string IdNumber { get; init; }

    public required string CheckDigit { get; init; }

    public required string Name { get; init; }

    public required string Address { get; init; }

    public required string Phone { get; init; }

    public required string City { get; init; }
}

public sealed class InvoiceLine
{
    public required string Plu { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPriceWithVat { get; init; }

    public required decimal TotalWithVat { get; init; }
}

public sealed class Resolution
{
    public required string AuthorizationNumber { get; init; }

    public required string ValidFrom { get; init; }

    public required string ValidTo { get; init; }

    public required string Prefix { get; init; }

    public required long RangeFrom { get; init; }

    public required long RangeTo { get; init; }
}
