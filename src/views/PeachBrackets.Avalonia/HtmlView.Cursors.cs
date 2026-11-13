using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PeachBrackets.Avalonia.Logic;
using PeachDrawing.Core;

namespace PeachBrackets.Avalonia;

// The CSS cursor property: keyword cursors are mapped in Logic/AvaloniaViewLogic.cs; this is for url() images.
public sealed partial class HtmlView
{
    private const int MaxCursorPixels = 128;
    private const int MaxCursorBytes = 2 * 1024 * 1024;

    // The pointers made from the document's images, by address and hotspot; a null cursor marks an image that could not be used.
    private readonly Dictionary<string, Cursor?> _imageCursors = [];
    private string? _shownImageKey;

    // The document cursor whose image is still being loaded, so the pointer can be shown when it arrives (cleared when a keyword pointer is shown).
    private DocumentCursor? _pendingImageCursor;

    // Shows the first image of a document's cursor that has loaded. True when one is shown; false when none is usable yet (the keyword's pointer
    // stands in, and a load that is under way shows the image when it finishes) or none can be.
    private bool TryShowImageCursor(DocumentCursor cursor)
    {
        foreach (var image in cursor.Images)
        {
            var key = CursorKey(image);
            if (!_imageCursors.TryGetValue(key, out var made))
            {
                _pendingImageCursor = cursor;
                _ = LoadCursorImagesAsync(cursor);
                return false;
            }

            if (made is null)
                continue;

            _pendingImageCursor = null;
            if (_shownImageKey != key || _shownCursor is not null)
            {
                _shownImageKey = key;
                _shownCursor = null;
                Cursor = made;
            }

            return true;
        }

        return false;
    }

    private static string CursorKey(CursorImage image) => $"{image.Uri}|{image.HotspotX}|{image.HotspotY}";

    // Loads the images the document named, then shows the pointer if it still is the one asked for.
    private async Task LoadCursorImagesAsync(DocumentCursor cursor)
    {
        var allowNetwork = AllowNetworkAccess;
        foreach (var image in cursor.Images)
        {
            var key = CursorKey(image);
            if (!_imageCursors.ContainsKey(key))
                _imageCursors[key] = await TryCreateCursorAsync(image, allowNetwork);
        }

        if (ReferenceEquals(_pendingImageCursor, cursor))
        {
            _pendingImageCursor = null;
            ShowCursor(cursor);
        }
    }

    internal void ReleaseCursors()
    {
        _pendingImageCursor = null;
        if (_shownImageKey is not null)
            Cursor = null;

        _shownImageKey = null;
        _shownCursor = null;
        foreach (var cursor in _imageCursors.Values)
            cursor?.Dispose();

        _imageCursors.Clear();
    }

    /// <summary>The image cursors made so far (an unusable image counts too). For tests.</summary>
    internal int ImageCursorCount => _imageCursors.Count;

    /// <summary>Makes a pointer from a cursor image, or null when the image cannot be fetched or decoded.</summary>
    internal static async Task<Cursor?> TryCreateCursorAsync(CursorImage image, bool allowNetwork)
    {
        try
        {
            if (!CursorImages.IsFetchable(image.Uri))
                return null;

            var bytes = await FetchCursorBytesAsync(image.Uri, allowNetwork);
            if (bytes is null)
                return null;

            using var stream = new MemoryStream(bytes, writable: false);
            using var decoded = new Bitmap(stream);
            var source = decoded.PixelSize;
            var (width, height) = CursorImages.Fit(source.Width, source.Height, MaxCursorPixels);

            // The hotspot is in the image's own pixels; a .cur file carries one of its own for when the style gives none.
            var own = CursorImages.CurHotspot(bytes);
            var (hotX, hotY) = CursorImages.Hotspot(image.HotspotX ?? own?.X, image.HotspotY ?? own?.Y, source.Width, source.Height, width, height);
            if (width == source.Width && height == source.Height)
                return new Cursor(new Bitmap(new MemoryStream(bytes, writable: false)), new PixelPoint(hotX, hotY));

            using var scaled = decoded.CreateScaledBitmap(new PixelSize(width, height));
            return new Cursor(scaled, new PixelPoint(hotX, hotY));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An address that does not load, or bytes that are not an image: the keyword's pointer is used instead.
            return null;
        }
    }

    private static async Task<byte[]?> FetchCursorBytesAsync(string uri, bool allowNetwork)
    {
        if (CursorImages.TryDecodeDataUri(uri, out var data))
            return data.Length <= MaxCursorBytes ? data : null;

        var address = new Uri(uri);
        if (address.IsFile)
        {
            var info = new FileInfo(address.LocalPath);
            return info.Exists && info.Length <= MaxCursorBytes ? await File.ReadAllBytesAsync(address.LocalPath) : null;
        }

        if (!allowNetwork)
            return null;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", HostResourceLoader.UserAgent);
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxCursorBytes)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length <= MaxCursorBytes ? bytes : null;
    }
}
