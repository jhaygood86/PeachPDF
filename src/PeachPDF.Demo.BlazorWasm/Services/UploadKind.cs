namespace PeachPDF.Demo.BlazorWasm.Services;

/// <summary>What an uploaded file is, and therefore how its resources are resolved.</summary>
public enum UploadKind
{
    /// <summary>A plain HTML document. Only <c>data:</c> URIs resolve; there is nothing else to read from.</summary>
    Html,

    /// <summary>An MHTML archive ("single page document"), carrying its own resources as MIME parts.</summary>
    Mhtml,

    /// <summary>A ZIP archive holding a document and its assets.</summary>
    Zip,
}
