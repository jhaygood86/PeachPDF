using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Fragments
{
    /// <summary>
    /// Says which boxes a capture or a measurement leaves out, without there having to be a set of them: for a column of a
    /// long container that set is the rest of the document.
    /// </summary>
    internal interface IBoxExclusion
    {
        /// <summary>Whether <paramref name="box"/> is left out.</summary>
        bool Contains(CssBox box);
    }
}
