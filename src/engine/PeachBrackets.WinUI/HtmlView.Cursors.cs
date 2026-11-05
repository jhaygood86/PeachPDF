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
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", HostResourceLoader.UserAgent);
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxCursorBytes)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length <= MaxCursorBytes ? bytes : null;
    }
}
