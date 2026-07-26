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
        var extension = Path.GetExtension(entryName).TrimStart('.').ToLowerInvariant();

        return extension switch
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
}
