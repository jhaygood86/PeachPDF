namespace PeachBrackets;

/// <summary>
/// The rendering mode of a document: how closely it follows the standards, as a browser tells it from the DOCTYPE. A document with a current DOCTYPE
/// (<c>&lt;!DOCTYPE html&gt;</c>) is in <see cref="NoQuirks"/> mode; one with none, or with a legacy one, is in <see cref="Quirks"/> mode, which
/// keeps the behavior pages written before the standards were settled depend on (see the HTML Standard's rendering section and the Quirks Mode
/// Standard).
/// </summary>
public enum DocumentMode
{
    /// <summary>The standards: the mode of a document with a current DOCTYPE, and of a fragment.</summary>
    NoQuirks,

    /// <summary>
    /// "Almost standards" mode, for a few transitional DOCTYPEs: the standards, except that a line box is sized the way quirks mode sizes it (an
    /// inline box with no text does not make a line taller, and a block's own <c>line-height</c> does not give its line boxes a minimum height).
    /// </summary>
    LimitedQuirks,

    /// <summary>
    /// Quirks mode, the mode of a document with no DOCTYPE or a legacy one: class and ID selectors ignore case, <c>#</c> may be left off a color and
    /// <c>px</c> off a length, a table does not inherit the body's font, the first paragraph of a <c>body</c> or a table cell has no top margin, and
    /// the other rules of the Quirks Mode Standard and of the HTML Standard's "in quirks mode" rules apply.
    /// </summary>
    Quirks
}
