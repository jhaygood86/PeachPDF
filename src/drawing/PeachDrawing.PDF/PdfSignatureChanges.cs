namespace PeachDrawing.PDF;

/// <summary>
/// What changed in a document after a signature was made, as far as the objects of the file show. See
/// <see cref="PdfSourceSignature.ChangesAfter"/>. Each value includes the ones before it: a file with a new signature <i>and</i> a filled form
/// field is <see cref="FormFilling"/>.
/// </summary>
public enum PdfSignatureChanges
{
    /// <summary>No object of the document differs from what the signature covered. Bytes may still follow it (a later revision that changes nothing, or trailing data).</summary>
    None,

    /// <summary>Only signatures, document timestamps and validation data (the document security store) were added. What the signer saw is unchanged.</summary>
    SignaturesAndTimestamps,

    /// <summary>Form field values (and the appearances that show them) were also filled in or changed.</summary>
    FormFilling,

    /// <summary>Annotations (comments, highlights, stamps) were also added or changed.</summary>
    Annotations,

    /// <summary>Something else changed: page content, fonts, images, the page tree, metadata, or a part of the document the checks above do not recognize. The signed content may no longer be what a reader shows.</summary>
    Other,

    /// <summary>It could not be worked out: the signature is not intact, or the file as it was signed cannot be read back (a revision boundary that is not a complete file).</summary>
    Unknown,
}
