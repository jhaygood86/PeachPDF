using System.Net.Http;

namespace PeachPDF.Demo.BlazorWasm.Services;

/// <summary>
/// Supplies the fonts PeachPDF renders with. A browser has no discoverable system fonts at all —
/// <c>FontResolver.DiscoverSupportedFonts</c> deliberately returns nothing for a WebAssembly host — so an
/// application there must register its own or nothing can be measured, let alone drawn.
/// <para>
/// The font <i>bytes</i> are cached here rather than the registration, because registration is per
/// <see cref="PdfGenerator"/>: <c>AddFontFromStream</c> writes into that instance's own resolver, and the
/// app builds a fresh generator per render so one document's <c>@font-face</c> fonts cannot leak into the
/// next. The fetch happens once, on the first render, and is shared by every later one.
/// </para>
/// </summary>
public sealed class FontLibrary(HttpClient httpClient)
{
    /// <summary>
    /// The Liberation faces, in the order they are registered. Liberation Sans is deliberately first: it is
    /// what <c>DefaultFontResolver.DefaultFont</c> resolves to on a browser host, and registering it under exactly
    /// that name is what makes the engine's last-resort font a real font rather than a mapping.
    /// </summary>
    private static readonly string[] FontFiles =
    [
        "LiberationSans-Regular.woff",
        "LiberationSans-Bold.woff",
        "LiberationSans-Italic.woff",
        "LiberationSans-BoldItalic.woff",
        "LiberationSerif-Regular.woff",
        "LiberationSerif-Bold.woff",
        "LiberationSerif-Italic.woff",
        "LiberationSerif-BoldItalic.woff",
        "LiberationMono-Regular.woff",
        "LiberationMono-Bold.woff",
        "LiberationMono-Italic.woff",
        "LiberationMono-BoldItalic.woff",
    ];

    /// <summary>
    /// Where each family name a document might ask for is sent. PeachPDF's font-family mapping is consulted
    /// only when the requested family is not itself registered, and it is <b>single-hop</b> — so every entry
    /// has to name a real registered family directly. In particular the generic families all have to be
    /// re-pointed: <see cref="PdfGenerator"/>'s constructor ran before any of these fonts existed and
    /// mapped every one of them at the default font, i.e. at Liberation <i>Sans</i>, which is wrong for
    /// <c>serif</c> and <c>monospace</c>.
    /// </summary>
    private static readonly (string From, string To)[] FamilyMappings =
    [
        // Metrically compatible with their Microsoft counterparts, so a document laid out for these
        // renders at the same measure rather than merely in a similar-looking face.
        ("Arial", "Liberation Sans"),
        ("Helvetica", "Liberation Sans"),
        ("Helvetica Neue", "Liberation Sans"),
        ("Times New Roman", "Liberation Serif"),
        ("Times", "Liberation Serif"),
        ("Courier New", "Liberation Mono"),
        ("Courier", "Liberation Mono"),

        // CSS generic families.
        ("sans-serif", "Liberation Sans"),
        ("serif", "Liberation Serif"),
        ("monospace", "Liberation Mono"),
        // No script or display face is bundled; the nearest shipped family is an honest fallback, and
        // certainly better than the arbitrary one the resolver's own last resort would pick.
        ("cursive", "Liberation Serif"),
        ("fantasy", "Liberation Sans"),
        ("system-ui", "Liberation Sans"),
        ("ui-sans-serif", "Liberation Sans"),
        ("ui-serif", "Liberation Serif"),
        ("ui-monospace", "Liberation Mono"),
        ("ui-rounded", "Liberation Sans"),
        ("math", "Liberation Serif"),

        // Common families that are not metric matches - mapped only so a document naming them renders in
        // something sensible instead of falling through to the default.
        ("Calibri", "Liberation Sans"),
        ("Verdana", "Liberation Sans"),
        ("Tahoma", "Liberation Sans"),
        ("Segoe UI", "Liberation Sans"),
        ("Cambria", "Liberation Serif"),
        ("Georgia", "Liberation Serif"),
        ("Consolas", "Liberation Mono"),
    ];

    private Task<IReadOnlyList<byte[]>>? _fetch;

    /// <summary>The number of font faces this library registers, for progress reporting.</summary>
    public static int FaceCount => FontFiles.Length;

    /// <summary>
    /// Registers every bundled face on <paramref name="generator"/> and maps the family names a document is
    /// likely to ask for onto them. <paramref name="onProgress"/> is invoked with the number of faces
    /// registered so far.
    /// </summary>
    public async Task RegisterAsync(PdfGenerator generator, Func<int, Task>? onProgress = null)
    {
        var faces = await GetFacesAsync();

        for (var i = 0; i < faces.Count; i++)
        {
            using var stream = new MemoryStream(faces[i], writable: false);
            await generator.AddFontFromStream(stream);

            if (onProgress is not null)
            {
                await onProgress(i + 1);
            }
        }

        foreach (var (from, to) in FamilyMappings)
        {
            generator.AddFontFamilyMapping(from, to);
        }
    }

    /// <summary>
    /// Fetches the faces from the app's own origin, once. The <see cref="Task"/> itself is cached rather
    /// than the result, so two renders started in quick succession share one fetch instead of racing.
    /// </summary>
    private Task<IReadOnlyList<byte[]>> GetFacesAsync() => _fetch ??= FetchAsync();

    private async Task<IReadOnlyList<byte[]>> FetchAsync()
    {
        var faces = new List<byte[]>(FontFiles.Length);

        foreach (var file in FontFiles)
        {
            faces.Add(await httpClient.GetByteArrayAsync($"fonts/{file}"));
        }

        return faces;
    }
}
