using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// A host with no discoverable system fonts - browser/WebAssembly, where
    /// <c>FontResolver.DiscoverSupportedFonts</c> deliberately returns nothing - cannot satisfy
    /// <c>DefaultFontResolver.DefaultFont</c> from the platform, so the engine's last-resort font has to be adopted
    /// from whatever the application registers instead. These pin that rule.
    /// <para>
    /// They use a synthetic default-font name rather than the real <c>DefaultFontResolver.DefaultFont</c>, because
    /// on every desktop CI runner the real one <i>is</i> installed - the branch under test would never be
    /// reached, and the assertions would hold whether or not the code did anything.
    /// </para>
    /// </summary>
    public class DefaultFontFallbackTests
    {
        private const string UnresolvableDefault = "PeachPDF Test Default That Is Not Installed";

        private static HtmlRenderContext RenderContextWithRegisteredFamily(string familyName)
        {
            var renderContext = new HtmlRenderContext();
            renderContext.AddFontFamily(new PdfCanvasFontFamily(familyName));
            return renderContext;
        }

        [Fact]
        public void DefaultFontMissing_AdoptsTheRegisteredFamily()
        {
            var renderContext = RenderContextWithRegisteredFamily("First Family");
            Assert.False(renderContext.IsFontExists(UnresolvableDefault));

            renderContext.AdoptDefaultFontIfMissing(UnresolvableDefault, "First Family", defaultFontExists: false);

            // IsFontExists follows one mapping hop, so the default now resolves even though no family
            // literally named UnresolvableDefault was ever registered.
            Assert.True(renderContext.IsFontExists(UnresolvableDefault));
        }

        [Fact]
        public void DefaultFontAlreadyResolves_LeavesItAlone()
        {
            var renderContext = RenderContextWithRegisteredFamily("First Family");

            renderContext.AdoptDefaultFontIfMissing(UnresolvableDefault, "First Family", defaultFontExists: true);

            Assert.False(renderContext.IsFontExists(UnresolvableDefault));
        }

        [Fact]
        public void FirstRegisteredFamilyWins_NotTheLast()
        {
            var renderContext = RenderContextWithRegisteredFamily("First Family");

            renderContext.AdoptDefaultFontIfMissing(UnresolvableDefault, "First Family", defaultFontExists: false);

            // The real caller re-asks after every registration, and the first mapping makes the answer true -
            // which is the whole latch. Feeding that recomputed answer back in is what makes this a test of
            // the latch rather than of the branch in isolation.
            renderContext.AdoptDefaultFontIfMissing(
                UnresolvableDefault, "Second Family Never Registered", renderContext.IsFontExists(UnresolvableDefault));

            // If the latch failed, the default would now point at an unregistered family and stop resolving.
            Assert.True(renderContext.IsFontExists(UnresolvableDefault));
        }
    }
}
