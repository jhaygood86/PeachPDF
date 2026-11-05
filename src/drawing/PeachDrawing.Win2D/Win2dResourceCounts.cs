using System.Threading;

namespace PeachDrawing.Win2D;

/// <summary>
/// How many of the native resources a context's canvases create are alive right now. It exists so a test can prove that every layer, tile
/// and command list was released; nothing in the backend reads it.
/// </summary>
internal sealed class Win2dResourceCounts
{
    private int _layers;
    private int _tiles;
    private int _commandLists;
    private int _sessions;
    private int _nativeTargets;
    private int _textBitmaps;
    private int _lcdLayers;
    private int _clipSurfaces;

    /// <summary>Clip layers (<c>CanvasActiveLayer</c>) pushed and not yet popped.</summary>
    public int Layers => Volatile.Read(ref _layers);

    /// <summary>Tile and layer canvases (with their command lists) created and not yet disposed.</summary>
    public int Tiles => Volatile.Read(ref _tiles);

    /// <summary>Command lists created and not yet disposed.</summary>
    public int CommandLists => Volatile.Read(ref _commandLists);

    /// <summary>Drawing sessions this backend opened itself (not borrowed ones) and has not yet closed.</summary>
    public int Sessions => Volatile.Read(ref _sessions);

    /// <summary>Readers of a session's native target (with their staging bitmaps) made and not yet disposed.</summary>
    public int NativeTargets => Volatile.Read(ref _nativeTargets);

    /// <summary>Bitmaps of text runs the context keeps for reuse (not counted in <see cref="Total"/>: they live as long as the context, not a canvas).</summary>
    public int TextBitmaps => Volatile.Read(ref _textBitmaps);

    /// <summary>Scratch render targets the GPU text blend uses (not counted in <see cref="Total"/>: they live as long as the context).</summary>
    public int LcdLayers => Volatile.Read(ref _lcdLayers);

    /// <summary>Render targets geometry clips draw into (see <c>Win2dCanvas.BeginSurfaceClip</c>) and have not yet composited and released.</summary>
    public int ClipSurfaces => Volatile.Read(ref _clipSurfaces);

    public void ClipSurface(int delta) => Interlocked.Add(ref _clipSurfaces, delta);

    public void LcdLayer(int delta) => Interlocked.Add(ref _lcdLayers, delta);

    public void TextBitmap(int delta) => Interlocked.Add(ref _textBitmaps, delta);

    public void NativeTarget(int delta) => Interlocked.Add(ref _nativeTargets, delta);

    public void Layer(int delta) => Interlocked.Add(ref _layers, delta);

    public void Tile(int delta) => Interlocked.Add(ref _tiles, delta);

    public void CommandList(int delta) => Interlocked.Add(ref _commandLists, delta);

    public void Session(int delta) => Interlocked.Add(ref _sessions, delta);

    /// <summary>The sum of everything still alive.</summary>
    public int Total => Layers + Tiles + CommandLists + Sessions + NativeTargets + ClipSurfaces;
}
