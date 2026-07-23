using System.Globalization;
using PeachPDF;

namespace PeachPDF.Cli;

/// <summary>
/// Parses the command line into <see cref="CliOptions"/>. Grammar: long options accept
/// <c>--name=value</c> or <c>--name value</c>; short options (<c>-o</c>/<c>-s</c>/<c>-l</c>)
/// take their value as the next argument; boolean shorts (<c>-v</c>/<c>-h</c>) take none; a lone
/// <c>-</c> is the stdin/stdout sentinel; anything else beginning with <c>-</c> is an unknown option.
/// The parser recognizes only the supported options — any other flag is an error (the CLI implements
/// only the options that map cleanly onto PeachPDF).
/// </summary>
internal static class ArgumentParser
{
    public static CliOptions Parse(string[] args) => new Worker(args).Run();

    /// <summary>
    /// Carries the parse state (the argument array and current index) so value-consuming helpers can
    /// advance the cursor via a field rather than a <c>ref</c> parameter (which a local function or
    /// lambda cannot capture).
    /// </summary>
    private sealed class Worker(string[] args)
    {
        private readonly CliOptions _options = new();
        private int _index;

        public CliOptions Run()
        {
            for (_index = 0; _index < args.Length; _index++)
            {
                var arg = args[_index];
                if (arg.Length == 0)
                {
                    continue;
                }

                if (arg == "-")
                {
                    _options.Inputs.Add(new CliInput(CliInputKind.Stdin, "-"));
                }
                else if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    ParseLongOption(arg);
                }
                else if (arg[0] == '-')
                {
                    ParseShortOption(arg);
                }
                else
                {
                    _options.Inputs.Add(ClassifyInput(arg));
                }
            }

            PostProcess(_options);
            return _options;
        }

        /// <summary>Returns the value for the current long option, consuming the next argument if needed.</summary>
        private string? RequireValue(string name, string? inlineValue)
        {
            if (inlineValue is not null)
            {
                return inlineValue;
            }

            if (_index + 1 < args.Length)
            {
                _index++;
                return args[_index];
            }

            _options.Errors.Add($"option --{name} requires a value");
            return null;
        }

        /// <summary>Returns the value for the current short option, consuming the next argument.</summary>
        private string? ShortValue(string flag)
        {
            if (_index + 1 < args.Length)
            {
                _index++;
                return args[_index];
            }

            _options.Errors.Add($"option {flag} requires a value");
            return null;
        }

        private void ParseLongOption(string arg)
        {
            string name;
            string? inlineValue;
            var eq = arg.IndexOf('=');
            if (eq >= 0)
            {
                name = arg[2..eq];
                inlineValue = arg[(eq + 1)..];
            }
            else
            {
                name = arg[2..];
                inlineValue = null;
            }

            switch (name)
            {
                case "help": _options.Action = CliAction.Help; break;
                case "version": _options.Action = CliAction.Version; break;
                case "show-license": _options.Action = CliAction.ShowLicense; break;
                case "credits": _options.Action = CliAction.Credits; break;

                case "output": SetOutput(_options, RequireValue(name, inlineValue)); break;
                case "input-list": ReadInputList(_options, RequireValue(name, inlineValue)); break;
                case "baseurl": _options.BaseUrl = RequireValue(name, inlineValue) ?? _options.BaseUrl; break;
                case "style": AddStyle(_options, RequireValue(name, inlineValue)); break;
                case "no-default-style": _options.NoDefaultStyle = true; break;
                case "no-author-style": _options.NoAuthorStyle = true; break;
                case "media": _options.Media = RequireValue(name, inlineValue) ?? _options.Media; break;
                case "page-size": ParsePageSize(_options, RequireValue(name, inlineValue)); break;
                case "page-margin": ParsePageMargin(_options, RequireValue(name, inlineValue)); break;

                case "no-compress": _options.NoCompress = true; break;
                case "tagged-pdf": _options.TaggedPdf = true; break;
                case "pdf-title": _options.PdfTitle = RequireValue(name, inlineValue) ?? _options.PdfTitle; break;
                case "pdf-author": _options.PdfAuthor = RequireValue(name, inlineValue) ?? _options.PdfAuthor; break;
                case "pdf-subject": _options.PdfSubject = RequireValue(name, inlineValue) ?? _options.PdfSubject; break;
                case "pdf-keywords": _options.PdfKeywords = RequireValue(name, inlineValue) ?? _options.PdfKeywords; break;
                case "pdf-creator": _options.PdfCreator = RequireValue(name, inlineValue) ?? _options.PdfCreator; break;
                case "pdf-lang": _options.PdfLang = RequireValue(name, inlineValue) ?? _options.PdfLang; break;

                case "http-timeout": SetTimeout(_options, RequireValue(name, inlineValue)); break;
                case "http-header": AddHeader(_options, RequireValue(name, inlineValue)); break;
                case "user-agent": _options.UserAgent = RequireValue(name, inlineValue) ?? _options.UserAgent; break;
                case "auth-user": _options.AuthUser = RequireValue(name, inlineValue) ?? _options.AuthUser; break;
                case "auth-password": _options.AuthPassword = RequireValue(name, inlineValue) ?? _options.AuthPassword; break;
                case "http-proxy": _options.HttpProxy = RequireValue(name, inlineValue) ?? _options.HttpProxy; break;
                case "insecure": _options.Insecure = true; break;
                case "no-network": _options.NoNetwork = true; break;
                case "no-local-files": _options.NoLocalFiles = true; break;

                case "verbose": _options.Verbose = true; break;
                case "debug": _options.Debug = true; break;
                case "log": _options.LogFile = RequireValue(name, inlineValue) ?? _options.LogFile; break;

                default: _options.Errors.Add($"unknown option: --{name}"); break;
            }
        }

        private void ParseShortOption(string arg)
        {
            switch (arg)
            {
                case "-o": SetOutput(_options, ShortValue(arg)); break;
                case "-s": AddStyle(_options, ShortValue(arg)); break;
                case "-l": ReadInputList(_options, ShortValue(arg)); break;
                case "-v": _options.Verbose = true; break;
                case "-h": _options.Action = CliAction.Help; break;
                default: _options.Errors.Add($"unknown option: {arg}"); break;
            }
        }
    }

    private static CliInput ClassifyInput(string value)
    {
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new CliInput(CliInputKind.Url, value);
        }

        var ext = Path.GetExtension(value);
        if (ext.Equals(".mht", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mhtml", StringComparison.OrdinalIgnoreCase))
        {
            return new CliInput(CliInputKind.Mhtml, value);
        }

        return new CliInput(CliInputKind.File, value);
    }

    private static void SetOutput(CliOptions options, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value == "-")
        {
            options.OutputKind = CliOutputKind.Stdout;
            options.OutputPath = null;
        }
        else
        {
            options.OutputKind = CliOutputKind.File;
            options.OutputPath = value;
        }
    }

    private static void AddStyle(CliOptions options, string? value)
    {
        if (value is not null)
        {
            options.StyleSheets.Add(value);
        }
    }

    private static void AddHeader(CliOptions options, string? value)
    {
        if (value is not null)
        {
            options.HttpHeaders.Add(value);
        }
    }

    private static void SetTimeout(CliOptions options, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            options.HttpTimeoutSeconds = seconds;
        }
        else
        {
            options.Errors.Add($"invalid --http-timeout value '{value}' (expected a positive number of seconds)");
        }
    }

    private static void ReadInputList(CliOptions options, string? path)
    {
        if (path is null)
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            options.Errors.Add($"cannot read input list '{path}': {ex.Message}");
            return;
        }

        foreach (var line in lines)
        {
            var entry = line.Trim();
            if (entry.Length > 0)
            {
                options.Inputs.Add(ClassifyInput(entry));
            }
        }
    }

    private static void ParsePageSize(CliOptions options, string? value)
    {
        if (value is null)
        {
            return;
        }

        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Pull out an orientation keyword if present, leaving 0-2 size tokens.
        var sizeTokens = new List<string>();
        foreach (var token in tokens)
        {
            if (token.Equals("portrait", StringComparison.OrdinalIgnoreCase))
            {
                options.Orientation = PageOrientation.Portrait;
            }
            else if (token.Equals("landscape", StringComparison.OrdinalIgnoreCase))
            {
                options.Orientation = PageOrientation.Landscape;
            }
            else
            {
                sizeTokens.Add(token);
            }
        }

        switch (sizeTokens.Count)
        {
            case 0:
                // Orientation only — keep the default page size.
                break;
            case 1 when TryParseNamedSize(sizeTokens[0], out var named):
                options.PageSize = named;
                options.ManualPageWidthPt = null;
                options.ManualPageHeightPt = null;
                break;
            case 1 when TryParseLength(sizeTokens[0], out var square):
                options.PageSize = PeachPDF.PageSize.Undefined;
                options.ManualPageWidthPt = square;
                options.ManualPageHeightPt = square;
                break;
            case 2 when TryParseLength(sizeTokens[0], out var width) && TryParseLength(sizeTokens[1], out var height):
                options.PageSize = PeachPDF.PageSize.Undefined;
                options.ManualPageWidthPt = width;
                options.ManualPageHeightPt = height;
                break;
            default:
                options.Errors.Add($"invalid --page-size value '{value}'");
                break;
        }
    }

    private static void ParsePageMargin(CliOptions options, string? value)
    {
        if (value is null)
        {
            return;
        }

        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var margins = new double[tokens.Length];
        for (var k = 0; k < tokens.Length; k++)
        {
            if (!TryParseLength(tokens[k], out margins[k]))
            {
                options.Errors.Add($"invalid --page-margin value '{value}'");
                return;
            }
        }

        // CSS shorthand ordering: 1 = all; 2 = vertical horizontal; 3 = top horizontal bottom; 4 = top right bottom left.
        double top, right, bottom, left;
        switch (tokens.Length)
        {
            case 1: top = right = bottom = left = margins[0]; break;
            case 2: top = bottom = margins[0]; right = left = margins[1]; break;
            case 3: top = margins[0]; right = left = margins[1]; bottom = margins[2]; break;
            case 4: top = margins[0]; right = margins[1]; bottom = margins[2]; left = margins[3]; break;
            default:
                options.Errors.Add($"invalid --page-margin value '{value}' (expected 1 to 4 lengths)");
                return;
        }

        options.MarginTopPt = top;
        options.MarginRightPt = right;
        options.MarginBottomPt = bottom;
        options.MarginLeftPt = left;
    }

    private static bool TryParseNamedSize(string token, out PageSize size)
    {
        size = PeachPDF.PageSize.Undefined;

        // Reject all-digit tokens so Enum.TryParse doesn't interpret a bare number as an enum value.
        if (token.Length == 0 || token.All(char.IsDigit))
        {
            return false;
        }

        return Enum.TryParse(token, ignoreCase: true, out size)
               && size != PeachPDF.PageSize.Undefined
               && Enum.IsDefined(size);
    }

    /// <summary>
    /// Parses a CSS length into PDF points (1pt = 1/72in). Supported units: mm, cm, in, pt, pc, px
    /// (1px = 1/96in = 0.75pt); a bare number is treated as points.
    /// </summary>
    internal static bool TryParseLength(string token, out double points)
    {
        points = 0;
        token = token.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var split = token.Length;
        for (var k = 0; k < token.Length; k++)
        {
            var c = token[k];
            if (!(char.IsDigit(c) || c is '.' or '+' or '-'))
            {
                split = k;
                break;
            }
        }

        var numberPart = token[..split];
        var unit = token[split..].Trim().ToLowerInvariant();

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var factor = unit switch
        {
            "" => 1.0,
            "pt" => 1.0,
            "px" => 0.75,
            "in" => 72.0,
            "pc" => 12.0,
            "mm" => 72.0 / 25.4,
            "cm" => 72.0 / 2.54,
            _ => double.NaN,
        };

        if (double.IsNaN(factor))
        {
            return false;
        }

        points = value * factor;
        return true;
    }

    private static void PostProcess(CliOptions options)
    {
        if (options.Action != CliAction.Run)
        {
            return;
        }

        if (options.Errors.Count > 0)
        {
            return;
        }

        if (options.Inputs.Count == 0)
        {
            options.Errors.Add("no input files specified");
            return;
        }

        if (options.NoNetwork && options.Inputs.Any(input => input.Kind == CliInputKind.Url))
        {
            options.Errors.Add("cannot fetch a URL input while --no-network is set");
        }

        // Without an explicit -o, the output name is derived from the first input's file name, which
        // only exists for a file/MHTML input (a URL or stdin has no name to reuse).
        if (options.OutputKind == CliOutputKind.Default &&
            options.Inputs[0].Kind is not CliInputKind.File and not CliInputKind.Mhtml)
        {
            options.Errors.Add("an output file (-o) must be specified when the first input is a URL or stdin");
        }
    }
}
