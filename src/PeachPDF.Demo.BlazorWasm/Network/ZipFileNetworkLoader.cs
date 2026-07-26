using System.IO.Compression;
using System.Text;
using PeachPDF.Network;

namespace PeachPDF.Demo.BlazorWasm.Network;

/// <summary>
/// Serves a document and its assets out of an uploaded ZIP archive, so a whole site folder can be rendered
/// without anything ever being fetched or read from disk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a synthetic <c>https:</c> base URI.</b> The base cannot be <c>file:</c> or <c>data:</c> — PeachPDF's
/// adapter intercepts both schemes before a loader ever sees them. A registered scheme also buys correct
/// URI handling for free: <c>System.Uri</c> collapses <c>..</c> segments, strips the query and fragment
/// from <c>AbsolutePath</c> (so <c>style.css?v=2</c> resolves), and canonicalises percent-encoding. An
/// invented <c>zip://</c> scheme would fall to the generic parser, which does none of that.
/// <c>zip.invalid</c> can never resolve — <c>.invalid</c> is reserved for exactly this — and any request
/// whose scheme or host is not the synthetic one is refused outright, so a document referencing a real CDN
/// gets nothing back by construction rather than by policy.
/// </para>
/// <para>
/// <b>Why the entries are read eagerly.</b> A <see cref="ZipArchiveEntry"/> opens as a forward-only
/// decompression stream, while the image and font decoders want to seek; and holding the archive would
/// mean holding the upload's stream for the lifetime of the render. Reading each entry once up front
/// avoids both, and lets the archive be size-checked before anything is rendered.
/// </para>
/// </remarks>
internal sealed class ZipFileNetworkLoader : RNetworkLoader
{
    private const string SyntheticHost = "zip.invalid";
    private const string SyntheticScheme = "https";

    /// <summary>Entry-point document names, in the order they are looked for.</summary>
    private static readonly string[] PrimaryDocumentNames =
        ["index.html", "index.htm", "default.html", "default.htm"];

    // A zip bomb would otherwise be a plain denial of service on the browser tab.
    private const long MaxTotalUncompressedBytes = 256L * 1024 * 1024;
    private const long MaxEntryUncompressedBytes = 64L * 1024 * 1024;

    private readonly Dictionary<string, byte[]> _entries;
    private readonly Dictionary<string, byte[]> _entriesIgnoringCase;
    private readonly string _primaryDocumentPath;

    private ZipFileNetworkLoader(
        Dictionary<string, byte[]> entries,
        Dictionary<string, byte[]> entriesIgnoringCase,
        string primaryDocumentPath)
    {
        _entries = entries;
        _entriesIgnoringCase = entriesIgnoringCase;
        _primaryDocumentPath = primaryDocumentPath;
    }

    /// <summary>
    /// The primary document's own synthetic URI, mirroring how a file loader bases relative references on
    /// the loaded file rather than on its directory. RFC 3986 resolution drops the last segment, so an
    /// <c>index.html</c> nested inside a folder still resolves its siblings correctly, and a
    /// <c>&lt;base href&gt;</c> in the document composes with it as it would anywhere else.
    /// </summary>
    public override RUri? BaseUri =>
        new($"{SyntheticScheme}://{SyntheticHost}/{_primaryDocumentPath}");

    /// <summary>
    /// Reads every entry of <paramref name="zipBytes"/> into memory and locates the document to render.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The archive holds no recognisable entry-point document, or is too large to read safely.
    /// </exception>
    public static ZipFileNetworkLoader Create(byte[] zipBytes)
    {
        using var buffer = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var duplicateIgnoringCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesIgnoringCase = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            // Directory entries have an empty Name; there is nothing to serve for them.
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (entry.Length > MaxEntryUncompressedBytes)
            {
                throw new InvalidOperationException(
                    $"'{entry.FullName}' is larger than the {MaxEntryUncompressedBytes / 1024 / 1024} MB per-file limit.");
            }

            total += entry.Length;
            if (total > MaxTotalUncompressedBytes)
            {
                throw new InvalidOperationException(
                    $"The archive expands to more than the {MaxTotalUncompressedBytes / 1024 / 1024} MB limit.");
            }

            using var entryStream = entry.Open();
            using var contents = new MemoryStream();
            entryStream.CopyTo(contents);
            var bytes = contents.ToArray();

            var key = NormalizePath(entry.FullName);
            entries[key] = bytes;

            // A case-insensitive fallback, because a zip authored on Windows is routinely referenced by
            // differently-cased paths in the HTML. Only unambiguous names get one: if two entries differ
            // only by case, neither can be resolved this way without guessing.
            if (!duplicateIgnoringCase.Add(key))
            {
                entriesIgnoringCase.Remove(key);
            }
            else
            {
                entriesIgnoringCase[key] = bytes;
            }
        }

        return new ZipFileNetworkLoader(entries, entriesIgnoringCase, FindPrimaryDocument(entries.Keys));
    }

    /// <inheritdoc/>
    public override Task<string> GetPrimaryContents()
    {
        var bytes = _entries[_primaryDocumentPath];

        // Honouring a <meta charset> would mean parsing the document to decide how to parse it; a BOM, or
        // UTF-8, covers essentially every archive a browser or authoring tool produces.
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);

        return Task.FromResult(reader.ReadToEnd());
    }

    /// <inheritdoc/>
    public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
    {
        // Yields to the browser's event loop. Blazor WebAssembly is single-threaded, so this is one of the
        // few points during a render where the page can actually repaint.
        await Task.Yield();

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Uri.Scheme, SyntheticScheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Uri.Host, SyntheticHost, StringComparison.OrdinalIgnoreCase))
        {
            // Anything not inside this archive - including a real http(s) URL the document happens to
            // reference - is simply unresolvable. That is what makes "no network access" structural.
            return null;
        }

        var key = NormalizePath(Uri.UnescapeDataString(uri.Uri.AbsolutePath));

        if (!_entries.TryGetValue(key, out var bytes) && !_entriesIgnoringCase.TryGetValue(key, out bytes))
        {
            return null;
        }

        var headers = new Dictionary<string, string[]>
        {
            ["Content-Type"] = [ZipMimeTypes.GetMimeType(key)],
        };

        return new RNetworkResponse(new MemoryStream(bytes, writable: false), headers);
    }

    /// <summary>
    /// Looks for the entry-point document: the four conventional names at the archive root first, then the
    /// same names anywhere in the archive preferring the shallowest match. The two passes are what stop a
    /// deeply nested <c>index.html</c> from beating a root-level <c>index.htm</c>; the second exists because
    /// "compress this folder" produces an archive with a single wrapper directory, which is far and away
    /// the commonest shape.
    /// </summary>
    private static string FindPrimaryDocument(IEnumerable<string> paths)
    {
        var all = paths.ToList();

        foreach (var name in PrimaryDocumentNames)
        {
            var atRoot = all.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            if (atRoot is not null)
            {
                return atRoot;
            }
        }

        foreach (var name in PrimaryDocumentNames)
        {
            var nested = all
                .Where(p => string.Equals(LastSegment(p), name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Count(c => c == '/'))
                .ThenBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault();

            if (nested is not null)
            {
                return nested;
            }
        }

        // Last resort: an archive with exactly one HTML document has an unambiguous entry point whatever
        // it happens to be called.
        var singleHtml = all
            .Where(p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (singleHtml.Count == 1)
        {
            return singleHtml[0];
        }

        throw new InvalidOperationException(
            "No entry-point document found in the archive. Add one of " +
            string.Join(", ", PrimaryDocumentNames) + ".");
    }

    private static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>
    /// Brings an entry name and a requested path to the same form: forward slashes, no leading slash, and
    /// <c>.</c>/<c>..</c> segments collapsed. A <c>..</c> that would escape the archive root simply matches
    /// no entry — nothing here is ever resolved against a real path, so there is no traversal to prevent,
    /// only a lookup to miss.
    /// </summary>
    private static string NormalizePath(string raw)
    {
        var segments = new List<string>();

        foreach (var segment in raw.Replace('\\', '/').Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    continue;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join('/', segments);
    }
}
