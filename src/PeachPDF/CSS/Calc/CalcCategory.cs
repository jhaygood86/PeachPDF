using System;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The CSS calc() type-checking category a (sub-)expression resolves to. Flags so that
    /// "Length | Percentage" can represent the combined &lt;length-percentage&gt; type once a
    /// length and a percentage have been added/subtracted together.
    /// </summary>
    [Flags]
    internal enum CalcCategory
    {
        Number = 1,
        Length = 2,
        Percentage = 4,
        LengthPercentage = Length | Percentage
    }
}
