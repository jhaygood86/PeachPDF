using System.Net;
using System.Net.Http;
using System.Text;
using PeachPDF;
using PeachPDF.Network;

namespace PeachPDF.Cli;

/// <summary>
/// Executes a parsed <see cref="CliOptions"/>: builds a <see cref="PdfGenerateConfig"/> and the right
/// <see cref="RNetworkLoader"/> per input, renders each input (combining multiple inputs into one PDF),
/// and writes the result to the chosen output. Returns the process exit code.
/// </summary>
internal static class CliRunner
{
    public static Task<int> RunAsync(CliOptions options)
        => RunAsync(options, Console.In, Console.OpenStandardOutput());

    internal static async Task<int> RunAsync(CliOptions options, TextReader stdin, Stream stdout)
    {
        using var logger = new CliLogger(options);
        HttpClient? httpClient = null;

        // MHTML loaders read from a source stream lazily throughout rendering, so the streams must stay
        // open until every input is rendered, then be disposed here.
        var openedStreams = new List<Stream>();

        try
        {
            if (options.Insecure)
            {
                logger.Warn("TLS certificate verification is disabled (--insecure).");
            }

            httpClient = BuildHttpClient(options);

            var generator = new PdfGenerator();
            var config = BuildConfig(options);
            var cssData = await BuildCssDataAsync(generator, options);

            PeachPdfDocument? document = null;

            foreach (var input in options.Inputs)
            {
                logger.Info($"Rendering {Describe(input)}");

                var (html, loader) = await PrepareInputAsync(input, options, httpClient, stdin, openedStreams);
                config.NetworkLoader = loader;

                if (document is null)
                {
                    document = await generator.GeneratePdf(html, config, cssData);
                }
                else
                {
                    await generator.AddPdfPages(document, html, config, cssData);
                }
            }

            if (document is null)
            {
                await Console.Error.WriteLineAsync("error: no input produced any output");
                return 1;
            }

            await WriteOutputAsync(document, options, stdout, logger);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or UriFormatException or ArgumentException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
        finally
        {
            foreach (var stream in openedStreams)
            {
                stream.Dispose();
            }

            httpClient?.Dispose();
        }
    }

    internal static PdfGenerateConfig BuildConfig(CliOptions options)
    {
        var config = new PdfGenerateConfig
        {
            PageOrientation = options.Orientation ?? PageOrientation.Portrait,
            CompressContentStreams = !options.NoCompress,
            EnableTaggedPdf = options.TaggedPdf,
            Media = options.Media ?? "print",
            IgnoreAuthorStyleSheets = options.NoAuthorStyle,
        };

        if (options.PageSize is { } pageSize)
        {
            config.PageSize = pageSize;
            if (pageSize == PageSize.Undefined)
            {
                config.ManualPageWidth = options.ManualPageWidthPt ?? 0;
                config.ManualPageHeight = options.ManualPageHeightPt ?? 0;
            }
        }
        else
        {
            // Default when none is given; a document's own @page { size } still overrides it.
            config.PageSize = PageSize.Letter;
        }

        if (options.MarginTopPt is { } top) config.MarginTop = (int)Math.Round(top);
        if (options.MarginRightPt is { } right) config.MarginRight = (int)Math.Round(right);
        if (options.MarginBottomPt is { } bottom) config.MarginBottom = (int)Math.Round(bottom);
        if (options.MarginLeftPt is { } left) config.MarginLeft = (int)Math.Round(left);

        if (options.PdfTitle is not null || options.PdfAuthor is not null || options.PdfSubject is not null ||
            options.PdfKeywords is not null || options.PdfCreator is not null)
        {
            config.Metadata = new PdfDocumentMetadata
            {
                Title = options.PdfTitle,
                Author = options.PdfAuthor,
                Subject = options.PdfSubject,
                Keywords = options.PdfKeywords,
                Creator = options.PdfCreator,
            };
        }

        if (!string.IsNullOrEmpty(options.PdfLang))
        {
            config.DefaultLanguage = options.PdfLang;
        }

        return config;
    }

    private static async Task<PeachPdfCssContent?> BuildCssDataAsync(PdfGenerator generator, CliOptions options)
    {
        if (options.StyleSheets.Count == 0 && !options.NoDefaultStyle)
        {
            return null;
        }

        // Start from the UA default styles (unless --no-default-style), then layer each -s sheet on top
        // so later sheets win (applied in order, last overwrites).
        var cssData = await generator.ParseStyleSheet(string.Empty, combineWithDefault: !options.NoDefaultStyle);

        foreach (var styleSheetPath in options.StyleSheets)
        {
            var css = await File.ReadAllTextAsync(styleSheetPath);
            await cssData.AddStyleSheet(css);
        }

        return cssData;
    }

    private static async Task<(string? Html, RNetworkLoader Loader)> PrepareInputAsync(
        CliInput input, CliOptions options, HttpClient? httpClient, TextReader stdin, List<Stream> openedStreams)
    {
        if (input.Kind == CliInputKind.Mhtml)
        {
            // MHTML archives are self-contained; the loader supplies both the document and its resources.
            // The stream is owned by the caller and disposed once rendering completes (see RunAsync).
            var stream = File.OpenRead(input.Value);
            openedStreams.Add(stream);
            return (null, new MimeKitNetworkLoader(stream));
        }

        var baseUri = options.BaseUrl is not null ? ResolveBaseUri(options.BaseUrl) : DefaultBaseFor(input);

        var html = input.Kind switch
        {
            CliInputKind.File => await File.ReadAllTextAsync(input.Value),
            CliInputKind.Url => await FetchUrlAsync(input.Value, httpClient),
            CliInputKind.Stdin => await stdin.ReadToEndAsync(),
            _ => throw new InvalidOperationException($"unsupported input kind {input.Kind}"),
        };

        var loader = new CliNetworkLoader(baseUri, options.NoNetwork ? null : httpClient, !options.NoLocalFiles);
        return (html, loader);
    }

    private static async Task<string> FetchUrlAsync(string url, HttpClient? httpClient)
    {
        if (httpClient is null)
        {
            throw new InvalidOperationException($"cannot fetch '{url}' because network access is disabled");
        }

        return await httpClient.GetStringAsync(url);
    }

    internal static RUri DefaultBaseFor(CliInput input) => input.Kind switch
    {
        CliInputKind.File => new RUri(new Uri(Path.GetFullPath(input.Value))),
        CliInputKind.Url => new RUri(new Uri(input.Value)),
        _ => new RUri(new Uri(EnsureTrailingSeparator(Directory.GetCurrentDirectory()))),
    };

    internal static RUri ResolveBaseUri(string baseUrl)
    {
        var absolute = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ? parsed : null;

        // An explicit http(s) base is used verbatim. Everything else - a bare local path (which, on
        // Unix, Uri parses as a schemeless file URI) or an explicit file: URL - is routed through the
        // local-path branch so a directory base gets a trailing separator; without it, resolving a
        // relative reference would drop the directory's own last segment (RFC 3986).
        if (absolute is { Scheme: "http" or "https" })
        {
            return new RUri(absolute);
        }

        var path = absolute is { Scheme: "file" } ? absolute.LocalPath : baseUrl;
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            fullPath = EnsureTrailingSeparator(fullPath);
        }

        return new RUri(new Uri(fullPath));
    }

    private static async Task WriteOutputAsync(PeachPdfDocument document, CliOptions options, Stream stdout, CliLogger logger)
    {
        if (options.OutputKind == CliOutputKind.Stdout)
        {
            using var buffer = new MemoryStream();
            document.Save(buffer);
            buffer.Position = 0;
            await buffer.CopyToAsync(stdout);
            await stdout.FlushAsync();
            logger.Info($"Wrote PDF to standard output ({document.PageCount} page(s)).");
            return;
        }

        var outputPath = options.OutputKind == CliOutputKind.File
            ? options.OutputPath!
            : Path.ChangeExtension(options.Inputs[0].Value, ".pdf");

        await using var file = File.Create(outputPath);
        document.Save(file);
        logger.Info($"Wrote {outputPath} ({document.PageCount} page(s)).");
    }

    internal static HttpClient? BuildHttpClient(CliOptions options)
    {
        if (options.NoNetwork)
        {
            return null;
        }

        var handler = new HttpClientHandler();

        if (!string.IsNullOrEmpty(options.HttpProxy))
        {
            handler.Proxy = new WebProxy(options.HttpProxy);
            handler.UseProxy = true;
        }

        if (options.Insecure)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler);

        if (options.HttpTimeoutSeconds is { } seconds)
        {
            client.Timeout = TimeSpan.FromSeconds(seconds);
        }

        if (!string.IsNullOrEmpty(options.UserAgent))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        }

        foreach (var header in options.HttpHeaders)
        {
            var separator = header.IndexOf(':');
            if (separator > 0)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header[..separator].Trim(), header[(separator + 1)..].Trim());
            }
        }

        if (!string.IsNullOrEmpty(options.AuthUser))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AuthUser}:{options.AuthPassword}"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {token}");
        }

        return client;
    }

    private static string EnsureTrailingSeparator(string directory)
    {
        return directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
    }

    private static string Describe(CliInput input) => input.Kind switch
    {
        CliInputKind.Stdin => "standard input",
        _ => input.Value,
    };
}
