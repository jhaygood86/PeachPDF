using PeachPDF;

namespace PeachPDF.Cli;

/// <summary>The top-level action the CLI should take, decided during argument parsing.</summary>
internal enum CliAction
{
    /// <summary>Render the inputs to a PDF.</summary>
    Run,
    /// <summary>Print usage and exit 0.</summary>
    Help,
    /// <summary>Print version and exit 0.</summary>
    Version,
    /// <summary>Print the license and exit 0.</summary>
    ShowLicense,
    /// <summary>Print the license plus third-party acknowledgements and exit 0.</summary>
    Credits,
}

/// <summary>How a single input document is sourced.</summary>
internal enum CliInputKind
{
    /// <summary>A local file path.</summary>
    File,
    /// <summary>An HTTP/HTTPS URL.</summary>
    Url,
    /// <summary>Standard input (the <c>-</c> sentinel).</summary>
    Stdin,
    /// <summary>A local MHTML (<c>.mht</c>/<c>.mhtml</c>) archive.</summary>
    Mhtml,
}

/// <summary>Where the generated PDF is written.</summary>
internal enum CliOutputKind
{
    /// <summary>Derive the output path from the first file input's name (input basename + <c>.pdf</c>).</summary>
    Default,
    /// <summary>A specific file path (<c>-o FILE</c>).</summary>
    File,
    /// <summary>Standard output (<c>-o -</c>).</summary>
    Stdout,
}

/// <summary>A single input document and how it is sourced.</summary>
internal sealed record CliInput(CliInputKind Kind, string Value);

/// <summary>
/// The fully parsed command line. <see cref="ArgumentParser.Parse"/> produces one of these; when
/// <see cref="Errors"/> is non-empty the command line was invalid and the CLI exits non-zero.
/// </summary>
internal sealed class CliOptions
{
    public CliAction Action { get; set; } = CliAction.Run;

    /// <summary>Parse errors (unknown options, missing values, bad page sizes). Non-empty ⇒ invalid.</summary>
    public List<string> Errors { get; } = [];

    // --- Input / output ---
    public List<CliInput> Inputs { get; } = [];
    public CliOutputKind OutputKind { get; set; } = CliOutputKind.Default;
    public string? OutputPath { get; set; }
    public string? BaseUrl { get; set; }

    // --- CSS ---
    public List<string> StyleSheets { get; } = [];
    public bool NoDefaultStyle { get; set; }
    public bool NoAuthorStyle { get; set; }
    public string? Media { get; set; }

    // --- Page geometry ---
    public PageSize? PageSize { get; set; }
    public double? ManualPageWidthPt { get; set; }
    public double? ManualPageHeightPt { get; set; }
    public PageOrientation? Orientation { get; set; }
    public double? MarginTopPt { get; set; }
    public double? MarginRightPt { get; set; }
    public double? MarginBottomPt { get; set; }
    public double? MarginLeftPt { get; set; }

    // --- PDF output ---
    public bool NoCompress { get; set; }
    public bool TaggedPdf { get; set; }
    public string? PdfTitle { get; set; }
    public string? PdfAuthor { get; set; }
    public string? PdfSubject { get; set; }
    public string? PdfKeywords { get; set; }
    public string? PdfCreator { get; set; }
    public string? PdfLang { get; set; }

    // --- Network ---
    public int? HttpTimeoutSeconds { get; set; }
    public List<string> HttpHeaders { get; } = [];
    public string? UserAgent { get; set; }
    public string? AuthUser { get; set; }
    public string? AuthPassword { get; set; }
    public string? HttpProxy { get; set; }
    public bool Insecure { get; set; }
    public bool NoNetwork { get; set; }
    public bool NoLocalFiles { get; set; }

    // --- Logging ---
    public bool Verbose { get; set; }
    public bool Debug { get; set; }
    public string? LogFile { get; set; }
}
