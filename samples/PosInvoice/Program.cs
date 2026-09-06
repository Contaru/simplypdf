// An electronic sales invoice in receipt form on an 80 mm roll, modelled on the layout large
// Colombian retailers print, rendered with an embedded thermal-look font.
//
// Usage: dotnet run --project samples/PosInvoice [-- output.pdf [font.ttf]]
//        font.ttf defaults to ShareTechMono-Regular.ttf; VT323-Regular.ttf and CourierPrime-Regular.ttf ship too.

using SimplyPdf;
using SimplyPdf.Samples.PosInvoice;

var outputPath = args.Length > 0 ? args[0] : "factura-pos.pdf";
var fontFile = args.Length > 1 ? args[1] : "ShareTechMono-Regular.ttf";
var fontPath = Path.IsPathRooted(fontFile) ? fontFile : Path.Combine(AppContext.BaseDirectory, "fonts", fontFile);

var invoice = new PosInvoice
{
    Website = "www.almacenesuniversal.com",
    IssuerName = "Almacenes Universal S.A.S.",
    IssuerNit = "901260162-8",
    Number = "FE0177205",
    InternalOrder = "20260906-0417",
    OrderNumber = "417",
    Register = "02",
    Date = "2026/09/06",
    Time = "10:32:15",
    Cashier = "LAURA GOMEZ",
    Store = "01",
    CustomerId = "901832669",
    CustomerName = "TORTA CHANTEL",
    CustomerPhone = "3153887080",
    PaymentForm = "CONTADO",
    PaymentMeans = "EFECTIVO",
    Seller = "CARLOS RAMIREZ",
    Observations = ".",
    Lines =
    [
        new PosInvoiceLine { Code = "7709990205118", Description = "Cuchilla picahielo Oster 6 aspas 4980", VatRate = 19, Quantity = 1, UnitPrice = 40000, DiscountPercent = 10 },
        new PosInvoiceLine { Code = "7709992692142", Description = "Ventilador Samurai Ultra Silence Force", VatRate = 19, Quantity = 2, UnitPrice = 215000 },
        new PosInvoiceLine { Code = "7709990100521", Description = "Nevera Haceb ARF 315L CE 2P DA TI", VatRate = 19, Quantity = 1, UnitPrice = 1300000, DiscountPercent = 5 },
    ],
    Subtotal = 1429412,
    Vat = 271588,
    Total = 1701000,
    PaymentLabel = "EFECTIVO",
    VatTable = [new VatSummary(19, 1429412, 271588)],
    Deliveries =
    [
        new DeliveryItem("7709992692142", 2, "BOD01", "2026-09-10", "Ventilador Samurai Ultra Silence Force"),
        new DeliveryItem("7709990100521", 1, "BOD01", "2026-09-10", "Nevera Haceb ARF 315L CE 2P DA TI"),
    ],
    TaxStatus =
    [
        "Responsables de I.V.A.",
        "Autorretenedores ICA Neiva",
        "Acuerdo 028 de 2018 Art. 637",
        "Actividad economica 4754",
    ],
    Prefix = "FE01",
    RangeFrom = 75572,
    RangeTo = 100000,
    Resolution = "18764112677944",
    ResolutionDate = "2026/07/16",
    ReturnPolicy =
    [
        "Estimado Cliente, consulta la",
        "politica de cambios y garantias en:",
        "almacenesuniversal.com/garantias",
    ],
    SoftwareNotice = "Software de facturacion electronica: software propio de Almacenes Universal S.A.S. Nit 901260162-8",
    Savings = 69000,
    Cufe = "3f9c1a7e5b2d4c6a8e0f1b3d5a7c9e2b4d6f8a0c1e3b5d7f9a2c4e6b8d0f1a3c5e7b9d2f4a6c8e0b1d3f5a7c9e2b4d6f",
};

var font = PdfFont.FromFile(fontPath);
PosInvoiceRenderer.Render(invoice, font).Save(outputPath);

Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)} ({new FileInfo(outputPath).Length:N0} bytes) with {font.Name}");
