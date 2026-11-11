# SimplyPdf

A dependency-free PDF writer for .NET, made for business documents. Absolute-position drawing,
the 14 standard fonts, embedded TrueType fonts with any character they hold, PNG and JPEG images,
a table that continues on the next page, links, attachments and PDF/A-3b. No native binaries, no
third-party packages, no licence gate.

It exists for documents like invoices, quotations, receipts and labels: fixed layouts drawn at
known coordinates, plus one table that paginates. If you need flowing multi-page layouts beyond
that, or scripts that need shaping, use a layout engine instead (QuestPDF, PDFsharp).

```sh
dotnet add package SimplyPdf
```

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

An embedded font draws every character it has a glyph for, whatever the alphabet: `Łódź`,
`Ősz`, `Ω`. The text is written as glyph ids with a map back to Unicode, so viewers can copy
and search it; a character the font lacks shows its `.notdef` glyph. The standard 14 stay
limited to WinAnsi (Latin-1 plus curly quotes, dashes, €, ™) and write `?` for anything else,
so a customer name outside Latin-1 needs an embedded font.

The font's OS/2 embedding permissions are honoured: *restricted licence* and *bitmap-only* fonts
are rejected, *no subsetting* fonts are embedded whole. CFF-based `.otf` files and `.ttc`
collections are not supported — use `.ttf` with `glyf` outlines (every Google Fonts download
qualifies).

Only embed fonts whose licence allows it. SIL OFL fonts do; most "free for personal use" fonts
and many freeware desktop EULAs (Typodermic's, for instance) do not cover servers or software.

## Mixed styles in a paragraph

A `TextRun` is a stretch of text with its own font, size and colour; whatever it leaves out comes
from the page, and a plain string is a run in the current style. Several runs passed to `Text`
make **one** paragraph that wraps and aligns as a whole, so a line can break inside any of them:

```csharp
var bold = PdfFont.HelveticaBold;

page.Font(PdfFont.Helvetica, 9)
    .Text([new TextRun("Observaciones: ", bold), invoice.Note], 30, 640, new TextOptions { Width = 350 })
    .Text([new TextRun("ANULADA", bold, Color: "#c00000"), " el 3 de noviembre de 2026"], 30, 700);
```

Runs of different sizes sit on a common baseline and each line is as tall as its tallest run.
The page's own font, size and colour are left as they were.

Fonts do not always agree on how tall a line is (Helvetica-Bold asks for 3 % more than
Helvetica), so a paragraph that mixes them has lines slightly apart in height.
`TextOptions.LineHeight` fixes the distance from one line to the next, for plain text and for
runs alike: `new TextOptions { Width = 350, LineHeight = 11 }`.

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

A cell is a plain string in the table's style, or a `PdfTableCell` where one needs its own font,
size, colours, alignment or column span, which is all a totals row takes. It is a row like
any other, so it paginates with the rest:

```csharp
var bold = PdfFont.HelveticaBold;

table.AddRow(
    new PdfTableCell("Total a Pagar") { ColumnSpan = 3, Align = TextAlign.Right, Font = bold },
    new PdfTableCell(invoice.Total) { Font = bold, Background = "#e3e3e3" });
```

The grid draws no line across a spanning cell. A column can also centre its caption over
right-aligned amounts: `new PdfTableColumn("Vr. Total", 90, TextAlign.Right) { HeaderAlign = TextAlign.Center }`.

Every call draws at least the header and one row, even if that row overflows `maxY`, so the
loop always advances. `PdfTableStyle` sets fonts, colours, padding, stripes and whether the
header repeats. For single-line cells that must not wrap, `font.Fit(text, size, maxWidth)` (or
`page.FitString`) trims the text to the width with an ellipsis.

## Links

`Link(x, y, width, height, uri)` makes a rectangle of the page clickable. It draws nothing, so
put it over whatever should be clicked — here a line of text, measured with the current font:

```csharp
const string url = "https://contaru.com/facturas/FE0177192";

page.Font(StandardFont.Helvetica, 9).Fill("#0b57d0")
    .Text(url, 40, 700)
    .Link(40, 700, page.WidthOfString(url), page.CurrentLineHeight(), url)
    .Link(40, 45, 160, 48, "mailto:facturacion@contaru.com");   // over the logo
```

The URI must be absolute (`https://…`, `mailto:…`, `tel:…`; a bare `www.example.com` is rejected).
Spaces and non-ASCII characters are percent-encoded. The rectangle follows `Translate` / `Scale` /
`Rotate`, so it lands where the drawing does; under a rotation the clickable area is the bounding
box of the rotated rectangle.

## QR codes

The library draws a QR code; it does not compute one. Bring the module matrix from any generator,
as a size and a `(column, row)` predicate or as a `bool[row, column]` array, and `Modules` draws
it as vector rectangles:

```csharp
var qr = QrCode.EncodeText(payload, QrCode.Ecc.Medium); // Net.Codecrete.QrCodeGenerator, or any other

page.Modules(qr.Size, qr.GetModule, x: 375, y: 25, quietZone: 4);            // 1 pt per module
page.Modules(qr.Size, qr.GetModule, x: 375, y: 25, width: 90, quietZone: 4); // or fitted to a 90 pt box
```

Any other matrix code (Aztec, Data Matrix) is drawn the same way. See `samples/DianInvoice`.

## Attachments

A document can carry other files, which viewers list in their attachments panel: the XML of an
electronic invoice next to its printed page, for instance.

```csharp
doc.Attach("factura.xml", xmlBytes, "application/xml",
    description: "Factura electrónica (UBL 2.1)",
    relationship: PdfAttachmentRelationship.Alternative);
```

The relationship tells archiving and e-invoicing readers what the file is to the document.
Attaching a file does not by itself make the document PDF/A; that is the next section.

## PDF/A-3

Set `Conformance` and the document is written as PDF/A-3b, the archival flavour that may carry
attachments. It gains XMP metadata, an sRGB output intent and a file identifier:

```csharp
var doc = new PdfDocument { Conformance = PdfConformance.PdfA3B };
```

The standard forbids a few things, and `Save` refuses them with the reason instead of writing a
file that only claims to conform: the standard 14 fonts (they are never embedded, so every font
has to come from `PdfFont.FromFile`), a character its font has no glyph for, and CMYK JPEGs.

Documents made this way pass the PDF/A-3b profile of [veraPDF](https://verapdf.org). Validate
your own before relying on it: a validator is the only real test of conformance.

## Coordinate model

The origin is the **top-left** corner and y grows downwards, exactly like pdfkit. Units are PDF
points (1/72 in). `Text(x, y)` places the *top* of the line at `y`; the baseline sits at
`y + ascender × size / 1000`, and the cursor advances by `(ascender − descender + lineGap) × size / 1000`,
so layouts written for pdfkit port one to one.

Positions taken from CSS, SVG or a design tool are baselines instead. `TextOptions.Baseline`
says what `y` refers to (`Top` by default, `Alphabetic`, `Middle` or `Bottom`), and the cursor
keeps that reference, so consecutive calls flow from baseline to baseline:

```csharp
page.Text("Total a Pagar", 400, 558.6, new TextOptions { Baseline = TextBaseline.Alphabetic });
```

`font.Ascent(size)` and `font.Descent(size)` give the distances from the baseline to the top
and to the bottom of a line, for boxes that have to hug the text.

Drawing follows the PDF model: path calls (`Rect`, `MoveTo`, `LineTo`, `Circle`…) accumulate,
and the next `Fill` / `Stroke` / `FillAndStroke` / `Clip` consumes them. `Fill(color)` with no
pending path only sets the fill colour, which is also the text colour.

## What it does

| Area | Support |
| --- | --- |
| Pages | Any size (`PdfPageSize.Letter`, `A4`, `.Landscape`, custom), any number of pages |
| Fonts | Helvetica, Times, Courier families (12 faces) with AFM metrics; TrueType `.ttf` embedded and subsetted |
| Text | Word wrap, left / centre / right / justify, line gap, fixed line height, `WidthOfString`, `HeightOfString`, `Fit` with ellipsis, cursor flow, runs of different fonts, sizes and colours in one paragraph (`TextRun`), placement by top, middle, baseline or bottom |
| Tables | `PdfTable`: fixed column widths, wrapped cells, header band, grid, stripes, cells with their own font, colours, alignment and column span, drawn in slices that continue on the next page |
| Encoding | Embedded fonts: any character the font has, as glyph ids with a `ToUnicode` map (text can be copied and searched). Standard fonts: WinAnsi, Latin-1 plus curly quotes, dashes, €, ™…; anything else becomes `?` |
| Paths | Rectangles, rounded rectangles, ellipses, circles, polygons, Bézier curves, dashes, line width, clipping |
| QR codes | `Modules` draws the matrix any generator produces (QR, Aztec, Data Matrix): dark runs merged into rectangles and filled once, quiet zone, sized by module or by drawn width |
| Transforms | `Save` / `Restore`, `Translate`, `Scale`, `Rotate` around an origin |
| Links | `Link`: a clickable rectangle that opens a URI (`https:`, `mailto:`, `tel:`…), placed in the same coordinates as the drawing |
| Images | PNG (all colour types and bit depths, transparency, Adam7), JPEG (embedded as-is), raw RGB/grey pixels |
| Attachments | Files carried inside the PDF with `doc.Attach`: name, MIME type, description and relationship to the document |
| Output | PDF 1.4, or PDF/A-3b (PDF 1.7) on request; Flate-compressed streams, `/Info` metadata, culture-invariant everywhere |

## What it does not do

- Text shaping. Characters map to glyphs one to one, left to right, so scripts that need shaping or
  run right to left (Arabic, Hebrew, Indic) and colour emoji do not come out right. CJK works with a
  font that has the glyphs, but lines only break at spaces.
- Kerning. Widths are plain advance widths (pdfkit applies AFM kern pairs; differences are fractions of a point).
- Automatic page breaks for free text: text past the bottom of the page is simply off the page (tables paginate through `PdfTable.Draw`).
- Encryption, forms, annotations other than links, bookmarks.
- QR encoding, by design: `Modules` draws the matrix a generator produces, it does not compute it.

## Repository layout

```
src/SimplyPdf/          the library (net10.0)
tests/SimplyPdf.Tests/  xunit tests, including a PNG encoder used to fabricate inputs
samples/A4Invoice/      an A4 electronic invoice ported from its wkhtmltopdf original, drawn in CSS pixels
samples/DianInvoice/    a Colombian electronic invoice reproduced from its pdfkit original
samples/PosReceipt/     an 80 mm thermal-printer receipt in three OFL monospace fonts
samples/PosInvoice/     an electronic invoice in receipt form (44-column retail layout) on an 80 mm roll
tools/AfmToCSharp/      regenerates the font metrics from tools/afm/*.afm
```

```sh
dotnet test
dotnet run --project samples/A4Invoice -- FSII1305.pdf
dotnet run --project samples/DianInvoice -- FE0177192.pdf
dotnet run --project samples/PosReceipt -- out/
dotnet run --project samples/PosInvoice -- factura-pos.pdf VT323-Regular.ttf
```

## Releasing

Bump `<Version>` in `src/SimplyPdf/SimplyPdf.csproj`, merge, then push the matching tag:

```sh
git tag v0.3.0 && git push origin v0.3.0
```

`.github/workflows/release.yml` builds, tests, packs and pushes to nuget.org through
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): no API
key is stored anywhere. It needs a trusted publishing policy on nuget.org for this repository
and workflow, and the repository variable `NUGET_USER` set to the nuget.org username that owns it.

## License

MIT. Font metrics derive from the Adobe Core 14 AFM files (© 1985-1997 Adobe Systems Incorporated),
redistributable with their copyright notice; see `tools/afm/README.md`.
