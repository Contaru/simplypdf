namespace SimplyPdf.Samples.PosReceipt;

/// <summary>What a point-of-sale receipt carries. Amounts are in COP without decimals, as on the paper slip.</summary>
public sealed class Receipt
{
    public required IReadOnlyList<string> Header { get; init; }

    public required string DocumentNumber { get; init; }

    public required string Date { get; init; }

    public required string Time { get; init; }

    public required string Register { get; init; }

    public required string Cashier { get; init; }

    public required string CustomerName { get; init; }

    public required string CustomerId { get; init; }

    public required IReadOnlyList<ReceiptLine> Lines { get; init; }

    public required long Subtotal { get; init; }

    public required long Vat { get; init; }

    public required long Total { get; init; }

    public required string PaymentMeans { get; init; }

    public required long Received { get; init; }

    public long Change => Received - Total;

    public required string Cude { get; init; }

    public required IReadOnlyList<string> Footer { get; init; }
}

public sealed class ReceiptLine
{
    public required int Quantity { get; init; }

    public required string Description { get; init; }

    public required long UnitPrice { get; init; }

    public long Total => Quantity * UnitPrice;
}
