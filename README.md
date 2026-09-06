# SimplyPdf

A dependency-free PDF writer for .NET. Absolute-position drawing, the 14 standard fonts, PNG and
JPEG images — nothing else. No native binaries, no third-party packages, no licence gate.

It exists for documents like invoices, receipts and labels: fixed layouts drawn at known
coordinates. If you need flowing multi-page layouts, tables that paginate themselves, or
Unicode beyond Latin-1, use a layout engine instead (QuestPDF, PDFsharp).

```csharp
using SimplyPdf;

var doc = new PdfDocument();
doc.Info.Title = "Invoice FE0177192";

var page = doc.AddPage(PdfPageSize.Letter, PdfMargins.All(25));

page.Rect(30, 130, 70, 54).Fill("#e3e3e3")          // grey label background
    .Rect(30, 130, 430, 18).Stroke("#e3e3e3")        // outline
    .Font(StandardFont.HelveticaBold, 9)
    .Text("Nombre", 40, 136)
    .Font(StandardFont.Helvetica)
    .Text("TORTA CHANTEL", 110, 136)
    .Text("COP 40,000.00", 475, 220, width: 100, TextAlign.Right)
    .Image(PdfImage.FromFile("logo.png"), 40, 45, width: 160);

doc.Save("invoice.pdf");          // or doc.ToArray() for an HTTP response
```

## Coordinate model

The origin is the **top-left** corner and y grows downwards, exactly like pdfkit. Units are PDF
points (1/72 in). `Text(x, y)` places the *top* of the line at `y`; the baseline sits at
`y + ascender × size / 1000`, and the cursor advances by `(ascender − descender + lineGap) × size / 1000`,
so layouts written for pdfkit port one to one.

Drawing follows the PDF model: path calls (`Rect`, `MoveTo`, `LineTo`, `Circle`…) accumulate,
and the next `Fill` / `Stroke` / `FillAndStroke` / `Clip` consumes them. `Fill(color)` with no
pending path only sets the fill colour, which is also the text colour.

## What it does

| Area | Support |
| --- | --- |
| Pages | Any size (`PdfPageSize.Letter`, `A4`, `.Landscape`, custom), any number of pages |
| Fonts | Helvetica, Times, Courier families (12 faces), never embedded, AFM metrics for measuring |
| Text | Word wrap, left / centre / right / justify, line gap, `WidthOfString`, `HeightOfString`, cursor flow |
| Encoding | WinAnsi: Latin-1 plus curly quotes, dashes, €, ™… Unsupported characters become `?` |
| Paths | Rectangles, rounded rectangles, ellipses, circles, polygons, Bézier curves, dashes, line width, clipping |
| Transforms | `Save` / `Restore`, `Translate`, `Scale`, `Rotate` around an origin |
| Images | PNG (all colour types and bit depths, transparency, Adam7), JPEG (embedded as-is), raw RGB/grey pixels |
| Output | PDF 1.4, Flate-compressed streams, `/Info` metadata, culture-invariant everywhere |

## What it does not do

- Embed fonts. Viewers substitute Helvetica with Arial / Liberation Sans, which are metric-compatible.
- Kerning. Widths are plain advance widths (pdfkit applies AFM kern pairs; differences are fractions of a point).
- Automatic page breaks: text past the bottom of the page is simply off the page.
- PDF/A, encryption, forms, annotations, links, bookmarks.
- QR codes. Draw the module matrix from any generator as 1 pt squares — see `samples/DianInvoice`.

## Repository layout

```
src/SimplyPdf/          the library (net10.0)
tests/SimplyPdf.Tests/  xunit tests, including a PNG encoder used to fabricate inputs
samples/DianInvoice/    a Colombian electronic invoice reproduced from its pdfkit original
tools/AfmToCSharp/      regenerates the font metrics from tools/afm/*.afm
```

```sh
dotnet test
dotnet run --project samples/DianInvoice -- FE0177192.pdf
```

## License

MIT. Font metrics derive from the Adobe Core 14 AFM files (© 1985-1997 Adobe Systems Incorporated),
redistributable with their copyright notice; see `tools/afm/README.md`.
