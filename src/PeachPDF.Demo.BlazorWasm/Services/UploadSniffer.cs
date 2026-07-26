using System.Text;

namespace PeachPDF.Demo.BlazorWasm.Services;

/// <summary>
/// Works out what an uploaded file is. The extension is the usual answer, but content wins where it is
/// unambiguous — a ZIP is a ZIP whatever it has been renamed to, and a browser-saved "web archive" turns up
/// under several extensions.
/// </summary>
public static class UploadSniffer
{
    private static readonly byte[][] ZipSignatures =
    [
        [0x50, 0x4B, 0x03, 0x04], // a normal archive
        [0x50, 0x4B, 0x05, 0x06], // empty
        [0x50, 0x4B, 0x07, 0x08], // spanned
    ];

    public static UploadKind Detect(string fileName, byte[] bytes)
    {
        // Content first, and only for the case where content is conclusive.
        if (ZipSignatures.Any(signature => StartsWith(bytes, signature)))
        {
            return UploadKind.Zip;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension is ".zip")
        {
            return UploadKind.Zip;
        }

        if (extension is ".mhtml" or ".mht" or ".eml")
        {
            return UploadKind.Mhtml;
        }

        if (extension is ".html" or ".htm" or ".xhtml")
        {
            return UploadKind.Html;
        }

        return LooksLikeMhtml(bytes) ? UploadKind.Mhtml : UploadKind.Html;
    }

    private static bool StartsWith(byte[] bytes, byte[] signature) =>
        bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);

    /// <summary>
    /// An MHTML archive opens with MIME headers. Only the head of the file is examined, and only before any
    /// markup starts — a plain HTML page that happens to mention these words later on is not an archive.
    /// </summary>
    private static bool LooksLikeMhtml(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var markup = head.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (markup >= 0)
        {
            head = head[..markup];
        }

        return head.Contains("MIME-Version:", StringComparison.OrdinalIgnoreCase)
               || head.Contains("multipart/related", StringComparison.OrdinalIgnoreCase);
    }
}
