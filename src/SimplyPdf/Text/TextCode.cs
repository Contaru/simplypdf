namespace SimplyPdf.Text;

/// <summary>
/// One unit of text in a font's own encoding: a WinAnsi byte for the standard fonts, a glyph id
/// for embedded ones. <paramref name="IsSpace"/> remembers that it came from a plain space, the
/// only place a line breaks and the only thing justification stretches; the code alone cannot
/// tell, because a font may draw a no-break space with the very same glyph.
/// </summary>
internal readonly record struct TextCode(ushort Value, bool IsSpace);
