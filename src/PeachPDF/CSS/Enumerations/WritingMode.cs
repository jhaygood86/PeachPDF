namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>writing-mode</c> property's five values (CSS Writing Modes Level 4 §4.1) -
    /// <c>vertical-rl</c>/<c>vertical-lr</c> are Level 3; <c>sideways-rl</c>/<c>sideways-lr</c> are Level 4
    /// additions.
    /// </summary>
    internal enum WritingMode : byte
    {
        HorizontalTb,
        VerticalRl,
        VerticalLr,
        SidewaysRl,
        SidewaysLr
    }
}
