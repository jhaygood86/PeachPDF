using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Turns an image XObject into a <see cref="PeachDrawing.Core.Image"/> a canvas can draw: JPEG data passes through
/// untouched, anything else is unpacked (bit depth, <c>/Decode</c>, colour space), given its alpha (<c>/SMask</c>, a stencil
/// or colour-key <c>/Mask</c>, or the fill colour of an image mask) and handed over as PNG. JPEG 2000 is decoded to pixels; CCITT
/// fax and JBIG2 data are decoded to the one-bit samples the filters produce and then read like any other 1-bit image.
/// </summary>
internal static class PdfImageXObject
{
    /// <summary>Whether the image's pixels depend on the current fill colour (a stencil), so a decoded copy cannot be reused.</summary>
    public static bool IsStencil(PdfSourceDocument doc, PdfDictionary image) =>
        doc.Resolve(image.GetRaw("ImageMask") ?? image.GetRaw("IM")) is PdfBoolean { Value: true };

    /// <summary>Decodes <paramref name="image"/>; returns null and a reason in <paramref name="why"/> when it cannot be.</summary>
    public static Image? Decode(PdfSourceDocument doc, PdfDictionary image, PdfDictionary? resources, PaintColor fill, RenderContext context, out string? why)
    {
        why = null;
        int width = doc.IntOf(image.GetRaw("Width") ?? image.GetRaw("W"));
        int height = doc.IntOf(image.GetRaw("Height") ?? image.GetRaw("H"));
        if (width <= 0 || height <= 0 || (long)width * height > 100_000_000) { why = "image size"; return null; }

        var decoded = doc.DecodeStream(image);
        bool stencil = IsStencil(doc, image);

        if (decoded.ImageFilter == "DCTDecode")
        {
            if (stencil) { why = "DCT stencil mask"; return null; }
            if (image.GetRaw("SMask") is null && image.GetRaw("Mask") is null)
                return FromStream(context, decoded.Data, out why);
            return FromMaskedJpeg(doc, image, resources, context, decoded.Data, out why);
        }
        if (decoded.ImageFilter == "JPXDecode")
        {
            if (stencil) { why = "JPX stencil mask"; return null; }
            return FromJpx(doc, image, resources, context, decoded.Data, out why);
        }
        bool bilevel = decoded.ImageFilter is "CCITTFaxDecode" or "JBIG2Decode";
        if (bilevel)
        {
            var bits = DecodeBilevel(doc, decoded, width, height);
            if (bits is null) { why = decoded.ImageFilter + " image"; return null; }
            decoded = decoded with { Data = bits, ImageFilter = null };
        }
        else if (decoded.ImageFilter is not null) { why = decoded.ImageFilter + " image"; return null; }

        int bpc = stencil || bilevel ? 1 : doc.IntOf(image.GetRaw("BitsPerComponent") ?? image.GetRaw("BPC"));
        if (bpc is not (1 or 2 or 4 or 8 or 16)) { why = "bits per component"; return null; }
        double[]? decode = null;
        if (doc.Resolve(image.GetRaw("Decode") ?? image.GetRaw("D")) is PdfArray da)
        {
            decode = new double[da.Elements.Count];
            for (int i = 0; i < decode.Length; i++) decode[i] = doc.NumberOf(da.Elements[i]);
        }

        var rgba = new byte[width * height * 4];
        if (stencil)
        {
            var fillRgb = fill.ToRgb();   // a CMYK colour keeps its ink values in R/G/B untouched, so ask for the RGB rendition
            bool invert = decode is { Length: >= 2 } && decode[0] > decode[1];
            int stride = (width + 7) / 8;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int byteIndex = y * stride + (x >> 3);
                    int bit = byteIndex < decoded.Data.Length ? (decoded.Data[byteIndex] >> (7 - (x & 7))) & 1 : 1;
                    bool painted = invert ? bit == 1 : bit == 0;
                    int o = (y * width + x) * 4;
                    rgba[o] = fillRgb.R; rgba[o + 1] = fillRgb.G; rgba[o + 2] = fillRgb.B; rgba[o + 3] = painted ? fill.A : (byte)0;
                }
            return FromPixels(context, rgba, width, height, out why);
        }

        var space = PdfColorSpace.Resolve(doc, image.GetRaw("ColorSpace") ?? image.GetRaw("CS"), resources);
        if (space is null) { why = "image colour space"; return null; }
        int comps = space.Components;
        bool indexed = space.Name == "Indexed";
        int rowBytes = (width * comps * bpc + 7) / 8;
        double max = (1 << Math.Min(bpc, 16)) - 1;
        var samples = new double[comps];
        var raw = new int[comps];
        int[]? keyMask = null;
        if (doc.Resolve(image.GetRaw("Mask")) is PdfArray ma && ma.Elements.Count >= comps * 2)
        {
            keyMask = new int[comps * 2];
            for (int i = 0; i < keyMask.Length; i++) keyMask[i] = doc.IntOf(ma.Elements[i]);
        }
        var data = decoded.Data;
        if (space == PdfColorSpace.Cmyk && keyMask is null && image.GetRaw("SMask") is null && image.GetRaw("Mask") is null)
        {
            // A CMYK image stays CMYK: it goes to the canvas as a CMYK TIFF, which a PDF canvas embeds as a DeviceCMYK image rather
            // than a converted RGB one, so the viewer's own CMYK conversion applies exactly as it did to the original.
            var cmyk = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int v = ReadSample(data, y * rowBytes, (x * 4 + c) * bpc, bpc);
                        double d0 = decode is not null && decode.Length >= c * 2 + 2 ? decode[c * 2] : 0;
                        double d1 = decode is not null && decode.Length >= c * 2 + 2 ? decode[c * 2 + 1] : 1;
                        cmyk[(y * width + x) * 4 + c] = (byte)Math.Round(Math.Clamp(d0 + v / max * (d1 - d0), 0, 1) * 255);
                    }
            var tiff = FromStream(context, EncodeCmykTiff(cmyk, width, height), out why);
            if (tiff is not null) return tiff;
            why = null;                                                     // not decodable as a TIFF: fall through to the RGB conversion
        }

        if (space.Bulk is { } bulk && bpc == 8 && decode is null && keyMask is null)
        {
            // An ICC-based image is converted whole: one call to the profile engine rather than one per pixel.
            var device = new byte[width * height * comps];
            for (int y = 0; y < height; y++)
            {
                int available = Math.Max(0, Math.Min(rowBytes, data.Length - y * rowBytes));
                Buffer.BlockCopy(data, y * rowBytes, device, y * width * comps, available);
            }
            var converted = new byte[width * height * 3];
            bulk(device, converted, width * height);
            for (int i = 0; i < width * height; i++)
            {
                rgba[i * 4] = converted[i * 3]; rgba[i * 4 + 1] = converted[i * 3 + 1]; rgba[i * 4 + 2] = converted[i * 3 + 2]; rgba[i * 4 + 3] = 255;
            }
            if (doc.Resolve(image.GetRaw("SMask")) is PdfDictionary bulkSoft) ApplyAlpha(doc, bulkSoft, rgba, width, height, resources, soft: true);
            else if (doc.Resolve(image.GetRaw("Mask")) is PdfDictionary bulkMask) ApplyAlpha(doc, bulkMask, rgba, width, height, resources, soft: false);
            return FromPixels(context, rgba, width, height, out why);
        }

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < comps; c++)
                {
                    int bitPos = (x * comps + c) * bpc;
                    int v = ReadSample(data, rowStart, bitPos, bpc);
                    raw[c] = v;
                    double d0 = decode is not null && decode.Length >= c * 2 + 2 ? decode[c * 2] : 0;
                    double d1 = decode is not null && decode.Length >= c * 2 + 2 ? decode[c * 2 + 1] : (indexed ? max : 1);
                    samples[c] = indexed ? d0 + v * (d1 - d0) / max : d0 + v / max * (d1 - d0);
                }
                var color = space.ToColor(samples, 1);
                int o = (y * width + x) * 4;
                var (cr, cg, cb) = color.ToRgb();
                rgba[o] = cr; rgba[o + 1] = cg; rgba[o + 2] = cb; rgba[o + 3] = 255;
                if (keyMask is not null)
                {
                    bool masked = true;
                    for (int c = 0; c < comps && masked; c++) masked = raw[c] >= keyMask[c * 2] && raw[c] <= keyMask[c * 2 + 1];
                    if (masked) rgba[o + 3] = 0;
                }
            }
        }

        if (doc.Resolve(image.GetRaw("SMask")) is PdfDictionary smask) ApplyAlpha(doc, smask, rgba, width, height, resources, soft: true);
        else if (doc.Resolve(image.GetRaw("Mask")) is PdfDictionary stencilMask) ApplyAlpha(doc, stencilMask, rgba, width, height, resources, soft: false);

        return FromPixels(context, rgba, width, height, out why);
    }

    static int ReadSample(byte[] data, int rowStart, int bitPos, int bpc)
    {
        int index = rowStart + (bitPos >> 3);
        if (index >= data.Length) return 0;
        switch (bpc)
        {
            case 8: return data[index];
            case 16: return index + 1 < data.Length ? (data[index] << 8) | data[index + 1] : data[index] << 8;
            default:
                int shift = 8 - bpc - (bitPos & 7);
                return (data[index] >> shift) & ((1 << bpc) - 1);
        }
    }

    /// <summary>Sets the alpha of <paramref name="rgba"/> from a soft-mask image or a stencil mask, sampling the mask nearest-neighbour onto the image grid.</summary>
    static void ApplyAlpha(PdfSourceDocument doc, PdfDictionary mask, byte[] rgba, int width, int height, PdfDictionary? resources, bool soft)
    {
        int mw = doc.IntOf(mask.GetRaw("Width")), mh = doc.IntOf(mask.GetRaw("Height"));
        var decoded = doc.DecodeStream(mask);
        bool bilevel = decoded.ImageFilter is "CCITTFaxDecode" or "JBIG2Decode";
        if (mw > 0 && mh > 0 && bilevel)
        {
            if (DecodeBilevel(doc, decoded, mw, mh) is not { } bits) return;
            decoded = decoded with { Data = bits, ImageFilter = null };
        }
        if (mw <= 0 || mh <= 0 || decoded.ImageFilter is not null) return;
        int bpc = soft && !bilevel ? doc.IntOf(mask.GetRaw("BitsPerComponent")) : 1;
        if (bpc is not (1 or 2 or 4 or 8 or 16)) return;
        bool invert = doc.Resolve(mask.GetRaw("Decode")) is PdfArray { Elements.Count: >= 2 } d && doc.NumberOf(d.Elements[0]) > doc.NumberOf(d.Elements[1]);
        int rowBytes = (mw * bpc + 7) / 8;
        double max = (1 << Math.Min(bpc, 16)) - 1;
        for (int y = 0; y < height; y++)
        {
            int my = Math.Min(mh - 1, y * mh / height);
            for (int x = 0; x < width; x++)
            {
                int mx = Math.Min(mw - 1, x * mw / width);
                int v = ReadSample(decoded.Data, my * rowBytes, mx * bpc, bpc);
                double a = v / max;
                // A stencil mask: sample 1 means "masked out" unless /Decode flips it.
                if (!soft) a = 1 - a;
                if (invert) a = 1 - a;
                int o = (y * width + x) * 4 + 3;
                rgba[o] = (byte)Math.Round(Math.Clamp(a, 0, 1) * rgba[o]);
            }
        }
    }

    /// <summary>
    /// Decodes a CCITT fax or JBIG2 stream to what the PDF filter produces: one bit per pixel, rows padded to a byte, 0 for black
    /// unless the fax filter's <c>/BlackIs1</c> says otherwise. Null when the data cannot be decoded.
    /// </summary>
    static byte[]? DecodeBilevel(PdfSourceDocument doc, PdfDecodedStream decoded, int width, int height)
    {
        var parms = decoded.ImageParameters;
        PeachImage.DecoderOptions options;
        if (decoded.ImageFilter == "CCITTFaxDecode")
        {
            bool Flag(string key, bool fallback) => parms is not null && doc.Resolve(parms.GetRaw(key)) is PdfBoolean flag ? flag.Value : fallback;
            int Int(string key, int fallback) => parms is not null && parms.GetRaw(key) is { } v && doc.Resolve(v) is PdfNumber ? doc.IntOf(v) : fallback;
            int columns = Int("Columns", 1728);
            int rows = Int("Rows", 0);
            options = new PeachImage.Formats.Ccitt.CcittFaxDecoderOptions
            {
                K = Int("K", 0),
                Columns = columns,
                Rows = rows > 0 ? rows : height,
                EndOfLine = Flag("EndOfLine", false),
                EncodedByteAlign = Flag("EncodedByteAlign", false),
                EndOfBlock = Flag("EndOfBlock", true),
                BlackIs1 = Flag("BlackIs1", false),
            };
        }
        else
        {
            byte[]? globals = parms is not null && doc.Resolve(parms.GetRaw("JBIG2Globals")) is PdfDictionary g ? doc.DecodeStream(g).Data : null;
            options = new PeachImage.Formats.Jbig2.Jbig2DecoderOptions { Embedded = true, Globals = globals };
        }

        try
        {
            using var gray = PeachImage.Image.Load(new MemoryStream(decoded.Data), options);
            if (gray.PixelFormat != PeachImage.PixelFormat.Gray8) return null;
            var pixels = gray.PixelMemory.Span;
            int stride = (width + 7) / 8;
            var packed = new byte[stride * height];
            int rowsAvailable = Math.Min(height, gray.Height), columnsAvailable = Math.Min(width, gray.Width);
            // A row the data did not reach is white (1), as the filter would leave it.
            Array.Fill(packed, (byte)0xFF);
            for (int y = 0; y < rowsAvailable; y++)
                for (int x = 0; x < columnsAvailable; x++)
                {
                    int mask = 0x80 >> (x & 7);
                    if (pixels[y * gray.Width + x] >= 128) packed[y * stride + (x >> 3)] |= (byte)mask;
                    else packed[y * stride + (x >> 3)] &= (byte)~mask;
                }
            return packed;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A JPEG 2000 image: decoded to RGBA (the codestream carries its own colour), then given the image's soft or stencil mask.</summary>
    static Image? FromJpx(PdfSourceDocument doc, PdfDictionary image, PdfDictionary? resources, RenderContext context, byte[] jpx, out string? why)
    {
        why = null;
        byte[] rgba;
        int width, height;
        try
        {
            if (!PeachImage.Image.TryLoad(new MemoryStream(jpx), out var decoded, new PeachImage.DecoderOptions { TargetPixelFormat = PeachImage.PixelFormat.Rgba32 }) || decoded is null)
            {
                why = "image data could not be decoded";
                return null;
            }

            using (decoded)
            {
                (width, height) = (decoded.Width, decoded.Height);
                rgba = decoded.PixelMemory.Span.ToArray();
            }
        }
        catch (Exception)
        {
            why = "image data could not be decoded";
            return null;
        }

        // The codestream's own alpha channel counts only when the dictionary asks for it (/SMaskInData).
        if (doc.IntOf(image.GetRaw("SMaskInData")) == 0)
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;

        if (doc.Resolve(image.GetRaw("SMask")) is PdfDictionary soft) ApplyAlpha(doc, soft, rgba, width, height, resources, soft: true);
        else if (doc.Resolve(image.GetRaw("Mask")) is PdfDictionary stencilMask) ApplyAlpha(doc, stencilMask, rgba, width, height, resources, soft: false);
        return FromPixels(context, rgba, width, height, out why);
    }

    /// <summary>A JPEG with a soft mask or stencil mask: its pixels are decoded so the mask can become the alpha channel.</summary>
    static Image? FromMaskedJpeg(PdfSourceDocument doc, PdfDictionary image, PdfDictionary? resources, RenderContext context, byte[] jpeg, out string? why)
    {
        why = null;
        byte[] rgba;
        int width, height;
        try
        {
            if (!PeachImage.Image.TryLoad(new MemoryStream(jpeg), out var decoded, new PeachImage.DecoderOptions { TargetPixelFormat = PeachImage.PixelFormat.Rgba32 }) || decoded is null)
            {
                why = "image data could not be decoded";
                return null;
            }

            using (decoded)
            {
                (width, height) = (decoded.Width, decoded.Height);
                rgba = decoded.PixelMemory.Span.ToArray();
            }
        }
        catch (Exception)
        {
            why = "image data could not be decoded";
            return null;
        }

        if (doc.Resolve(image.GetRaw("SMask")) is PdfDictionary soft) ApplyAlpha(doc, soft, rgba, width, height, resources, soft: true);
        else if (doc.Resolve(image.GetRaw("Mask")) is PdfDictionary stencilMask) ApplyAlpha(doc, stencilMask, rgba, width, height, resources, soft: false);
        return FromPixels(context, rgba, width, height, out why);
    }

    static Image? FromStream(RenderContext context, byte[] bytes, out string? why)
    {
        why = null;
        try { return context.ImageFromStream(new MemoryStream(bytes)); }
        catch (Exception)
        {
            why = "image data could not be decoded";
            return null;
        }
    }

    static Image? FromPixels(RenderContext context, byte[] rgba, int width, int height, out string? why) =>
        FromStream(context, EncodePng(rgba, width, height), out why);

    /// <summary>An uncompressed 8-bit CMYK (separated) TIFF.</summary>
    internal static byte[] EncodeCmykTiff(byte[] cmyk, int width, int height)
    {
        const int entries = 11;
        int ifdOffset = 8;
        int bitsOffset = ifdOffset + 2 + entries * 12 + 4;
        int dataOffset = bitsOffset + 8;
        var o = new List<byte>();
        void U16(int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        void U32(long v) { o.Add((byte)v); o.Add((byte)(v >> 8)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 24)); }
        void Entry(int tag, int type, int count, long value) { U16(tag); U16(type); U32(count); if (type == 3 && count == 1) { U16((int)value); U16(0); } else U32(value); }

        o.AddRange("II"u8.ToArray());
        U16(42);
        U32(ifdOffset);
        U16(entries);
        Entry(256, 4, 1, width);                         // ImageWidth
        Entry(257, 4, 1, height);                        // ImageLength
        Entry(258, 3, 4, bitsOffset);                    // BitsPerSample: 8 8 8 8
        Entry(259, 3, 1, 1);                             // Compression: none
        Entry(262, 3, 1, 5);                             // Photometric: separated
        Entry(273, 4, 1, dataOffset);                    // StripOffsets
        Entry(277, 3, 1, 4);                             // SamplesPerPixel
        Entry(278, 4, 1, height);                        // RowsPerStrip
        Entry(279, 4, 1, cmyk.Length);                   // StripByteCounts
        Entry(284, 3, 1, 1);                             // PlanarConfiguration: chunky
        Entry(332, 3, 1, 1);                             // InkSet: CMYK
        U32(0);                                          // no further IFD
        for (int i = 0; i < 4; i++) U16(8);
        o.AddRange(cmyk);
        return o.ToArray();
    }

    // ------------------------------------------------------------ PNG

    /// <summary>Encodes straight (non-premultiplied) RGBA8 pixels as a PNG.</summary>
    internal static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0; // filter: none
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, raw.Length);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, width); WriteBe(ihdr, 4, height);
        ihdr[8] = 8; ihdr[9] = 6; // 8 bits, RGBA
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", idat.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static void WriteBe(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, body, 4, data.Length);
        s.Write(body);
        var crc = new byte[4];
        WriteBe(crc, 0, unchecked((int)Crc32(body)));
        s.Write(crc);
    }

    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
