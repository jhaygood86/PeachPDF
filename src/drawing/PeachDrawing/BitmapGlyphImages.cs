using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace PeachDrawing;

/// <summary>
/// The decoded image of a bitmap colour glyph (CBDT/sbix), made once per font, glyph and strike so a glyph
/// used many times on one canvas - a row of the same emoji - shares one decoded image.
/// </summary>
internal static class BitmapGlyphImages
{
    private static readonly ConditionalWeakTable<Typeface, Dictionary<(int GlyphId, int Ppem), DecodedImage>> Cache = new();

    public static DecodedImage? Get(Typeface typeface, int glyphId, EmbeddedBitmap glyph)
    {
        var images = Cache.GetOrCreateValue(typeface);
        lock (images)
        {
            if (!images.TryGetValue((glyphId, glyph.Ppem), out var image))
            {
                var decoded = DecodedImage.TryDecode(new MemoryStream(glyph.Data));
                if (decoded is null)
                    return null;

                image = decoded;
                images[(glyphId, glyph.Ppem)] = image;
            }

            return image;
        }
    }
}
