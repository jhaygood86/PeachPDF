namespace PeachBrackets.Css
{
    /// <summary>The kinds of PDF annotation <c>-peachpdf-pdf-annotation-type</c> can make an element into.</summary>
    internal enum PdfAnnotationTypeKeyword
    {
        None,
        Text,
        Highlight,
        Underline,
        Wavy,
        LineThrough,
        FileAttachment,
        FreeText,
        Stamp,
        Square,
        Circle,
        Line,
        Ink
    }

    /// <summary>The icons <c>-peachpdf-pdf-annotation-icon</c> can name (<c>auto</c> picks the type's default).</summary>
    internal enum PdfAnnotationIconKeyword
    {
        Auto,
        Note,
        Comment,
        Key,
        Help,
        NewParagraph,
        Paragraph,
        Insert,
        Paperclip,
        PushPin,
        Graph,
        Tag
    }

    /// <summary>The keywords of <c>-peachpdf-pdf-annotation-position</c> (lengths and percentages are the other values).</summary>
    internal enum PdfAnnotationPositionKeyword
    {
        Left,
        Right,
        Top,
        Bottom,
        Center,
        PageLeft,
        PageRight,
        PageTop,
        PageBottom
    }

    /// <summary>The keywords of <c>-peachpdf-pdf-annotation-merge</c> (<c>key(&lt;string&gt;)</c> is the other value).</summary>
    internal enum PdfAnnotationMergeKeyword
    {
        None,
        Duplicates
    }
}
