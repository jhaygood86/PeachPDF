using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Adapters
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

        private static PdfSharpAdapter AdapterWithRegisteredFamily(string familyName)
        {
            var adapter = new PdfSharpAdapter();
            adapter.AddFontFamily(new FontFamilyAdapter(new XFontFamily(familyName)));
            return adapter;
        }

        [Fact]
        public void DefaultFontMissing_AdoptsTheRegisteredFamily()
        {
            var adapter = AdapterWithRegisteredFamily("First Family");
            Assert.False(adapter.IsFontExists(UnresolvableDefault));

            adapter.AdoptDefaultFontIfMissing(UnresolvableDefault, "First Family", defaultFontExists: false);

            // IsFontExists follows one mapping hop, so the default now resolves even though no family
            // literally named UnresolvableDefault was ever registered.
            Assert.True(adapter.IsFontExists(UnresolvableDefault));
        }

        [Fact]
        public void DefaultFontAlreadyResolves_LeavesItAlone()
        {
            var adapter = AdapterWithRegisteredFamily("First Family");

            adapter.AdoptDefaultFontIfMissing(UnresolvableDefault, "First Family", defaultFontExists: true);

            Assert.False(adapter.IsFontExists(UnresolvableDefault));
        }

        [Fact]
        public void FirstRegisteredFamilyWins_NotTheLast()
        {
            var adapter = AdapterWithRegisteredFamily("First Family");

            adapter.AdoptDefaultFontIfMissing(UnresolvableDefault, "First Family", defaultFontExists: false);

            // The real caller re-asks after every registration, and the first mapping makes the answer true -
            // which is the whole latch. Feeding that recomputed answer back in is what makes this a test of
            // the latch rather than of the branch in isolation.
            adapter.AdoptDefaultFontIfMissing(
                UnresolvableDefault, "Second Family Never Registered", adapter.IsFontExists(UnresolvableDefault));

            // If the latch failed, the default would now point at an unregistered family and stop resolving.
            Assert.True(adapter.IsFontExists(UnresolvableDefault));
        }
    }
}
