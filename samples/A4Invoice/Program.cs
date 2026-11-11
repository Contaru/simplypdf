// Renders the graphical representation of invoice FSII 1305 so it can be compared side by side
// with the PDF the HTML/wkhtmltopdf implementation produced for the same document.
//
// Usage: dotnet run --project samples/A4Invoice [-- output.pdf [fontsDirectory]]
//        fontsDirectory holds LiberationSans-Regular.ttf and LiberationSans-Bold.ttf (defaults to
//        the fonts folder next to the executable). Without them the sample draws in Helvetica,
//        which has the same advance widths.

using SimplyPdf;
using SimplyPdf.Samples.A4Invoice;

var outputPath = args.Length > 0 ? args[0] : "FSII1305.pdf";
var fontsDirectory = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "fonts");

var invoice = new Invoice
{
    Issuer = new Issuer
    {
        Name = "ALMACENES UNIVERSAL S.A.S.",
        Nit = "901260162",
        CheckDigit = "8",
        Address = "Carrera 3 # 9 - 101",
        Phone = "(318) 0000000",
        City = "Neiva - Colombia",
        Email = "info@almacenesuniversal.com",
        Website = "almacenesuniversal.com",
        TaxProfile =
            "Responsable de IVA - Actividad Económica 4754  Comercio al por menor de electrodomésticos y gasodomesticos " +
            "de uso doméstico, muebles y equipos de iluminación Tarifa ",
    },
    Prefix = "FSII",
    Number = 1305,
    Date = new DateOnly(2026, 11, 3),
    Time = new TimeOnly(17, 26, 5),
    DueDate = new DateOnly(2026, 12, 3),
    Customer = new Customer
    {
        Name = "NEWELL BRANDS DE COLOMBIA SAS",
        Nit = "860002595",
        CheckDigit = "1",
        Address = "Calle 26 No 69B - 45 Of. 503-505",
        Phone = "(57) 7700000",
        City = "Neiva - Colombia",
    },
    Lines =
    [
        new InvoiceLine { Name = "garantias-oster-SERVICIOS TÉCNICOS POR GARANTIAS", Quantity = 1, Total = 247210.60m },
    ],
    Subtotal = 207740.00m,
    VatRate = 19,
    Vat = 39470.60m,
    Total = 247210.60m,
    TotalInWords = "Doscientos cuarenta y siete mil doscientos diez pesos m/cte con sesenta cent.",
    PaymentForm = "Crédito",
    PaymentMeans = "Otro",
    Note = "SOMOS AUTORRETENEDORES ICA NEIVA SEGÚN ACUERDO 028 DE 2018 ART. 637",
    Resolution = new Resolution
    {
        AuthorizationNumber = "18764115804861",
        ApprovedOn = "20260917",
        Prefix = "FSII",
        RangeFrom = 1293,
        RangeTo = 3000,
        ValidityMonths = 24,
    },
    Cufe = "01349e756c2cf82da9872c9bb687193db82f86d6ddb85b6229fe152bbae78def641d613a7e4fb52dcf17fa86c2345a05",
};

var face = Typeface.LiberationSans(fontsDirectory);
if (face is null)
{
    Console.WriteLine($"Liberation Sans not found in {fontsDirectory}: drawing in Helvetica.");
    face = Typeface.Helvetica;
}

var logo = PdfImage.FromFile(Path.Combine(AppContext.BaseDirectory, "logo.png"));
var sideNote = PdfImage.FromFile(Path.Combine(AppContext.BaseDirectory, "side-note.png"));
InvoiceRenderer.Render(invoice, face, logo, sideNote).Save(outputPath);

Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)} ({new FileInfo(outputPath).Length:N0} bytes) with {face.Regular.Name}");
