using PeachPDF.PdfSharpCore.Drawing;
using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Direct regression test for <see cref="LoadedTypeface.GetOrCreateFrom"/>'s per-instance cache
    /// <b>hit</b> path (a second request for the same custom family+key returns the already-cached
    /// instance) - the companion case to <c>FontFactoryCrossInstanceIsolationTests</c>, which covers the
    /// cross-instance isolation the cache split exists for in the first place.
    /// </summary>
    public class XGlyphTypefaceInstanceCacheTests
    {
        [Fact]
        public void SameCustomFamilyRequestedTwice_ReturnsSameCachedInstance()
        {
            const string family = "TestInstanceCacheFamily";
            var resolver = new FontResolver();
            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, family);

            var options = new FontResolvingOptions(FaceStyle.Regular);

            var first = LoadedTypeface.GetOrCreateFrom(family, options, resolver);
            var second = LoadedTypeface.GetOrCreateFrom(family, options, resolver);

            Assert.Same(first, second);
        }
    }
}
