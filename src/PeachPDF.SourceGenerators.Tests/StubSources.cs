namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>
    /// Minimal stand-ins for the real <c>CssBox</c>/<c>SvgElement</c> shape, for isolated,
    /// PeachPDF-assembly-independent tests of PPG010/PPG011's symbol validation. Tests that need the
    /// *real* shape use <see cref="GeneratorTestHost.PeachPdfReference"/> instead.
    /// </summary>
    internal static class StubSources
    {
        public const string MinimalCssBoxAndSvgElement = """
            namespace PeachPDF.Html.Core.Dom
            {
                public class CssBox
                {
                    public string BorderBottomWidth { get; set; } = "";
                    public string Transform { get; set; } = "";
                    public int NoSetter { get; } = 0;
                }
            }

            namespace PeachPDF.Svg
            {
                public class SvgElement
                {
                    public string Fill { get; set; } = "";
                    public double StrokeWidth { get; set; }
                    public double[] StrokeDashArray { get; set; } = System.Array.Empty<double>();
                }
            }
            """;
    }
}
