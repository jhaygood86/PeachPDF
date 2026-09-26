using PeachPDF.Adapters;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>The members of <see cref="BundledFonts"/> that need PeachPDF's font adapter, so the rest can be shared with the engine's own tests.</summary>
    internal static partial class BundledFonts
    {
        /// <summary>
        /// Registers <paramref name="fontPath"/> on <paramref name="adapter"/> under
        /// <paramref name="familyName"/> directly via <see cref="PdfSharpAdapter.AddFont(Stream, string?)"/>,
        /// bypassing CSS <c>@font-face</c> entirely. For a test whose actual subject is layout or paint
        /// (not CSS <c>@font-face</c> parsing/loading itself), prefer this over embedding the font as a
        /// <see cref="FontFaceRule"/> data: URI - CSS's <c>url()</c> value handling re-materializes a
        /// large token's content on every access rather than once (see
        /// .claude/recent-fixes/2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md),
        /// so embedding a multi-megabyte font (e.g. <see cref="Math"/>) that way costs roughly 500-700ms
        /// per call versus about 1ms for this direct registration - the same real font, the same
        /// registered family name, none of the CSS-parsing overhead neither the test nor its assertions
        /// have anything to do with.
        /// </summary>
        internal static async Task RegisterFont(PdfSharpAdapter adapter, string fontPath, string familyName)
        {
            using var stream = File.OpenRead(fontPath);
            await adapter.AddFont(stream, familyName);
        }

        /// <summary>
        /// Liberation Sans Regular: metrically identical to Arial (same advance widths, same 1.149em line
        /// height), which is the font most layout fixtures here were calibrated against.
        /// </summary>
        internal static string LiberationSans => Path.Combine(AppContext.BaseDirectory, "LiberationSans-Regular.woff");

        /// <summary>
        /// Points the <c>sans-serif</c> generic at the bundled Liberation Sans on <paramref name="adapter"/>, so a
        /// fixture that says <c>font: 12px sans-serif</c> measures the same on every host. Without it the
        /// generic resolves to whatever the platform maps it to - Arial, Helvetica, DejaVu Sans, a fontconfig
        /// answer - and those differ in line height (Helvetica's ascent + descent is exactly 1em, Arial's is
        /// about 1.15em) and glyph widths, which is enough to flip a test whose geometry or guard is
        /// calibrated to one of them. Arial's metrics are the ones being pinned, because they are what the
        /// existing fixtures were written against.
        /// Use it for a layout fixture whose subject is not which font the host resolves.
        /// </summary>
        internal static async Task PinSansSerifAsync(PdfSharpAdapter adapter)
        {
            const string pinnedFamily = "PinnedSans";
            await RegisterFont(adapter, LiberationSans, pinnedFamily);
            adapter.AddFontFamilyMapping("sans-serif", pinnedFamily);
        }
    }
}
