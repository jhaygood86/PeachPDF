namespace PeachPDF.CSS
{
    /// <summary>
    /// The four CSS Page Floats <c>float-reference</c> keywords - which fragmentation context a float is
    /// positioned relative to.
    /// </summary>
    internal enum FloatReference : byte
    {
        /// <summary>The initial value: the float's own line box. A footnote has no inline float
        /// reference, so for one this behaves as <see cref="Page"/>.</summary>
        Inline,

        /// <summary>The column of a multi-column container the float's anchor sits in.</summary>
        Column,

        /// <summary>A CSS Regions region. PeachPDF implements no part of CSS Regions, so for a footnote
        /// this behaves as <see cref="Page"/>.</summary>
        Region,

        /// <summary>The page the float's anchor sits on.</summary>
        Page
    }
}
