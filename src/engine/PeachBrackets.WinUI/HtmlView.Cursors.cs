using Microsoft.UI.Input;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    private const int MaxCursorPixels = 128;
    private const int MaxCursorBytes = 2 * 1024 * 1024;

    // The pointers made from the document's images, by address and hotspot; a null cursor marks an image that could not be used.
    private readonly Dictionary<string, (InputCursor? Cursor, IntPtr Handle)> _imageCursors = [];
    private int _cursorRequest;
    private string? _shownKey;

    // Shows the pointer a document asks for. A system pointer is assigned only when it changes (the framework restarts its pointer handling on every
    // assignment). A document may ask for an image first (`cursor: url(...) x y, pointer`): the first image that loads is shown, and until it has,
    // and when none can be used, the keyword's system pointer stands in.
    private void ShowCursor(DocumentCursor cursor)
    {
        var request = ++_cursorRequest;
        foreach (var image in cursor.Images)
        {
            var key = CursorKey(image);
            if (!_imageCursors.TryGetValue(key, out var made))
            {
                SetSystemCursor(cursor.Shape);
                _ = LoadCursorImagesAsync(cursor, request);
                return;
            }

            if (made.Cursor is not null)
            {
                SetCursor(key, made.Cursor);
                return;
            }
        }

        SetSystemCursor(cursor.Shape);
    }

    private static string CursorKey(CursorImage image) => $"{image.Uri}|{image.HotspotX}|{image.HotspotY}";

    private void SetSystemCursor(CursorShape shape)
    {
        var system = HtmlViewLogic.ToSystemCursor(shape);
        var key = "system:" + system;
        if (_shownKey != key)
            SetCursor(key, InputSystemCursor.Create(system));
    }

    private void SetCursor(string key, InputCursor cursor)
    {
        if (_shownKey == key)
            return;

        _shownKey = key;
        ProtectedCursor = cursor;
    }

    // Loads the images the document named, then shows the pointer if it still is the one asked for.
    private async Task LoadCursorImagesAsync(DocumentCursor cursor, int request)
    {
        foreach (var image in cursor.Images)
        {
            var key = CursorKey(image);
            if (!_imageCursors.ContainsKey(key))
                _imageCursors[key] = await TryCreateCursorAsync(image);
        }

        if (request == _cursorRequest)
            ShowCursor(cursor);
    }

    private void ReleaseCursors()
    {
        _shownKey = null;
        foreach (var (_, handle) in _imageCursors.Values)
            Win32Cursors.Destroy(handle);

        _imageCursors.Clear();
    }

    private static async Task<(InputCursor?, IntPtr)> TryCreateCursorAsync(CursorImage image)
    {
        try
        {
            if (!CursorImages.IsFetchable(image.Uri))
                return (null, IntPtr.Zero);

            var bytes = await FetchAsync(image.Uri);
            if (bytes is null)
                return (null, IntPtr.Zero);

            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            var (width, height) = CursorImages.Fit((int)decoder.PixelWidth, (int)decoder.PixelHeight, MaxCursorPixels);
            var transform = new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height, InterpolationMode = BitmapInterpolationMode.Fant };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

            // The hotspot is in the image's own pixels; a .cur file carries one of its own for when the style gives none.
            var own = CursorImages.CurHotspot(bytes);
            var (hotX, hotY) = CursorImages.Hotspot(image.HotspotX ?? own?.X, image.HotspotY ?? own?.Y, (int)decoder.PixelWidth, (int)decoder.PixelHeight, width, height);
            var handle = Win32Cursors.Create(pixels.DetachPixelData(), width, height, hotX, hotY);
            var cursor = Win32Cursors.ToInputCursor(handle);
            if (cursor is null)
            {
                Win32Cursors.Destroy(handle);
                return (null, IntPtr.Zero);
            }

            return (cursor, handle);
        }
        catch (Exception)
        {
            // An address that does not load, or bytes that are not an image: the keyword's pointer is used instead.
            return (null, IntPtr.Zero);
        }
    }

    private static async Task<byte[]?> FetchAsync(string uri)
    {
        if (CursorImages.TryDecodeDataUri(uri, out var data))
            return data.Length <= MaxCursorBytes ? data : null;

        var address = new Uri(uri);
        if (address.IsFile)
        {
            var info = new FileInfo(address.LocalPath);
            return info.Exists && info.Length <= MaxCursorBytes ? await File.ReadAllBytesAsync(address.LocalPath) : null;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxCursorBytes)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length <= MaxCursorBytes ? bytes : null;
    }
}

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
