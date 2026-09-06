using System.Collections;
using System.Globalization;
using System.Text;

namespace SimplyPdf.Internal;

/// <summary>A PDF name object, written as <c>/Name</c>.</summary>
internal readonly record struct PdfName(string Value)
{
    public override string ToString() => "/" + Value;
}

/// <summary>Reference to an indirect object, written as <c>n 0 R</c>.</summary>
internal sealed class PdfObjectRef(int number)
{
    public int Number { get; } = number;

    public override string ToString() => Number.ToString(CultureInfo.InvariantCulture) + " 0 R";
}

/// <summary>Pre-formatted token(s) inserted verbatim.</summary>
internal readonly record struct PdfRaw(string Text);

/// <summary>
/// A PDF dictionary. Keys are names without the leading slash. Values may be: <see langword="null"/>,
/// <see cref="bool"/>, <see cref="int"/>, <see cref="long"/>, <see cref="double"/>, <see cref="string"/>
/// (a text string), <see cref="PdfName"/>, <see cref="PdfObjectRef"/>, <see cref="PdfRaw"/>,
/// <see cref="DateTimeOffset"/>, another <see cref="PdfDictionary"/>, or an <see cref="IEnumerable"/> (array).
/// </summary>
internal sealed class PdfDictionary : Dictionary<string, object?>
{
    public PdfDictionary()
        : base(StringComparer.Ordinal)
    {
    }

    public void AppendTo(StringBuilder sb)
    {
        sb.Append("<<");
        foreach (var (key, value) in this)
        {
            if (value is null)
            {
                continue;
            }

            sb.Append(' ');
            PdfFormat.AppendName(sb, key);
            sb.Append(' ');
            PdfSerializer.Append(sb, value);
        }

        sb.Append(" >>");
    }
}

internal static class PdfSerializer
{
    public static void Append(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(PdfFormat.Num(d));
                break;
            case float f:
                sb.Append(PdfFormat.Num(f));
                break;
            case string s:
                PdfFormat.AppendTextString(sb, s);
                break;
            case PdfName name:
                PdfFormat.AppendName(sb, name.Value);
                break;
            case PdfObjectRef reference:
                sb.Append(reference.ToString());
                break;
            case PdfRaw raw:
                sb.Append(raw.Text);
                break;
            case DateTimeOffset date:
                sb.Append('(').Append(PdfFormat.Date(date)).Append(')');
                break;
            case PdfDictionary dictionary:
                dictionary.AppendTo(sb);
                break;
            case IEnumerable array:
                sb.Append('[');
                var first = true;
                foreach (var item in array)
                {
                    if (!first)
                    {
                        sb.Append(' ');
                    }

                    first = false;
                    Append(sb, item);
                }

                sb.Append(']');
                break;
            default:
                throw new NotSupportedException($"Cannot serialise a {value.GetType()} into a PDF object.");
        }
    }
}
