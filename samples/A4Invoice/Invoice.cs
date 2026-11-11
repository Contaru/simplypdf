namespace SimplyPdf.Samples.A4Invoice;

/// <summary>The data the graphical representation needs.</summary>
public sealed class Invoice
{
    public required Issuer Issuer { get; init; }

    public required string Prefix { get; init; }

    public required long Number { get; init; }

    public required DateOnly Date { get; init; }

    public required TimeOnly Time { get; init; }

    public required DateOnly DueDate { get; init; }

    public required Customer Customer { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    public required decimal Subtotal { get; init; }

    public required int VatRate { get; init; }

    public required decimal Vat { get; init; }

    public required decimal Total { get; init; }

    public required string TotalInWords { get; init; }

    public required string PaymentForm { get; init; }

    public required string PaymentMeans { get; init; }

    public string Note { get; init; } = string.Empty;

    public required Resolution Resolution { get; init; }

    public required string Cufe { get; init; }
}

public sealed class Issuer
{
    public required string Name { get; init; }

    public required string Nit { get; init; }

    public required string CheckDigit { get; init; }

    public required string Address { get; init; }

    public required string Phone { get; init; }

    public required string City { get; init; }

    public required string Email { get; init; }

    public required string Website { get; init; }

    /// <summary>Tax responsibilities and economic activity, printed verbatim in the footer.</summary>
    public required string TaxProfile { get; init; }
}

public sealed class Customer
{
    public required string Name { get; init; }

    public required string Nit { get; init; }

    public required string CheckDigit { get; init; }

    public required string Address { get; init; }

    public required string Phone { get; init; }

    public required string City { get; init; }
}

public sealed class InvoiceLine
{
    public required string Name { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal Total { get; init; }
}

public sealed class Resolution
{
    public required string AuthorizationNumber { get; init; }

    public required string ApprovedOn { get; init; }

    public required string Prefix { get; init; }

    public required long RangeFrom { get; init; }

    public required long RangeTo { get; init; }

    public required int ValidityMonths { get; init; }
}
