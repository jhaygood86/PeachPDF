using System;

namespace PeachBrackets.WinUI;

/// <summary>The arithmetic of turning a document's cursor image into a pointer, apart from any window.</summary>
internal static class CursorImages
{
    /// <summary>Whether an address may be fetched for a pointer: the document may not reach other schemes (a local resource handler, an app URI).</summary>
    public static bool IsFetchable(string uri) =>
        uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" or "file");

    /// <summary>The size a pointer is drawn at: the image's own, scaled down (keeping its shape) to fit <paramref name="max"/> pixels a side.</summary>
    public static (int Width, int Height) Fit(int width, int height, int max)
    {
        if (width <= max && height <= max)
            return (Math.Max(1, width), Math.Max(1, height));

        var scale = (double)max / Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// The pointer's active point after scaling: the hotspot a style or a file gave (in the image's own pixels), moved with the scaling and held inside
    /// the pointer; the centre when neither gave one.
    /// </summary>
    public static (int X, int Y) Hotspot(double? x, double? y, int sourceWidth, int sourceHeight, int width, int height)
    {
        var hotX = ((x ?? (sourceWidth / 2.0)) * width) / sourceWidth;
        var hotY = ((y ?? (sourceHeight / 2.0)) * height) / sourceHeight;
        return (Math.Clamp((int)Math.Round(hotX), 0, width - 1), Math.Clamp((int)Math.Round(hotY), 0, height - 1));
    }

    /// <summary>The hotspot a <c>.cur</c> file carries in the header of its first image, or <see langword="null"/> when the bytes are not a cursor file.</summary>
    public static (int X, int Y)? CurHotspot(ReadOnlySpan<byte> file)
    {
        // ICONDIR: reserved 0, type 2 (cursor), count; then the first entry, whose "planes" and "bit count" fields hold the hotspot.
        if (file.Length < 22 || file[0] != 0 || file[1] != 0 || file[2] != 2 || file[3] != 0 || (file[4] | (file[5] << 8)) == 0)
            return null;

        return (file[10] | (file[11] << 8), file[12] | (file[13] << 8));
    }

    /// <summary>Reads the bytes out of a <c>data:</c> address (base64 or percent-encoded).</summary>
    public static bool TryDecodeDataUri(string uri, out byte[] data)
    {
        data = [];
        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        var comma = uri.IndexOf(',');
        if (comma < 0)
            return false;

        var header = uri[5..comma];
        var payload = uri[(comma + 1)..];
        try
        {
            data = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(Uri.UnescapeDataString(payload))
                : System.Text.Encoding.Latin1.GetBytes(Uri.UnescapeDataString(payload));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
