namespace SimplyPdf.Fonts;

/// <summary>
/// Maps .NET strings to WinAnsiEncoding (PDF 32000-1:2008, Annex D.2), the single-byte
/// encoding used with the standard fonts. It covers Latin-1 plus the usual typographic
/// extras (curly quotes, dashes, euro, trademark). Anything outside is replaced by
/// <see cref="Replacement"/>.
/// </summary>
internal static class WinAnsiEncoding
{
    /// <summary>Byte written for characters that WinAnsi cannot represent (a question mark).</summary>
    public const byte Replacement = (byte)'?';

    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        var length = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // Emoji and other astral characters: one replacement for the pair, not two.
                bytes[length++] = Replacement;
                i++;
                continue;
            }

            bytes[length++] = Encode(c);
        }

        return length == bytes.Length ? bytes : bytes[..length];
    }

    public static byte Encode(char c)
    {
        // ASCII and the Latin-1 supplement share their code points with WinAnsi.
        if (c < 0x80 || (c >= 0xA0 && c <= 0xFF))
        {
            return (byte)c;
        }

        // The 0x80-0x9F block is where WinAnsi and Unicode differ.
        return c switch
        {
            '€' => 0x80, // euro sign
            '‚' => 0x82, // single low-9 quotation mark
            'ƒ' => 0x83, // latin small letter f with hook
            '„' => 0x84, // double low-9 quotation mark
            '…' => 0x85, // horizontal ellipsis
            '†' => 0x86, // dagger
            '‡' => 0x87, // double dagger
            'ˆ' => 0x88, // modifier letter circumflex accent
            '‰' => 0x89, // per mille sign
            'Š' => 0x8A, // S with caron
            '‹' => 0x8B, // single left-pointing angle quotation mark
            'Œ' => 0x8C, // OE ligature
            'Ž' => 0x8E, // Z with caron
            '‘' => 0x91, // left single quotation mark
            '’' => 0x92, // right single quotation mark
            '“' => 0x93, // left double quotation mark
            '”' => 0x94, // right double quotation mark
            '•' => 0x95, // bullet
            '–' => 0x96, // en dash
            '—' => 0x97, // em dash
            '˜' => 0x98, // small tilde
            '™' => 0x99, // trade mark sign
            'š' => 0x9A, // s with caron
            '›' => 0x9B, // single right-pointing angle quotation mark
            'œ' => 0x9C, // oe ligature
            'ž' => 0x9E, // z with caron
            'Ÿ' => 0x9F, // Y with diaeresis
            '‐' or '‑' or '−' => (byte)'-', // hyphen, non-breaking hyphen, minus sign
            ' ' or ' ' => 0xA0, // figure space, narrow no-break space
            _ => Replacement,
        };
    }
}
