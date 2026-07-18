namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS font-weight value (keyword or numeric) to a concrete CSS Fonts numeric weight
    /// (1-1000), including the <c>bolder</c>/<c>lighter</c> relative keywords, which per CSS2.1/Fonts must
    /// step relative to the parent's own resolved (used) weight rather than always meaning a fixed
    /// "bold"/"normal". Extracted the same way <see cref="FontSizeResolver"/> was, so in-flow content and
    /// any future @page margin-box weight resolution share one implementation.
    /// </summary>
    internal static class FontWeightResolver
    {
        /// <param name="fontWeightValue">The raw CSS font-weight value (keyword or numeric string).</param>
        /// <param name="parentWeight">The parent's own resolved numeric weight, used by <c>bolder</c>/<c>lighter</c>.</param>
        internal static int Resolve(string fontWeightValue, int parentWeight)
        {
            if (int.TryParse(fontWeightValue, out var numeric))
                return numeric;

            return fontWeightValue switch
            {
                CssConstants.Bold => 700,
                // CSS2.1 §15.6's own worked example table ("bolder"/"lighter" columns against an
                // inherited 100-900 value) - not a fixed "always bold"/"always normal" as this box's own
                // FontWeight text might otherwise suggest:
                //   inherited: 100 200 300 400 500 600 700 800 900
                //     bolder:  400 400 400 700 700 900 900 900 900
                //    lighter:  100 100 100 100 100 400 400 700 700
                CssConstants.Bolder => parentWeight switch
                {
                    < 400 => 400,
                    <= 500 => 700,
                    _ => 900
                },
                CssConstants.Lighter => parentWeight switch
                {
                    <= 500 => 100,
                    <= 700 => 400,
                    _ => 700
                },
                _ => 400
            };
        }
    }
}
