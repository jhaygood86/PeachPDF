namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>text-orientation</c> property's values (CSS Writing Modes Level 4 §5.2) - only meaningful
    /// when the box's <c>writing-mode</c> is <see cref="WritingMode.VerticalRl"/>/
    /// <see cref="WritingMode.VerticalLr"/>; a no-op under <see cref="WritingMode.HorizontalTb"/> and,
    /// for now, under <c>sideways-rl</c>/<c>sideways-lr</c> too - see
    /// <see cref="PeachPDF.Html.Core.Utils.WritingModeFrame"/>'s remarks.
    /// </summary>
    internal enum TextOrientation : byte
    {
        Mixed,
        Upright,
        Sideways
    }
}
