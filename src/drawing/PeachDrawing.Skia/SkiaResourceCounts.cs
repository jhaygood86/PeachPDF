using System.Threading;

namespace PeachDrawing.Skia;

/// <summary>
/// How many of the native resources a context's canvases create are alive right now. It exists so a test can prove that every picture
/// recorder, picture and layer was released; nothing in the backend reads it.
/// </summary>
internal sealed class SkiaResourceCounts
{
    private int _layers;
    private int _tiles;
    private int _recorders;
    private int _surfaces;

    /// <summary>Isolated groups (<c>SaveLayer</c>) begun and not yet finished.</summary>
    public int Layers => Volatile.Read(ref _layers);

    /// <summary>Tile canvases (with their pictures) created and not yet disposed.</summary>
    public int Tiles => Volatile.Read(ref _tiles);

    /// <summary>Picture recorders created and not yet disposed.</summary>
    public int Recorders => Volatile.Read(ref _recorders);

    /// <summary>Images and bitmaps this backend allocated itself (never the caller's) and has not yet released.</summary>
    public int Surfaces => Volatile.Read(ref _surfaces);

    public void Layer(int delta) => Interlocked.Add(ref _layers, delta);

    public void Tile(int delta) => Interlocked.Add(ref _tiles, delta);

    public void Recorder(int delta) => Interlocked.Add(ref _recorders, delta);

    public void Surface(int delta) => Interlocked.Add(ref _surfaces, delta);

    /// <summary>The sum of everything still alive.</summary>
    public int Total => Layers + Tiles + Recorders + Surfaces;
}
