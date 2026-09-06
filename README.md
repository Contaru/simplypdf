# SimplyPdf

A dependency-free PDF writer for .NET. Absolute-position drawing, the 14 standard fonts, embedded
TrueType fonts, PNG and JPEG images — nothing else. No native binaries, no third-party packages,
no licence gate.

It exists for documents like invoices, receipts and labels: fixed layouts drawn at known
coordinates, plus one table that continues on the next page. If you need flowing multi-page
layouts beyond that, or Unicode beyond Latin-1, use a layout engine instead (QuestPDF, PDFsharp).

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

## Fonts

The standard 14 (`PdfFont.Helvetica`, `PdfFont.CourierBold`, …) cost nothing: viewers supply
them. Any other look needs a TrueType file, which is embedded **subsetted** — only the glyphs
the document uses, typically 5–15 KB:

```csharp
var mono = PdfFont.FromFile("fonts/ShareTechMono-Regular.ttf"); // load once, reuse everywhere
page.Font(mono, 9).Text("FACTURA DE VENTA POS-0012345", 10, 20);
```

Text is WinAnsi-encoded in both cases (Latin-1 plus curly quotes, dashes, €, ™); characters the
font lacks show its `.notdef` glyph. The font's OS/2 embedding permissions are honoured:
*restricted licence* and *bitmap-only* fonts are rejected, *no subsetting* fonts are embedded
whole. CFF-based `.otf` files and `.ttc` collections are not supported — use `.ttf` with `glyf`
outlines (every Google Fonts download qualifies).

Only embed fonts whose licence allows it. SIL OFL fonts do; most "free for personal use" fonts
and many freeware desktop EULAs (Typodermic's, for instance) do not cover servers or software.

## Tables

`PdfTable` (namespace `SimplyPdf.Layout`) is a grid of text cells with fixed column widths. Row
heights follow the wrapped cell text; the header sits on a band and the grid is stroked around
the rows. It draws in **slices**: each `Draw` paints as many rows as fit above `maxY`, repeats
the header, and returns where it stopped, so a long table continues on the next page:

```csharp
using SimplyPdf.Layout;

var table = new PdfTable(
[
    new PdfTableColumn("Ítem", 30, TextAlign.Center),
    new PdfTableColumn("Descripción", 250),
    new PdfTableColumn("Cant.", 40, TextAlign.Right),
    new PdfTableColumn("Vr. Total", 90, TextAlign.Right),
]);
foreach (var line in invoice.Lines)
{
    table.AddRow(line.Number, line.Description, line.Quantity, line.Total);
}

var slice = table.Draw(page, x: 30, y: 190, maxY: 540);
while (!slice.IsComplete)
{
    page = doc.AddPage(PdfPageSize.Letter, PdfMargins.All(25));
    slice = table.Draw(page, 30, 60, 700, slice.NextRow);
}
// slice.Bottom is where the totals go
```

Every call draws at least the header and one row, even if that row overflows `maxY`, so the
loop always advances. `PdfTableStyle` sets fonts, colours, padding, stripes and whether the
header repeats. For single-line cells that must not wrap, `font.Fit(text, size, maxWidth)` (or
`page.FitString`) trims the text to the width with an ellipsis.

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
| Fonts | Helvetica, Times, Courier families (12 faces) with AFM metrics; TrueType `.ttf` embedded and subsetted |
| Text | Word wrap, left / centre / right / justify, line gap, `WidthOfString`, `HeightOfString`, `Fit` with ellipsis, cursor flow |
| Tables | `PdfTable`: fixed column widths, wrapped cells, header band, grid, stripes, drawn in slices that continue on the next page |
| Encoding | WinAnsi: Latin-1 plus curly quotes, dashes, €, ™… Unsupported characters become `?` |
| Paths | Rectangles, rounded rectangles, ellipses, circles, polygons, Bézier curves, dashes, line width, clipping |
| Transforms | `Save` / `Restore`, `Translate`, `Scale`, `Rotate` around an origin |
| Images | PNG (all colour types and bit depths, transparency, Adam7), JPEG (embedded as-is), raw RGB/grey pixels |
| Output | PDF 1.4, Flate-compressed streams, `/Info` metadata, culture-invariant everywhere |

## What it does not do

- Unicode beyond WinAnsi (no CJK, no emoji), even with embedded fonts.
- Kerning. Widths are plain advance widths (pdfkit applies AFM kern pairs; differences are fractions of a point).
- Automatic page breaks for free text: text past the bottom of the page is simply off the page (tables paginate through `PdfTable.Draw`).
- PDF/A, encryption, forms, annotations, links, bookmarks.
- QR codes. Draw the module matrix from any generator as 1 pt squares — see `samples/DianInvoice`.

## Repository layout

```
src/SimplyPdf/          the library (net10.0)
tests/SimplyPdf.Tests/  xunit tests, including a PNG encoder used to fabricate inputs
samples/DianInvoice/    a Colombian electronic invoice reproduced from its pdfkit original
samples/PosReceipt/     an 80 mm thermal-printer receipt in three OFL monospace fonts
samples/PosInvoice/     an electronic invoice in receipt form (44-column retail layout) on an 80 mm roll
tools/AfmToCSharp/      regenerates the font metrics from tools/afm/*.afm
```

```sh
dotnet test
dotnet run --project samples/DianInvoice -- FE0177192.pdf
dotnet run --project samples/PosReceipt -- out/
dotnet run --project samples/PosInvoice -- factura-pos.pdf VT323-Regular.ttf
```

## License

MIT. Font metrics derive from the Adobe Core 14 AFM files (© 1985-1997 Adobe Systems Incorporated),
redistributable with their copyright notice; see `tools/afm/README.md`.
