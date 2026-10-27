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

    /// <summary>Clip layers (<c>CanvasActiveLayer</c>) pushed and not yet popped.</summary>
    public int Layers => Volatile.Read(ref _layers);

    /// <summary>Tile and layer canvases (with their command lists) created and not yet disposed.</summary>
    public int Tiles => Volatile.Read(ref _tiles);

    /// <summary>Command lists created and not yet disposed.</summary>
    public int CommandLists => Volatile.Read(ref _commandLists);

    /// <summary>Drawing sessions this backend opened itself (not borrowed ones) and has not yet closed.</summary>
    public int Sessions => Volatile.Read(ref _sessions);

    public void Layer(int delta) => Interlocked.Add(ref _layers, delta);

    public void Tile(int delta) => Interlocked.Add(ref _tiles, delta);

    public void CommandList(int delta) => Interlocked.Add(ref _commandLists, delta);

    public void Session(int delta) => Interlocked.Add(ref _sessions, delta);

    /// <summary>The sum of everything still alive.</summary>
    public int Total => Layers + Tiles + CommandLists + Sessions;
}
