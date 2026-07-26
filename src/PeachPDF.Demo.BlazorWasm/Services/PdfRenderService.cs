using System.Text;
using PeachPDF.Demo.BlazorWasm.Network;
using PeachPDF.Network;

namespace PeachPDF.Demo.BlazorWasm.Services;

/// <summary>An uploaded file, buffered so it can be read more than once and seeked.</summary>
/// <param name="Kind">What the file is.</param>
/// <param name="FileName">The name the browser reported, shown in the UI and used for the output name.</param>
/// <param name="Bytes">The file's contents.</param>
public sealed record UploadedDocument(UploadKind Kind, string FileName, byte[] Bytes);

/// <summary>
/// Renders an uploaded document to PDF, entirely in the browser.
/// </summary>
public sealed class PdfRenderService(FontLibrary fonts)
{
    /// <summary>
    /// Renders <paramref name="document"/> and returns the PDF bytes. <paramref name="onProgress"/> is
    /// called with a short status line as the render moves through its phases.
    /// </summary>
    public async Task<byte[]> RenderAsync(
        UploadedDocument document,
        PageSize pageSize,
        PageOrientation orientation,
        Func<string, Task>? onProgress = null)
    {
        // Not thread-safe, and font registrations live on the instance - so a fresh one per render, which
        // also means one document's @font-face fonts cannot leak into the next.
        var generator = new PdfGenerator();

        await Report(onProgress, "Loading fonts…");
        await fonts.RegisterAsync(generator, async registered =>
            await Report(onProgress, $"Loading fonts… ({registered}/{FontLibrary.FaceCount})"));

        var config = new PdfGenerateConfig
        {
            PageSize = pageSize,
            PageOrientation = orientation,
            // The whole point of the demo's sandbox: the document cannot reach the local file system, and
            // with no HTTP loader configured it cannot reach the network either. Everything it renders
            // comes from the file that was uploaded.
            AllowLocalFileAccess = false,
        };

        string? html = null;
        MemoryStream? archiveStream = null;

        try
        {
            switch (document.Kind)
            {
                case UploadKind.Html:
                    // DataUriNetworkLoader has no notion of a primary document, so the HTML is passed in
                    // directly rather than fetched from the loader.
                    config.NetworkLoader = new DataUriNetworkLoader();
                    html = DecodeText(document.Bytes);
                    break;

                case UploadKind.Mhtml:
                    // MimeKitNetworkLoader starts parsing in its constructor and does not own the stream,
                    // so this has to stay alive for the whole render.
                    archiveStream = new MemoryStream(document.Bytes, writable: false);
                    config.NetworkLoader = new MimeKitNetworkLoader(archiveStream);
                    break;

                case UploadKind.Zip:
                    config.NetworkLoader = ZipFileNetworkLoader.Create(document.Bytes);
                    break;
            }

            await Report(onProgress, "Rendering…");

            var pdf = await generator.GeneratePdf(html, config);

            using var output = new MemoryStream();
            pdf.Save(output);
            return output.ToArray();
        }
        finally
        {
            archiveStream?.Dispose();
        }
    }

    private static Task Report(Func<string, Task>? onProgress, string message) =>
        onProgress?.Invoke(message) ?? Task.CompletedTask;

    private static string DecodeText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
