using System.Globalization;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XColorResourceManagerTests
    {
        static XColorResourceManagerTests()
        {
            // XKnownColorTable.ColorTable is lazily populated only by KnownColorToArgb -- GetKnownColor
            // and IsKnownColor assume it is already populated and throw NullReferenceException if
            // called before anything has forced initialization. Touching a known color here (as any
            // real caller normally would, e.g. via XColors.Red) avoids making these tests depend on
            // incidental ordering against unrelated tests elsewhere in the assembly.
            _ = XColors.Black;
        }

        [Fact]
        public void GetKnownColor_KnownArgb_ReturnsMatchingColor()
        {
            var known = XColorResourceManager.GetKnownColor(0xFFFF0000);

            Assert.Equal(XKnownColor.Red, known);
        }

        [Fact]
        public void GetKnownColor_UnknownArgb_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => XColorResourceManager.GetKnownColor(0x12345678));
        }

        [Fact]
        public void GetKnownColors_IncludingTransparent_ContainsTransparent()
        {
            var colors = XColorResourceManager.GetKnownColors(includeTransparent: true);

            Assert.Contains(XKnownColor.Transparent, colors);
        }

        [Fact]
        public void GetKnownColors_ExcludingTransparent_OmitsTransparentAndIsOneShorter()
        {
            var withTransparent = XColorResourceManager.GetKnownColors(includeTransparent: true);
            var withoutTransparent = XColorResourceManager.GetKnownColors(includeTransparent: false);

            Assert.DoesNotContain(XKnownColor.Transparent, withoutTransparent);
            Assert.Equal(withTransparent.Length - 1, withoutTransparent.Length);
        }

        [Fact]
        public void ToColorName_KnownColor_EnglishCulture_ReturnsEnglishName()
        {
            var manager = new XColorResourceManager(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("Red", manager.ToColorName(XKnownColor.Red));
        }

        [Fact]
        public void ToColorName_KnownColor_GermanCulture_ReturnsGermanName()
        {
            var manager = new XColorResourceManager(CultureInfo.GetCultureInfo("de-DE"));

            Assert.Equal("Rot", manager.ToColorName(XKnownColor.Red));
        }

        [Fact]
        public void ToColorName_KnownXColor_ReturnsColorNameNotArgb()
        {
            var manager = new XColorResourceManager(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("Red", manager.ToColorName(XColors.Red));
        }

        [Fact]
        public void ToColorName_UnknownXColor_ReturnsArgbComponents()
        {
            var manager = new XColorResourceManager(CultureInfo.GetCultureInfo("en-US"));
            var custom = XColor.FromArgb(10, 20, 30);

            var name = manager.ToColorName(custom);

            Assert.Equal("255, 10, 20, 30", name);
        }

        [Fact]
        public void DefaultConstructor_UsesCurrentUICulture()
        {
            var ex = Record.Exception(() => new XColorResourceManager().ToColorName(XKnownColor.Blue));

            Assert.Null(ex);
        }
    }
}
