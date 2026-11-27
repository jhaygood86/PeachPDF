using PeachDrawing.Core;
using System;
using System.Threading;

namespace PeachBrackets.Media;

/// <summary>
/// One picture of a video, held by whoever acquired it until it is disposed. The <see cref="Image"/> is upright and in RGB: the provider has applied the
/// stream's rotation, its YUV matrix and its range. A provider on a graphics device makes it a texture on that device (an <see cref="Image"/> its
/// backend made), so drawing it needs no copy; otherwise it is an image of pixels. A provider may reuse the storage behind a frame once every lease on it
/// is disposed, and not before.
/// </summary>
public sealed class MediaFrame : IDisposable
{
    private Action? _release;

    /// <summary>Makes a frame.</summary>
    /// <param name="image">the picture</param>
    /// <param name="time">where in the media the picture belongs</param>
    /// <param name="release">called once when the frame is disposed, to give back what the picture is made of</param>
    public MediaFrame(Image image, TimeSpan time, Action? release = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image = image;
        Time = time;
        _release = release;
    }

    /// <summary>The picture.</summary>
    public Image Image { get; }

    /// <summary>
    /// The RGB space the picture is in. A picture of pixels is converted from it when it is drawn on a canvas of another space; sRGB (the default) is
    /// taken as it is.
    /// </summary>
    public MediaColorSpace ColorSpace { get; init; }

    /// <summary>Where in the media the picture belongs. Frames of one session with the same time are the same picture: the document paints again only when it changes.</summary>
    public TimeSpan Time { get; }

    /// <summary>Gives the frame back to its provider.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
