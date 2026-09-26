using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace PeachPDF.Adapters;

/// <summary>
/// The <see cref="XImage"/> of a bitmap colour glyph (CBDT/sbix), made once per font, glyph and strike so a glyph used many times in a
/// document - a row of the same emoji - shares one image object.
/// </summary>
internal static class BitmapGlyphImages
{
    private static readonly ConditionalWeakTable<OpenTypeDescriptor, Dictionary<(int GlyphId, int Ppem), XImage>> Cache = new();

    public static XImage Get(OpenTypeDescriptor descriptor, int glyphId, BitmapGlyph glyph)
    {
        var images = Cache.GetOrCreateValue(descriptor);
        lock (images)
        {
            if (!images.TryGetValue((glyphId, glyph.Ppem), out var image))
            {
                var data = glyph.Data;
                image = XImage.FromStream(() => new MemoryStream(data));
                images[(glyphId, glyph.Ppem)] = image;
            }

            return image;
        }
    }
}
