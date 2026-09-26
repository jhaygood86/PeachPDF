using System;

namespace PeachPDF.Fonts
{
    /// <summary>
    /// Which parts of a requested style the resolved face does not have and the renderer must fake: bold by
    /// emboldening the outlines, italic by shearing them.
    /// </summary>
    [Flags]
    internal enum SyntheticStyle
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        BoldItalic = Bold | Italic,
    }
}
