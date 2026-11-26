using PeachDrawing;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PeachBrackets.Scripting.Internal.Canvas;

/// <summary>Writes straight (not premultiplied) RGBA pixels as a PNG file.</summary>
internal static class PngWriter
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, Crc(name, data));
        output.Write(number);
    }

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;
        header[9] = 6;
        Chunk(file, "IHDR", header);

        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            raw.WriteTo(z);

        Chunk(file, "IDAT", packed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }
}

/// <summary>Reading a canvas back as pixels, and asking where a path is.</summary>
internal static class CanvasPixelExtensions
{
    /// <summary>The canvas as straight RGBA, 4 bytes a pixel, rows from the top: the recording played onto a bitmap.</summary>
    public static byte[] RenderPixels(this CanvasBitmap bitmap)
    {
        // A canvas too large to read back has no pixels to give.
        if (bitmap.Width * bitmap.Height * 4 > (1 << 26))
            return [];

        var (width, height) = ((int)bitmap.Width, (int)bitmap.Height);
        var rgba = new byte[Math.Max(0, width) * Math.Max(0, height) * 4];
        if (width <= 0 || height <= 0)
            return rgba;

        try
        {
            var context = new RasterRenderContext();
            using var canvas = context.CreateCanvas(width, height);
            bitmap.ReplayGuarded(canvas);
            var buffer = canvas.ToPixelBuffer();
            var source = buffer.PremultipliedRgba.Span;
            for (var i = 0; i + 3 < rgba.Length && i + 3 < source.Length; i += 4)
            {
                var alpha = source[i + 3];
                rgba[i + 3] = alpha;
                for (var c = 0; c < 3; c++)
                    rgba[i + c] = alpha == 0 ? (byte)0 : alpha == 255 ? source[i + c] : (byte)Math.Min(255, (source[i + c] * 255 + alpha / 2) / alpha);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Array.Clear(rgba);
        }

        return rgba;
    }

    // Subdivides a path's curves into straight pieces; each subpath is a closed polygon.
    private static List<List<(double X, double Y)>> Polygons(PathData path)
    {
        var result = new List<List<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        (double X, double Y) last = default;
        foreach (var s in path.Segments)
        {
            switch (s.Type)
            {
                case PathData.Kind.Move:
                    current = [(s.X1, s.Y1)];
                    result.Add(current);
                    last = (s.X1, s.Y1);
                    break;
                case PathData.Kind.Line:
                    current?.Add((s.X1, s.Y1));
                    last = (s.X1, s.Y1);
                    break;
                case PathData.Kind.Curve:
                    for (var i = 1; i <= 16; i++)
                    {
                        var t = i / 16.0;
                        var u = 1 - t;
                        current?.Add((u * u * u * last.X + 3 * u * u * t * s.X1 + 3 * u * t * t * s.X2 + t * t * t * s.X3,
                            u * u * u * last.Y + 3 * u * u * t * s.Y1 + 3 * u * t * t * s.Y2 + t * t * t * s.Y3));
                    }

                    last = (s.X3, s.Y3);
                    break;
            }
        }

        return result;
    }

    /// <summary>Whether a point is inside the path under a fill rule.</summary>
    public static bool Contains(this PathData path, double x, double y, FillMode rule)
    {
        var winding = 0;
        var crossings = 0;
        foreach (var polygon in Polygons(path))
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Count]);
                if ((a.Y <= y) == (b.Y <= y))
                    continue;

                var cross = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                if (cross > x)
                {
                    crossings++;
                    winding += b.Y > a.Y ? 1 : -1;
                }
            }
        }

        return rule == FillMode.EvenOdd ? crossings % 2 == 1 : winding != 0;
    }

    /// <summary>Whether a point is within half a line width of the path (both in the space the path is in).</summary>
    public static bool Touches(this PathData path, double x, double y, double lineWidth)
    {
        var reach = lineWidth / 2;
        foreach (var polygon in Polygons(path))
        {
            for (var i = 0; i + 1 < polygon.Count; i++)
            {
                var (a, b) = (polygon[i], polygon[i + 1]);
                var (dx, dy) = (b.X - a.X, b.Y - a.Y);
                var length = dx * dx + dy * dy;
                var t = length == 0 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / length, 0, 1);
                var (px, py) = (a.X + t * dx - x, a.Y + t * dy - y);
                if (px * px + py * py <= reach * reach)
                    return true;
            }
        }

        return false;
    }
}
