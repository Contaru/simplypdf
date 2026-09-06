// Renders the same 80 mm POS receipt with three OFL monospace fonts so they can be compared:
// one landscape page with the three side by side, plus one real-size (80 mm wide) PDF per font.
//
// Usage: dotnet run --project samples/PosReceipt [-- output-directory]

using System.Globalization;
using SimplyPdf;
using SimplyPdf.Samples.PosReceipt;

var outputDirectory = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outputDirectory);

var receipt = new Receipt
{
    Header =
    [
        "ALMACENES UNIVERSAL S.A.S.",
        "NIT 901.260.162-8",
        "RESPONSABLES DE IVA",
        "CARRERA 3 # 9 - 101, NEIVA",
        "TEL. 318 000 00 00",
    ],
    DocumentNumber = "POS-0012345",
    Date = "2026-09-06",
    Time = "10:32",
    Register = "02",
    Cashier = "LAURA G.",
    CustomerName = "TORTA CHANTEL",
    CustomerId = "901832669-5",
    Lines =
    [
        new ReceiptLine { Quantity = 1, Description = "Cuchilla picahielo Oster® 6 aspas 4980", UnitPrice = 40000 },
        new ReceiptLine { Quantity = 2, Description = "Pila alcalina AA x2", UnitPrice = 9450 },
        new ReceiptLine { Quantity = 1, Description = "Extensión eléctrica 3 m", UnitPrice = 15900 },
    ],
    Subtotal = 62857,
    Vat = 11943,
    Total = 74800,
    PaymentMeans = "Efectivo",
    Received = 80000,
    Cude = "9b3f1c2e6d8a4f7b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8091a2b3c4d5e6f708192a3",
    Footer =
    [
        "Resolución DIAN 18764112677944",
        "del 2026-07-16, prefijo POS del 1 al 500000",
        "Documento equivalente electrónico",
        "¡GRACIAS POR SU COMPRA!",
    ],
};

var fontsDirectory = Path.Combine(AppContext.BaseDirectory, "fonts");
(string Label, string File)[] candidates =
[
    ("VT323", "VT323-Regular.ttf"),
    ("Share Tech Mono", "ShareTechMono-Regular.ttf"),
    ("Courier Prime", "CourierPrime-Regular.ttf"),
];

// Side-by-side comparison on one landscape Letter page.
var comparison = new PdfDocument();
comparison.Info.Title = "POS receipt font comparison";
var page = comparison.AddPage(PdfPageSize.Letter.Landscape, PdfMargins.All(0));
var gap = (page.Width - candidates.Length * ReceiptRenderer.PaperWidth) / (candidates.Length + 1);

for (var i = 0; i < candidates.Length; i++)
{
    var font = PdfFont.FromFile(Path.Combine(fontsDirectory, candidates[i].File));
    var left = gap + i * (ReceiptRenderer.PaperWidth + gap);
    var size = ReceiptRenderer.FontSizeFor(page, font);

    page.Font(PdfFont.HelveticaBold, 10)
        .Text($"{candidates[i].Label} — {size.ToString("0.0", CultureInfo.InvariantCulture)} pt, {ReceiptRenderer.Columns} columnas", left, 14, ReceiptRenderer.PaperWidth, TextAlign.Center);

    var bottom = ReceiptRenderer.Draw(page, receipt, font, left, 30);
    page.Rect(left, 30, ReceiptRenderer.PaperWidth, bottom - 30 + 10).Stroke(PdfColor.Gray(0.75)); // paper edge
}

var comparisonPath = Path.Combine(outputDirectory, "receipt-comparison.pdf");
comparison.Save(comparisonPath);
Console.WriteLine($"Wrote {Path.GetFullPath(comparisonPath)} ({new FileInfo(comparisonPath).Length:N0} bytes)");

// One real-size receipt per font: 80 mm wide, as tall as it needs to be.
foreach (var (label, file) in candidates)
{
    var font = PdfFont.FromFile(Path.Combine(fontsDirectory, file));
    var path = Path.Combine(outputDirectory, $"receipt-{label.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}.pdf");
    ReceiptRenderer.RenderRealSize(receipt, font).Save(path);
    Console.WriteLine($"Wrote {Path.GetFullPath(path)} ({new FileInfo(path).Length:N0} bytes)");
}
