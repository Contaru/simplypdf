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
    private readonly Dictionary<PdfFont, HashSet<ushort>> _fontUsage = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfImage, string> _imageNames = new(ReferenceEqualityComparer.Instance);
    private readonly List<PdfAttachment> _attachments = [];

    /// <summary>Metadata written to the /Info dictionary.</summary>
    public PdfDocumentInfo Info { get; } = new();

    /// <summary>Files attached so far with <see cref="Attach"/>, in the order they were added.</summary>
    public IReadOnlyList<PdfAttachment> Attachments => _attachments;

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

        var catalog = new PdfDictionary
        {
            ["Type"] = new PdfName("Catalog"),
            ["Pages"] = pagesRef,
        };
        WriteAttachments(writer, catalog);
        writer.Set(catalogRef, catalog);

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

    /// <summary>
    /// Carries a file inside the document: the XML of an electronic invoice next to its printed
    /// page, a spreadsheet behind a report. <paramref name="fileName"/> is the bare name the file is
    /// listed and saved under, without any folder, and must be unique in the document;
    /// <paramref name="mediaType"/> is its MIME type.
    /// </summary>
    public PdfAttachment Attach(
        string fileName,
        byte[] data,
        string mediaType,
        string? description = null,
        PdfAttachmentRelationship relationship = PdfAttachmentRelationship.Unspecified,
        DateTimeOffset? modified = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        if (fileName.AsSpan().IndexOfAny('/', '\\') >= 0 || fileName.Any(char.IsControl))
        {
            throw new ArgumentException("An attachment is named by its file name alone, without a folder or control characters.", nameof(fileName));
        }

        var slash = mediaType.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == mediaType.Length - 1 || mediaType.Any(c => c is <= ' ' or > '~'))
        {
            throw new ArgumentException("A media type looks like \"application/xml\": a type and a subtype in plain ASCII.", nameof(mediaType));
        }

        if (!Enum.IsDefined(relationship))
        {
            throw new ArgumentOutOfRangeException(nameof(relationship), relationship, "Unknown relationship.");
        }

        if (_attachments.Any(existing => string.Equals(existing.FileName, fileName, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"The document already has an attachment named '{fileName}'.", nameof(fileName));
        }

        var attachment = new PdfAttachment(fileName, [.. data], mediaType, description, relationship, modified);
        _attachments.Add(attachment);
        return attachment;
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
    internal string RegisterFontUse(PdfFont font, ReadOnlySpan<Text.TextCode> codes)
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
            usage.Add(code.Value);
        }

        return name;
    }

    /// <summary>
    /// Writes each attachment as an embedded file with its file specification and lists them in the
    /// catalog: in the name tree viewers read (keys in byte order, as a name tree requires) and in
    /// /AF, where archiving readers look for the files associated with the document.
    /// </summary>
    private void WriteAttachments(PdfWriter writer, PdfDictionary catalog)
    {
        if (_attachments.Count == 0)
        {
            return;
        }

        var names = new List<object>();
        var associated = new List<object>();
        foreach (var attachment in _attachments.OrderBy(a => a.FileName, StringComparer.Ordinal))
        {
            var file = writer.Add(
                new PdfDictionary
                {
                    ["Type"] = new PdfName("EmbeddedFile"),
                    ["Subtype"] = new PdfName(attachment.MediaType),
                    ["Params"] = new PdfDictionary
                    {
                        ["Size"] = attachment.Length,
                        ["ModDate"] = attachment.Modified ?? Info.CreationDate,
                    },
                },
                attachment.Data,
                compress: true);

            var specification = writer.Add(new PdfDictionary
            {
                ["Type"] = new PdfName("Filespec"),
                ["F"] = attachment.FileName,
                ["UF"] = attachment.FileName,
                ["EF"] = new PdfDictionary { ["F"] = file, ["UF"] = file },
                ["Desc"] = attachment.Description,
                ["AFRelationship"] = new PdfName(attachment.Relationship.ToString()),
            });

            names.Add(attachment.FileName);
            names.Add(specification);
            associated.Add(specification);
        }

        catalog["Names"] = new PdfDictionary { ["EmbeddedFiles"] = new PdfDictionary { ["Names"] = names } };
        catalog["AF"] = associated;
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
