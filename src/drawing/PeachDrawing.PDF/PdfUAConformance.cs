namespace PeachDrawing.PDF;
/// <summary>
/// The PDF/UA (ISO 14289, "universal accessibility") conformance level to target, set via <see cref="PdfComposerOptions.UAConformance"/>.
/// Defaults to <see cref="None"/>: no PDF/UA claim is made.
/// </summary>
/// <remarks>
/// <para>
/// PDF/UA can be claimed alongside PDF/A (PDF/A-1a/2a/3a with <see cref="UA1"/>, PDF/A-4 with <see cref="UA2"/>) but not alongside PDF/X.
/// A claim identifies the file in its XMP metadata (<c>pdfuaid:part</c>, and <c>pdfuaid:rev</c> for <see cref="UA2"/>; when the file also
/// claims PDF/A, the <c>pdfuaid</c> schema is declared as an extension schema, as PDF/A requires) and the writer refuses to produce a file
/// that could not meet the requirements it can check: the document needs a tag structure (a tag recorder from
/// <see cref="PdfComposer.CreateTagRecorder"/>, whose figures then carry alternative text, empty for decorative ones), a title, a
/// language, and it displays the title rather than the file name.
/// </para>
/// <para>
/// The writer checks what it can see; it does not claim the content is accessible. That depends on the structure the document's author
/// gave it (a sensible reading order, headings that do not skip levels, real alternative text), which a conformance checker such as
/// veraPDF or the PAC checker is the way to verify.
/// </para>
/// </remarks>
public enum PdfUAConformance
{
    /// <summary>No PDF/UA claim is made. Default.</summary>
    None = 0,

    /// <summary>PDF/UA-1 (ISO 14289-1), defined against PDF 1.7.</summary>
    UA1 = 1,

    /// <summary>PDF/UA-2 (ISO 14289-2), defined against PDF 2.0.</summary>
    UA2 = 2,
}
