namespace PeachPDF.CSS
{
    internal enum PositionMode : byte
    {
        Static,
        Relative,
        Absolute,
        Fixed,
        Sticky,

        /// <summary>
        /// css-gcpm-3 <c>running(&lt;custom-ident&gt;)</c> - the element is removed from normal flow
        /// entirely (it is never placed at its original position) and becomes available for a page
        /// margin box to display via <c>content: element(&lt;custom-ident&gt;)</c>. The custom-ident
        /// itself is carried on <c>CssBox.RunningElementName</c>, not in this enum.
        /// </summary>
        Running
    }
}