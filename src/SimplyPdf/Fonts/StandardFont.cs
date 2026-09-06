namespace SimplyPdf;

/// <summary>
/// The Latin-text subset of the 14 standard PDF fonts. Every PDF viewer ships them (or a
/// metric-compatible substitute such as Arial / Liberation Sans), so they are never embedded
/// and cost nothing in file size.
/// </summary>
public enum StandardFont
{
    /// <summary>Helvetica (regular).</summary>
    Helvetica,

    /// <summary>Helvetica-Bold.</summary>
    HelveticaBold,

    /// <summary>Helvetica-Oblique.</summary>
    HelveticaOblique,

    /// <summary>Helvetica-BoldOblique.</summary>
    HelveticaBoldOblique,

    /// <summary>Times-Roman.</summary>
    TimesRoman,

    /// <summary>Times-Bold.</summary>
    TimesBold,

    /// <summary>Times-Italic.</summary>
    TimesItalic,

    /// <summary>Times-BoldItalic.</summary>
    TimesBoldItalic,

    /// <summary>Courier (monospaced).</summary>
    Courier,

    /// <summary>Courier-Bold.</summary>
    CourierBold,

    /// <summary>Courier-Oblique.</summary>
    CourierOblique,

    /// <summary>Courier-BoldOblique.</summary>
    CourierBoldOblique,
}
