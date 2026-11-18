namespace PeachReader;

/// <summary>A document another app asked the reader to open: the platform offers it here, the window takes it.</summary>
public static class IncomingDocument
{
    private static readonly object Gate = new();
    private static (byte[] Bytes, string? Name)? _pending;

    /// <summary>Raised when a document is offered while the window is already showing.</summary>
    public static event Action? Offered;

    /// <summary>Offers a document for opening.</summary>
    /// <param name="bytes">the bytes of the file</param>
    /// <param name="name">the file's name, when the platform knows it</param>
    public static void Offer(byte[] bytes, string? name)
    {
        lock (Gate) _pending = (bytes, name);
        Offered?.Invoke();
    }

    /// <summary>Takes the document that was offered, so it is opened once.</summary>
    /// <returns>the document, or <see langword="null"/> when none is waiting</returns>
    public static (byte[] Bytes, string? Name)? Take()
    {
        lock (Gate)
        {
            var taken = _pending;
            _pending = null;
            return taken;
        }
    }
}
