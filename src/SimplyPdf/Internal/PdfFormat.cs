using System.Globalization;
using System.Text;

namespace SimplyPdf.Internal;

/// <summary>
/// Low-level formatting of PDF tokens. Everything here is culture-invariant on purpose:
/// a decimal comma inside a content stream silently corrupts the document.
/// </summary>
internal static class PdfFormat
{
    /// <summary>Formats a real number with at most three decimals, as PDF viewers expect.</summary>
    public static string Num(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "PDF numbers must be finite.");
        }

        var rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            return "0"; // normalises -0 as well
        }

        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Appends a literal string token, e.g. <c>(Hello \(world\))</c>, escaping everything outside printable ASCII.</summary>
    public static void AppendLiteral(StringBuilder sb, ReadOnlySpan<byte> bytes)
    {
        sb.Append('(');
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(':
                    sb.Append("\\(");
                    break;
                case (byte)')':
                    sb.Append("\\)");
                    break;
                case (byte)'\\':
                    sb.Append("\\\\");
                    break;
                case < 32 or > 126:
                    sb.Append('\\');
                    sb.Append((char)('0' + ((b >> 6) & 7)));
                    sb.Append((char)('0' + ((b >> 3) & 7)));
                    sb.Append((char)('0' + (b & 7)));
                    break;
                default:
                    sb.Append((char)b);
                    break;
            }
        }

        sb.Append(')');
    }

    /// <summary>
    /// Appends a text string for use in dictionaries (Info, etc.). Pure printable ASCII is written
    /// as a literal; anything else as a UTF-16BE hex string with BOM, which every viewer understands.
    /// </summary>
    public static void AppendTextString(StringBuilder sb, string text)
    {
        var ascii = true;
        foreach (var c in text)
        {
            if (c is < ' ' or > '~')
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            AppendLiteral(sb, Encoding.ASCII.GetBytes(text));
            return;
        }

        sb.Append("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(text))
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        sb.Append('>');
    }

    /// <summary>Appends a name token, escaping delimiter and non-printable bytes with #xx.</summary>
    public static void AppendName(StringBuilder sb, string name)
    {
        sb.Append('/');
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            var regular = b is > 32 and < 127 && b is not ((byte)'/' or (byte)'#' or (byte)'(' or (byte)')'
                or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'%');
            if (regular)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('#').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>Formats a date as <c>D:YYYYMMDDHHmmSS+HH'mm'</c>.</summary>
    public static string Date(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture,
            $"D:{value:yyyyMMddHHmmss}{sign}{offset.Hours:00}'{offset.Minutes:00}'");
    }
}
