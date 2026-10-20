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
/// or colour-key <c>/Mask</c>, or the fill colour of an image mask) and handed over as PNG. JPEG 2000, CCITT and JBIG2 data
/// are not decoded yet.
/// </summary>
internal static class PdfImageXObject
{
    /// <summary>Whether the image's pixels depend on the current fill colour (a stencil), so a decoded copy cannot be reused.</summary>
    public static bool IsStencil(PdfSourceDocument doc, PdfDictionary image) =>
        doc.Resolve(image.GetRaw("ImageMask")) is PdfBoolean { Value: true };

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
            if (stencil || image.GetRaw("SMask") is not null || image.GetRaw("Mask") is not null)
            {
                why = "DCT image with a mask";
                return null;
            }
            return FromStream(context, decoded.Data, out why);
        }
        if (decoded.ImageFilter is not null) { why = decoded.ImageFilter + " image"; return null; }

        int bpc = stencil ? 1 : doc.IntOf(image.GetRaw("BitsPerComponent") ?? image.GetRaw("BPC"));
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
        if (mw <= 0 || mh <= 0 || decoded.ImageFilter is not null) return;
        int bpc = soft ? doc.IntOf(mask.GetRaw("BitsPerComponent")) : 1;
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
