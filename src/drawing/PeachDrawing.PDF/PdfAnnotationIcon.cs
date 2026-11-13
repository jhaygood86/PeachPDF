namespace PeachDrawing.PDF;
/// <summary>
/// A standard icon of a note or attached-file annotation. Each member names one of the icons a PDF reader knows by name and is what
/// <see cref="PdfAnnotationAppearance.Icon"/> draws, so the annotation looks the same whichever reader opens it.
/// </summary>
public enum PdfAnnotationIcon
{
    /// <summary>A sheet of paper with a folded corner (a note's default).</summary>
    Note,

    /// <summary>A speech bubble.</summary>
    Comment,

    /// <summary>A key.</summary>
    Key,

    /// <summary>A question mark in a circle.</summary>
    Help,

    /// <summary>A pilcrow with a plus: start a new paragraph here.</summary>
    NewParagraph,

    /// <summary>A pilcrow.</summary>
    Paragraph,

    /// <summary>A caret: insert text here.</summary>
    Insert,

    /// <summary>A paperclip (an attached file's default).</summary>
    Paperclip,

    /// <summary>A push pin.</summary>
    PushPin,

    /// <summary>A bar chart.</summary>
    Graph,

    /// <summary>A price tag.</summary>
    Tag,
}
