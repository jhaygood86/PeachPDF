using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// What 3D content is written as: a transform that is, to within a fraction of a device pixel, a plain matrix stays vector artwork, and only one that needs
    /// a real projective warp (or a depth test between planes that cross) becomes a bitmap. A bitmap is an <c>/Image</c> XObject in the PDF, which nothing
    /// else on these pages is.
    /// </summary>
    public class VectorProjectiveTests
    {
        private static readonly Regex ImageXObject = new(@"/Subtype\s*/Image");

        private static async Task<string> Pdf(string body)
        {
            var doc = await new PdfGenerator().GeneratePdf($"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            using var stream = new MemoryStream();
            doc.Save(stream);
            return System.Text.Encoding.Latin1.GetString(stream.ToArray());
        }

        private static bool HasBitmap(string pdf) => ImageXObject.IsMatch(pdf);

        private static string Svg(string content) =>
            $"<svg viewBox='0 0 220 180' style='display:block;width:220pt;height:180pt'>{content}</svg>";

        private static string Card(string colour, string style) =>
            $"<rect x='60' y='40' width='100' height='100' fill='{colour}' style='transform-origin:110px 90px;{style}'/>";

        // ---- HTML ----

        [Fact]
        public async Task Html_AFaintPerspective_IsAPlainMatrix_AStrongOneIsABitmap()
        {
            string Scene(string perspective, string tilt) =>
                $"<div style='perspective:{perspective};width:100pt;height:100pt;margin:20pt'><div style='width:100pt;height:100pt;background:#c33;transform:{tilt}'>words</div></div>";

            Assert.False(HasBitmap(await Pdf(Scene("200000pt", "rotateY(1deg)"))));
            Assert.True(HasBitmap(await Pdf(Scene("200pt", "rotateY(60deg)"))));
        }

        [Fact]
        public async Task Html_TiltedPlanesThatDoNotPierceEachOther_StayVectorInA3DContext()
        {
            string Context(string second) =>
                "<div style='position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'>" +
                "<div style='position:absolute;left:30pt;top:20pt;width:100pt;height:100pt;background:#f00;transform:translateZ(60pt) rotateY(10deg)'>a</div>" +
                $"<div style='position:absolute;left:30pt;top:20pt;width:100pt;height:100pt;background:#00f;transform:{second}'>b</div></div></div>";

            // The second plane is the first turned the same way and brought back: parallel, never touching, and both in front of the (empty) root plane.
            Assert.False(HasBitmap(await Pdf(Context("translateZ(30pt) rotateY(10deg)"))));

            // Turned the other way about the same axis, the planes cross: a depth test is needed.
            Assert.True(HasBitmap(await Pdf(Context("translateZ(60pt) rotateY(-10deg)"))));
        }

        // ---- SVG ----

        [Fact]
        public async Task Svg_ARotationWithNoPerspective_IsAPlainMatrix()
        {
            Assert.False(HasBitmap(await Pdf(Svg(Card("#c33", "transform:rotateY(60deg)")))));
        }

        [Fact]
        public async Task Svg_ARealPerspective_IsABitmap_AFaintOneIsNot()
        {
            Assert.True(HasBitmap(await Pdf(Svg($"<g style='perspective:200px;perspective-origin:110px 90px'>{Card("#c33", "transform:rotateY(60deg)")}</g>"))));
            Assert.False(HasBitmap(await Pdf(Svg($"<g style='perspective:2000000px;perspective-origin:110px 90px'>{Card("#c33", "transform:rotateY(1deg)")}</g>"))));
        }

        [Fact]
        public async Task Svg_ThePerspectiveFunctionInATransform_IsABitmap()
        {
            Assert.True(HasBitmap(await Pdf(Svg(Card("#c33", "transform:perspective(200px) rotateY(60deg)")))));
        }

        [Fact]
        public async Task Svg_TiltedPlanesThatDoNotPierce_StayVectorInA3DContext_AndCrossingOnesAreABitmap()
        {
            string Context(string second) =>
                $"<g style='transform-style:preserve-3d'>{Card("#f00", "transform:rotateY(30deg)")}{Card("#00f", $"transform:{second}")}</g>";

            Assert.False(HasBitmap(await Pdf(Svg(Context("rotateY(30deg) translateZ(-40px)")))));
            Assert.True(HasBitmap(await Pdf(Svg(Context("rotateY(-30deg)")))));
        }

        [Fact]
        public async Task Svg_ThePerspectiveOfTheRootSvg_ReachesItsContent()
        {
            var html = $"<svg viewBox='0 0 220 180' style='display:block;width:220pt;height:180pt;perspective:200px;perspective-origin:110px 90px'>{Card("#c33", "transform:rotateY(60deg)")}</svg>";

            Assert.True(HasBitmap(await Pdf(html)));
        }

        [Fact]
        public async Task Svg_ThePerspectiveOfANestedSvg_ReachesItsContent()
        {
            var nested = $"<svg x='0' y='0' width='220' height='180' viewBox='0 0 220 180' style='perspective:200px;perspective-origin:110px 90px'>{Card("#c33", "transform:rotateY(60deg)")}</svg>";

            Assert.True(HasBitmap(await Pdf(Svg(nested))));
        }

        [Fact]
        public async Task Svg_ThePerspectiveOfAUse_ReachesTheSymbolItInstantiates()
        {
            var content = $"<defs><symbol id='s' viewBox='0 0 220 180'>{Card("#c33", "transform:rotateY(60deg)")}</symbol></defs>" +
                          "<use href='#s' width='220' height='180' style='perspective:200px;perspective-origin:110px 90px'/>";

            Assert.True(HasBitmap(await Pdf(Svg(content))));
        }

        [Fact]
        public async Task Svg_ABackfaceHiddenElementTurnedAway_WritesNothingForIt()
        {
            var visible = await Pdf(Svg(Card("#c33", "transform:rotateY(180deg)")));
            var hidden = await Pdf(Svg(Card("#c33", "transform:rotateY(180deg);backface-visibility:hidden")));

            Assert.True(visible.Length > hidden.Length);
        }
    }
}
