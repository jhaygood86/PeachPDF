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
/// A file to embed (<c>--attach</c>). <see cref="MimeType"/> and <see cref="Relationship"/> are <c>null</c>
/// unless given on the command line, in which case the MIME type is guessed from the file's extension.
/// </summary>
internal sealed record CliAttachment(string Path, string? MimeType, PdfAttachmentRelationship? Relationship);

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

    /// <summary>Whether the document's <c>&lt;script&gt;</c> elements run (<c>--enable-scripting</c>), with the JavaScript host of <c>PeachBrackets.Scripting</c>.</summary>
    public bool EnableScripting { get; set; }

    /// <summary>Scripts to run in the document before its own (<c>--script</c>, repeatable); they run even without <c>--enable-scripting</c>.</summary>
    public List<string> UserScripts { get; } = [];

    /// <summary>The mode to lay the HTML out in (<c>--document-mode</c>); <see cref="PdfDocumentMode.Auto"/> takes it from the DOCTYPE, as a browser does.</summary>
    public PdfDocumentMode DocumentMode { get; set; } = PdfDocumentMode.Auto;

    /// <summary>How the HTML is parsed (<c>--document-syntax</c>); <see cref="PdfDocumentSyntax.Auto"/> parses a <c>.xhtml</c>/<c>.xht</c> file or an <c>application/xhtml+xml</c> response as XHTML.</summary>
    public PdfDocumentSyntax DocumentSyntax { get; set; } = PdfDocumentSyntax.Auto;

    /// <summary>Where in its run every CSS animation is sampled, 0 to 1 (<c>--animation-progress</c>); null leaves animations unapplied.</summary>
    public double? AnimationProgress { get; set; }

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

    /// <summary>Write a linearized ("fast web view") PDF (<c>--linearize</c>).</summary>
    public bool Linearize { get; set; }

    /// <summary>Compress with Brotli when writing PDF 2.0 (<c>--brotli</c>).</summary>
    public bool Brotli { get; set; }

    /// <summary>Embed eligible JPEG XL images as-is (<c>--jxl-passthrough</c>, experimental).</summary>
    public bool JxlPassthrough { get; set; }

    /// <summary>The PDF version to target (<c>--pdf-version</c>); null keeps the library default.</summary>
    public PdfVersion? PdfVersion { get; set; }

    /// <summary>Pixels per inch of paper for effects rendered as bitmaps (<c>--raster-dpi</c>); null keeps the library default.</summary>
    public double? RasterDpi { get; set; }

    /// <summary>Render what needs transparency as opaque bitmaps instead of rejecting it under PDF/A-1 and PDF/X-1a/X-3 (<c>--flatten-transparency</c>).</summary>
    public bool FlattenTransparency { get; set; }

    /// <summary>Disables anti-aliasing of bitmaps PeachPDF renders itself (<c>--no-raster-antialiasing</c>).</summary>
    public bool NoRasterAntiAliasing { get; set; }
    public bool TaggedPdf { get; set; }

    /// <summary>Snap box decorations to whole CSS pixels, like a browser (<c>--snap-box-decorations-to-css-pixels</c>).</summary>
    public bool SnapBoxDecorationsToCssPixels { get; set; }

    /// <summary>Snap border widths to whole CSS pixels, like a browser (<c>--snap-border-widths-to-css-pixels</c>).</summary>
    public bool SnapBorderWidthsToCssPixels { get; set; }
    public bool InteractivePdfForms { get; set; }
    public string? PdfTitle { get; set; }
    public string? PdfAuthor { get; set; }
    public string? PdfSubject { get; set; }
    public string? PdfKeywords { get; set; }
    public string? PdfCreator { get; set; }
    public string? PdfLang { get; set; }
    public PdfAConformance? PdfA { get; set; }
    public PdfUAConformance? PdfUA { get; set; }
    public DateTimeOffset? PdfCreationDate { get; set; }
    public List<CliAttachment> Attachments { get; } = [];
    public string? FacturXXmlPath { get; set; }
    public FacturXProfile? FacturXProfile { get; set; }

    /// <summary>The password that opens the PDF (<c>--user-password</c>, <c>-file</c> or <c>-env</c>).</summary>
    public string? UserPassword { get; set; }

    /// <summary>The password that lifts the PDF's restrictions (<c>--owner-password</c>, <c>-file</c> or <c>-env</c>).</summary>
    public string? OwnerPassword { get; set; }

    /// <summary>What a reader may do without the owner password (<c>--permissions</c>); null allows everything.</summary>
    public PdfPermissions? Permissions { get; set; }

    /// <summary>Leave the XMP metadata stream unencrypted (<c>--no-encrypt-metadata</c>).</summary>
    public bool NoEncryptMetadata { get; set; }

    /// <summary>Whether any option asks for an encrypted PDF.</summary>
    public bool WantsEncryption => UserPassword is not null || OwnerPassword is not null || Permissions is not null || NoEncryptMetadata;

    /// <summary>The PKCS#12 (.pfx/.p12) file holding the signing certificate and key (<c>--sign-cert</c>); null leaves the PDF unsigned.</summary>
    public string? SignCertificatePath { get; set; }

    /// <summary>The password of the certificate file (<c>--sign-password</c>, <c>-file</c> or <c>-env</c>).</summary>
    public string? SignPassword { get; set; }

    /// <summary>The signature format (<c>--sign-format</c>); null is the default PKCS#7.</summary>
    public PdfSignatureFormat? SignFormat { get; set; }

    /// <summary>The address of an RFC 3161 timestamp authority (<c>--timestamp-url</c>).</summary>
    public Uri? TimestampUrl { get; set; }

    /// <summary>The signer name (<c>--sign-name</c>).</summary>
    public string? SignName { get; set; }

    /// <summary>Why the document is signed (<c>--sign-reason</c>).</summary>
    public string? SignReason { get; set; }

    /// <summary>Where the document is signed (<c>--sign-location</c>).</summary>
    public string? SignLocation { get; set; }

    /// <summary>How to reach the signer (<c>--sign-contact</c>).</summary>
    public string? SignContact { get; set; }

    /// <summary>Where a visible signature is drawn (<c>--sign-box</c>); null is an invisible signature.</summary>
    public PdfSignatureBox? SignBox { get; set; }

    /// <summary>Whether any option asks for a signed PDF.</summary>
    public bool WantsSigning => SignCertificatePath is not null;

    /// <summary>Whether a signing detail was given, which only means something with <c>--sign-cert</c>.</summary>
    public bool HasSigningDetail => SignPassword is not null || SignFormat is not null || TimestampUrl is not null || SignName is not null
        || SignReason is not null || SignLocation is not null || SignContact is not null || SignBox is not null;

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
