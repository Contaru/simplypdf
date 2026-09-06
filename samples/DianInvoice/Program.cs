// Renders the graphical representation of invoice FE0177192 so it can be compared side by side
// with the PDF the Node.js/pdfkit implementation produced for the same document.
//
// Usage: dotnet run --project samples/DianInvoice [-- output.pdf]

using SimplyPdf;
using SimplyPdf.Samples.DianInvoice;

var outputPath = args.Length > 0 ? args[0] : "FE0177192.pdf";

var invoice = new Invoice
{
    Number = "FE0177192",
    Date = "2026-08-28",
    Time = "16:14:07",
    Customer = new Customer
    {
        IdType = "nit",
        IdNumber = "901832669",
        CheckDigit = "5",
        Name = "TORTA CHANTEL",
        Address = "cra12#7-69",
        Phone = "3153887080",
        City = "Neiva - Huila",
    },
    Lines =
    [
        new InvoiceLine
        {
            Plu = "20511",
            Name = "Cuchilla picahielo Oster® 6 aspas 4980",
            Quantity = 1,
            UnitPriceWithVat = 40000m,
            TotalWithVat = 40000m,
        },
    ],
    Subtotal = 33613.44m,
    Vat = 6386.56m,
    Total = 40000m,
    PaymentForm = "Contado",
    PaymentMeans = "Efectivo",
    TotalInWords = "Cuarenta Mil Pesos",
    Resolution = new Resolution
    {
        AuthorizationNumber = "18764112677944",
        ValidFrom = "2026-07-16",
        ValidTo = "2028-07-16",
        Prefix = "FE01",
        RangeFrom = 75572,
        RangeTo = 100000,
    },
    Cufe = "66921ab5c14d6ed8732e300d2a42c15fde014303bba0c63b374bacce196841ecf12a61d805cd65e35338b8ce93ce2513",
};

var logo = PdfImage.FromFile(Path.Combine(AppContext.BaseDirectory, "logo.png"));
var document = InvoiceRenderer.Render(invoice, logo);
document.Save(outputPath);

Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)} ({new FileInfo(outputPath).Length:N0} bytes)");
