namespace PeachPDF.Demo.BlazorWasm.Network;

/// <summary>
/// Maps a zip entry's file extension to the <c>Content-Type</c> the renderer is told about.
/// <para>
/// This is load-bearing rather than decoration: PeachPDF's stylesheet loader <b>drops</b> any response
/// whose type is not <c>text/css</c>, and its image loader picks the SVG path from
/// <c>image/svg+xml</c>. Get a type wrong here and the asset silently does nothing.
/// </para>
/// <para>
/// PeachPDF has its own resolver for this, but it is internal to the library and consults the host OS —
/// neither of which suits a zip being read inside a browser, so the demo carries this small table instead.
/// The raster list matches what the library's image decoder actually understands; notably there is no
/// WebP entry, because it cannot decode WebP at all.
/// </para>
/// </summary>
internal static class ZipMimeTypes
{
    public static string GetMimeType(string entryName)
    {
        return ExtensionOf(entryName) switch
        {
            "html" or "htm" or "xhtml" => "text/html",
            "css" => "text/css",
            "js" or "mjs" => "text/javascript",
            "json" => "application/json",
            "xml" => "application/xml",
            "txt" => "text/plain",

            "svg" => "image/svg+xml",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tga" => "image/x-tga",
            "psd" => "image/vnd.adobe.photoshop",
            "hdr" => "image/vnd.radiance",
            "ico" => "image/x-icon",

            "ttf" => "font/ttf",
            "otf" => "font/otf",
            "woff" => "font/woff",
            "woff2" => "font/woff2",

            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// The lowercased extension of a zip entry name, without the dot.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Path.GetExtension</c>. A zip entry name is always <c>/</c>-separated by
    /// specification and is not a path on this machine — nothing in this app opens a file, least of all in
    /// a browser, where there is no file system to open one on. <c>Path.GetExtension</c> would apply the
    /// host's own separator rules to it (on Windows it treats <c>\</c> as one), which is simply the wrong
    /// question to ask of an archive entry.
    /// </remarks>
    private static string ExtensionOf(string entryName)
    {
        var lastSeparator = entryName.LastIndexOf('/');
        var fileName = lastSeparator < 0 ? entryName : entryName[(lastSeparator + 1)..];
        var dot = fileName.LastIndexOf('.');

        return dot < 0 ? string.Empty : fileName[(dot + 1)..].ToLowerInvariant();
    }
}
