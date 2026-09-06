using SimplyPdf.Internal;

namespace SimplyPdf;

/// <summary>
/// A PDF document under construction. Add pages with <see cref="AddPage()"/>, draw on them,
/// then call <see cref="Save(Stream)"/> or <see cref="ToArray"/>.
/// </summary>
public sealed class PdfDocument
{
    private readonly List<PdfPage> _pages = [];
    private readonly Dictionary<PdfFont, string> _fontNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfFont, HashSet<byte>> _fontUsage = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfImage, string> _imageNames = new(ReferenceEqualityComparer.Instance);

    /// <summary>Metadata written to the /Info dictionary.</summary>
    public PdfDocumentInfo Info { get; } = new();

    /// <summary>
    /// Flate-compress page content streams. Default true; turn off to inspect the generated
    /// operators in a text editor.
    /// </summary>
    public bool CompressStreams { get; set; } = true;

    /// <summary>Pages added so far, in order.</summary>
    public IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>Adds a US Letter page with one-inch margins.</summary>
    public PdfPage AddPage() => AddPage(PdfPageSize.Letter, PdfMargins.Default);

    /// <summary>Adds a page of the given size with one-inch margins.</summary>
    public PdfPage AddPage(PdfPageSize size) => AddPage(size, PdfMargins.Default);

    /// <summary>Adds a page of the given size and margins.</summary>
    public PdfPage AddPage(PdfPageSize size, PdfMargins margins)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Page dimensions must be positive.");
        }

        var page = new PdfPage(this, size, margins);
        _pages.Add(page);
        return page;
    }

    /// <summary>Serialises the document into <paramref name="stream"/>.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (_pages.Count == 0)
        {
            throw new InvalidOperationException("A PDF document needs at least one page.");
        }

        var writer = new PdfWriter();
        var catalogRef = writer.Reserve();
        var pagesRef = writer.Reserve();

        var fontRefs = new Dictionary<string, PdfObjectRef>(StringComparer.Ordinal);
        foreach (var (font, name) in _fontNames)
        {
            fontRefs[name] = font.WriteTo(writer, _fontUsage[font]);
        }

        var imageRefs = new Dictionary<string, PdfObjectRef>(StringComparer.Ordinal);
        foreach (var (image, name) in _imageNames)
        {
            imageRefs[name] = WriteImage(writer, image);
        }

        var pageRefs = new List<PdfObjectRef>(_pages.Count);
        foreach (var page in _pages)
        {
            var contentRef = writer.Add([], page.GetContentBytes(), CompressStreams);

            var fonts = new PdfDictionary();
            foreach (var name in page.UsedFonts)
            {
                fonts[name] = fontRefs[name];
            }

            var resources = new PdfDictionary
            {
                ["ProcSet"] = new object[] { new PdfName("PDF"), new PdfName("Text"), new PdfName("ImageB"), new PdfName("ImageC"), new PdfName("ImageI") },
                ["Font"] = fonts,
            };

            if (page.UsedImages.Count > 0)
            {
                var xobjects = new PdfDictionary();
                foreach (var name in page.UsedImages)
                {
                    xobjects[name] = imageRefs[name];
                }

                resources["XObject"] = xobjects;
            }

            pageRefs.Add(writer.Add(new PdfDictionary
            {
                ["Type"] = new PdfName("Page"),
                ["Parent"] = pagesRef,
                ["MediaBox"] = new object[] { 0, 0, page.Width, page.Height },
                ["Contents"] = contentRef,
                ["Resources"] = resources,
            }));
        }

        writer.Set(pagesRef, new PdfDictionary
        {
            ["Type"] = new PdfName("Pages"),
            ["Kids"] = pageRefs,
            ["Count"] = pageRefs.Count,
        });

        writer.Set(catalogRef, new PdfDictionary
        {
            ["Type"] = new PdfName("Catalog"),
            ["Pages"] = pagesRef,
        });

        var infoRef = writer.Add(new PdfDictionary
        {
            ["Title"] = Info.Title,
            ["Author"] = Info.Author,
            ["Subject"] = Info.Subject,
            ["Keywords"] = Info.Keywords,
            ["Creator"] = Info.Creator,
            ["Producer"] = Info.Producer,
            ["CreationDate"] = Info.CreationDate,
        });

        writer.WriteTo(stream, catalogRef, infoRef);
    }

    /// <summary>Serialises the document to a file, replacing it if it exists.</summary>
    public void Save(string path)
    {
        using var file = File.Create(path);
        Save(file);
    }

    /// <summary>Serialises the document into a byte array.</summary>
    public byte[] ToArray()
    {
        using var buffer = new MemoryStream();
        Save(buffer);
        return buffer.ToArray();
    }

    /// <summary>Registers a font on first use and records which codes it draws, for subsetting.</summary>
    internal string RegisterFontUse(PdfFont font, ReadOnlySpan<byte> codes)
    {
        if (!_fontNames.TryGetValue(font, out var name))
        {
            name = "F" + (_fontNames.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _fontNames[font] = name;
            _fontUsage[font] = [];
        }

        var usage = _fontUsage[font];
        foreach (var code in codes)
        {
            usage.Add(code);
        }

        return name;
    }

    internal string ImageResourceName(PdfImage image)
    {
        if (!_imageNames.TryGetValue(image, out var name))
        {
            name = "I" + (_imageNames.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _imageNames[image] = name;
        }

        return name;
    }

    private static PdfObjectRef WriteImage(PdfWriter writer, PdfImage image)
    {
        var dictionary = new PdfDictionary
        {
            ["Type"] = new PdfName("XObject"),
            ["Subtype"] = new PdfName("Image"),
            ["Width"] = image.Width,
            ["Height"] = image.Height,
            ["BitsPerComponent"] = 8,
        };

        if (image.IsJpeg)
        {
            dictionary["ColorSpace"] = new PdfName(image.Components switch
            {
                1 => "DeviceGray",
                3 => "DeviceRGB",
                _ => "DeviceCMYK",
            });
            dictionary["Filter"] = new PdfName("DCTDecode");
            if (image.Components == 4)
            {
                // CMYK JPEGs in the wild are Adobe-inverted; every major producer applies this Decode.
                dictionary["Decode"] = new object[] { 1, 0, 1, 0, 1, 0, 1, 0 };
            }

            return writer.Add(dictionary, image.Data, compress: false);
        }

        if (image.Alpha is not null)
        {
            dictionary["SMask"] = writer.Add(new PdfDictionary
            {
                ["Type"] = new PdfName("XObject"),
                ["Subtype"] = new PdfName("Image"),
                ["Width"] = image.Width,
                ["Height"] = image.Height,
                ["ColorSpace"] = new PdfName("DeviceGray"),
                ["BitsPerComponent"] = 8,
            }, image.Alpha, compress: true);
        }

        dictionary["ColorSpace"] = new PdfName(image.Components == 1 ? "DeviceGray" : "DeviceRGB");
        return writer.Add(dictionary, image.Data, compress: true);
    }
}
