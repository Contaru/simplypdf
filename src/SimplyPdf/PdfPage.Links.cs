using System.Buffers;
using System.Globalization;
using System.Text;

namespace SimplyPdf;

public sealed partial class PdfPage
{
    private readonly List<LinkAnnotation> _links = [];
    private readonly Stack<Matrix> _savedMatrices = new();

    // Product of the transforms applied through Transform / Translate / Scale / Rotate, i.e. the map
    // from the coordinates the caller is using right now to the untransformed top-left page space.
    // Content streams do not need it (the viewer multiplies the cm operators itself), but annotation
    // rectangles are stored in default page space, outside the content stream.
    private Matrix _matrix = Matrix.Identity;

    internal IReadOnlyList<LinkAnnotation> Links => _links;

    /// <summary>
    /// Makes the rectangle whose top-left corner is (<paramref name="x"/>, <paramref name="y"/>) a
    /// clickable link to <paramref name="uri"/>. Nothing is drawn: put the link over text, an image
    /// or a shape drawn before or after it.
    /// </summary>
    /// <param name="x">Left edge of the clickable area.</param>
    /// <param name="y">Top edge of the clickable area.</param>
    /// <param name="width">Width of the clickable area; must be positive.</param>
    /// <param name="height">Height of the clickable area; must be positive.</param>
    /// <param name="uri">
    /// An absolute URI including its scheme, such as <c>https://example.com/invoice/1</c> or
    /// <c>mailto:billing@example.com</c>. White space around it is ignored. Spaces, non-ASCII
    /// characters (as UTF-8) and the few ASCII characters a URI cannot contain are percent-encoded;
    /// existing <c>%XX</c> escapes are kept, so an already encoded URI is written unchanged.
    /// </param>
    /// <remarks>
    /// <para>
    /// The rectangle is in the same coordinates as the drawing calls and follows the transforms in
    /// effect when <c>Link</c> is called (<see cref="Translate"/>, <see cref="Scale"/>,
    /// <see cref="Rotate"/>, <see cref="Transform"/>, undone by <see cref="Restore"/>). A PDF link
    /// area is always an upright rectangle, so under a rotation or skew the clickable area is the
    /// bounding box of the transformed rectangle. It is not affected by <see cref="Clip"/>.
    /// </para>
    /// <para>
    /// Any scheme is accepted and the viewer decides what to do with it. When the URI comes from
    /// user input, check its scheme before linking to it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="uri"/> is empty, has no scheme (<c>www.example.com</c>), or contains control
    /// characters or malformed UTF-16.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A coordinate is not a finite number, or <paramref name="width"/> or <paramref name="height"/>
    /// is not positive.
    /// </exception>
    public PdfPage Link(double x, double y, double width, double height, string uri)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(width, nameof(width));
        RequireFinite(height, nameof(height));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var target = EncodeUri(uri);

        // Bounding box of the four corners in untransformed top-left space...
        var (x1, y1) = _matrix.Apply(x, y);
        var (x2, y2) = _matrix.Apply(x + width, y);
        var (x3, y3) = _matrix.Apply(x, y + height);
        var (x4, y4) = _matrix.Apply(x + width, y + height);
        var left = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        var right = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
        var top = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        var bottom = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));

        // ...then flipped, because /Rect is [left bottom right top] with the origin at the bottom-left.
        _links.Add(new LinkAnnotation(left, Height - bottom, right, Height - top, target));
        return this;
    }

    private void PushMatrix() => _savedMatrices.Push(_matrix);

    private void PopMatrix()
    {
        // An unbalanced Restore is the caller's bug and is written to the content stream as is;
        // there is no saved matrix to go back to, so the current one stays.
        if (_savedMatrices.TryPop(out var saved))
        {
            _matrix = saved;
        }
    }

    private void ConcatMatrix(double a, double b, double c, double d, double e, double f) =>
        _matrix = new Matrix(a, b, c, d, e, f).Then(_matrix);

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Link coordinates must be finite numbers.");
        }
    }

    /// <summary>
    /// Validates <paramref name="uri"/> and returns it as printable ASCII, which is what a PDF URI
    /// action requires. Deliberately not <see cref="Uri"/>: it rewrites what it parses (trailing
    /// slashes, dot segments, escapes), leaves non-ASCII host names as they are, and reads
    /// <c>/path</c> as an absolute file URI on Unix only.
    /// </summary>
    private static string EncodeUri(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var text = uri.AsSpan().Trim();
        if (text.IsEmpty)
        {
            throw new ArgumentException("The link URI is empty.", nameof(uri));
        }

        if (!HasScheme(text))
        {
            throw new ArgumentException(
                $"The link URI '{uri}' must be absolute, including its scheme: 'https://example.com', 'mailto:someone@example.com'.",
                nameof(uri));
        }

        var sb = new StringBuilder(text.Length);
        Span<byte> utf8 = stackalloc byte[4];
        for (var i = 0; i < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text[i..], out var rune, out var consumed) != OperationStatus.Done)
            {
                throw new ArgumentException("The link URI contains an unpaired UTF-16 surrogate.", nameof(uri));
            }

            if (Rune.IsControl(rune))
            {
                throw new ArgumentException(
                    $"The link URI contains the control character U+{rune.Value:X4}; remove it or percent-encode it.",
                    nameof(uri));
            }

            var c = text[i];
            var isEscape = c == '%' && i + 2 < text.Length && char.IsAsciiHexDigit(text[i + 1]) && char.IsAsciiHexDigit(text[i + 2]);
            if (IsUriCharacter(c) || isEscape)
            {
                sb.Append(c);
            }
            else
            {
                // Also a '%' that does not start an escape: left alone it would swallow the next two characters.
                var length = rune.EncodeToUtf8(utf8);
                foreach (var b in utf8[..length])
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            i += consumed;
        }

        return sb.ToString();
    }

    /// <summary>
    /// RFC 3986 scheme (a letter, then letters, digits, '+', '-' or '.') followed by ':' and
    /// something after it. One-letter schemes are refused: none is registered, and in practice
    /// they are Windows paths such as <c>C:\invoices\1.pdf</c>.
    /// </summary>
    private static bool HasScheme(ReadOnlySpan<char> text)
    {
        var colon = text.IndexOf(':');
        if (colon < 2 || colon == text.Length - 1 || !char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        foreach (var c in text[1..colon])
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>RFC 3986 unreserved and reserved characters; everything else must be percent-encoded.</summary>
    private static bool IsUriCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || "-._~:/?#[]@!$&'()*+,;=".Contains(c, StringComparison.Ordinal);

    /// <summary>
    /// An affine transform in PDF notation: (x, y) maps to (a·x + c·y + e, b·x + d·y + f).
    /// </summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

        /// <summary>
        /// This transform followed by <paramref name="next"/>, which is how a <c>cm</c> operator
        /// combines its operand with the current matrix.
        /// </summary>
        public Matrix Then(Matrix next) => new(
            A * next.A + B * next.C,
            A * next.B + B * next.D,
            C * next.A + D * next.C,
            C * next.B + D * next.D,
            E * next.A + F * next.C + next.E,
            E * next.B + F * next.D + next.F);
    }

    /// <summary>
    /// A link ready to be written: the rectangle is already in PDF default page space (origin
    /// bottom-left, y up) and the URI is already printable ASCII.
    /// </summary>
    internal readonly record struct LinkAnnotation(double Left, double Bottom, double Right, double Top, string Uri);
}
