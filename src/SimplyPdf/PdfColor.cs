using System.Globalization;
using SimplyPdf.Internal;

namespace SimplyPdf;

/// <summary>
/// An RGB colour with components in the 0..1 range. Implicitly convertible from CSS-style
/// strings: <c>"#e3e3e3"</c>, <c>"#fff"</c> or a basic colour name such as <c>"black"</c>.
/// </summary>
public readonly record struct PdfColor
{
    /// <summary>Creates a colour from components in the 0..1 range.</summary>
    public PdfColor(double r, double g, double b)
    {
        R = Clamp(r);
        G = Clamp(g);
        B = Clamp(b);
    }

    /// <summary>Red component, 0..1.</summary>
    public double R { get; }

    /// <summary>Green component, 0..1.</summary>
    public double G { get; }

    /// <summary>Blue component, 0..1.</summary>
    public double B { get; }

    /// <summary>Pure black.</summary>
    public static PdfColor Black { get; } = new(0, 0, 0);

    /// <summary>Pure white.</summary>
    public static PdfColor White { get; } = new(1, 1, 1);

    /// <summary>Creates a colour from 0..255 components.</summary>
    public static PdfColor FromRgb(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>Creates a grey level, 0 = black and 1 = white.</summary>
    public static PdfColor Gray(double level) => new(level, level, level);

    /// <summary>Parses <c>#rgb</c>, <c>#rrggbb</c> or a basic colour name.</summary>
    public static PdfColor Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var text = value.Trim();

        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            }

            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }

            throw new FormatException($"'{value}' is not a valid hex colour.");
        }

        return text.ToLowerInvariant() switch
        {
            "black" => Black,
            "white" => White,
            "red" => FromRgb(255, 0, 0),
            "green" => FromRgb(0, 128, 0),
            "lime" => FromRgb(0, 255, 0),
            "blue" => FromRgb(0, 0, 255),
            "yellow" => FromRgb(255, 255, 0),
            "cyan" or "aqua" => FromRgb(0, 255, 255),
            "magenta" or "fuchsia" => FromRgb(255, 0, 255),
            "gray" or "grey" => FromRgb(128, 128, 128),
            "silver" => FromRgb(192, 192, 192),
            "orange" => FromRgb(255, 165, 0),
            "navy" => FromRgb(0, 0, 128),
            "maroon" => FromRgb(128, 0, 0),
            "purple" => FromRgb(128, 0, 128),
            "teal" => FromRgb(0, 128, 128),
            "olive" => FromRgb(128, 128, 0),
            _ => throw new FormatException($"'{value}' is not a known colour name."),
        };
    }

    /// <summary>Allows passing colour strings wherever a <see cref="PdfColor"/> is expected.</summary>
    public static implicit operator PdfColor(string value) => Parse(value);

    /// <summary>Equivalent of the implicit string conversion, for languages without operator support.</summary>
    public static PdfColor FromString(string value) => Parse(value);

    internal string ToOperands() => PdfFormat.Num(R) + " " + PdfFormat.Num(G) + " " + PdfFormat.Num(B);

    private static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
}
