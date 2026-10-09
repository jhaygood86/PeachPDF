using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS <c>image-orientation</c> (CSS Images 4 section 5.2): the grammar, the cascade, the orientation PeachImage reports, the oriented
    /// intrinsic size, and the ordered <c>PushTransform</c>/<c>DrawImage</c>/<c>PopTransform</c> calls each painter makes for
    /// each of the eight Exif orientations.
    /// </summary>
    public class ImageOrientationTests
    {
        private static string DataUri(byte[] bytes, string mime) => $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

        private static string Jpeg(int exif) => DataUri(OrientedImageFixture.Jpeg(exif), "image/jpeg");

        // ----------------------------------------------------------------- grammar

        [Theory]
        [InlineData("from-image", true, 0, false)]
        [InlineData("FROM-IMAGE", true, 0, false)]
        [InlineData("none", false, 0, false)]
        [InlineData("0deg", false, 0, false)]
        [InlineData("90deg", false, 1, false)]
        [InlineData("180deg", false, 2, false)]
        [InlineData("270deg", false, 3, false)]
        [InlineData("-90deg", false, 3, false)]
        [InlineData("450deg", false, 1, false)]
        [InlineData("45deg", false, 1, false)]
        [InlineData("44deg", false, 0, false)]
        [InlineData("100grad", false, 1, false)]
        [InlineData("0.25turn", false, 1, false)]
        [InlineData("1.5708rad", false, 1, false)]
        [InlineData("flip", false, 0, true)]
        [InlineData("90deg flip", false, 1, true)]
        [InlineData("flip 90deg", false, 1, true)]
        [InlineData("0deg flip", false, 0, true)]
        [InlineData("  FLIP   270DEG ", false, 3, true)]
        public void Grammar_AcceptsEveryValidForm(string text, bool fromImage, int turns, bool flip)
        {
            Assert.Equal(new ImageOrientation(fromImage, turns, flip), ImageOrientation.TryParse(text));
        }

        [Theory]
        [InlineData("")]
        [InlineData("bogus")]
        [InlineData("90")]
        [InlineData("0")]
        [InlineData("90px")]
        [InlineData("flip flip")]
        [InlineData("90deg 90deg")]
        [InlineData("90deg flip none")]
        [InlineData("none flip")]
        [InlineData("from-image flip")]
        [InlineData("90deg, flip")]
        [InlineData("calc(90deg)")]
        public void Grammar_RejectsEverythingElse(string text)
        {
            Assert.Null(ImageOrientation.TryParse(text));
        }

        [Theory]
        [InlineData(1, 0, false)]
        [InlineData(2, 0, true)]   // mirror horizontal
        [InlineData(3, 2, false)]  // rotate 180
        [InlineData(4, 2, true)]   // mirror vertical = rotate 180 then mirror horizontal
        [InlineData(5, 1, true)]   // transpose
        [InlineData(6, 1, false)]  // rotate 90 cw
        [InlineData(7, 3, true)]   // transverse
        [InlineData(8, 3, false)]  // rotate 270 cw
        [InlineData(0, 0, false)]
        [InlineData(9, 0, false)]
        public void Exif_ValuesMapToRotationAndFlip(int exif, int turns, bool flip)
        {
            Assert.Equal(new ImageOrientation(false, turns, flip), ImageOrientation.FromExif(exif));
        }

        [Fact]
        public void Exif_MappingMatchesTheExifDefinitionOnAPixelGrid()
        {
            // For each Exif value, where does the stored pixel (x, y) of a W x H raster land in the upright picture?
            const int w = 5, h = 3;
            Func<int, int, (int X, int Y)>[] definition =
            [
                (x, y) => (x, y),                   // 1 upright
                (x, y) => (w - 1 - x, y),           // 2 mirror horizontal
                (x, y) => (w - 1 - x, h - 1 - y),   // 3 rotate 180
                (x, y) => (x, h - 1 - y),           // 4 mirror vertical
                (x, y) => (y, x),                   // 5 transpose
                (x, y) => (h - 1 - y, x),           // 6 rotate 90 clockwise
                (x, y) => (h - 1 - y, w - 1 - x),   // 7 transverse
                (x, y) => (y, w - 1 - x),           // 8 rotate 270 clockwise
            ];

            for (var exif = 1; exif <= 8; exif++)
            {
                var o = ImageOrientation.FromExif(exif);
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        // rotate clockwise by the quarter turns, then mirror horizontally.
                        var (px, py) = (x, y);
                        var (cw, ch) = (w, h);
                        for (var t = 0; t < o.QuarterTurns; t++)
                        {
                            (px, py) = (ch - 1 - py, px);
                            (cw, ch) = (ch, cw);
                        }

                        if (o.Flip) px = cw - 1 - px;

                        Assert.Equal(definition[exif - 1](x, y), (px, py));
                    }
            }
        }

        // ----------------------------------------------------------------- decoded source

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
        public void DecodedImages_ExposeTheStoredOrientationAndRawDimensions(int exif)
        {
            foreach (var bytes in new[] { OrientedImageFixture.Jpeg(exif), OrientedImageFixture.Png(exif) })
            {
                var adapter = new ImageAdapter(XImage.FromStream(() => new MemoryStream(bytes)));
                Assert.Equal(exif, adapter.ExifOrientation);
                Assert.Equal(exif, ImageOrientationResolver.ExifOrientationOf(adapter));
                Assert.Equal(24, adapter.Width);   // the stored raster, never swapped
                Assert.Equal(16, adapter.Height);
            }
        }

        [Theory]
        [InlineData(0)] [InlineData(9)] [InlineData(255)]
        public void DecodedImages_WithAnOutOfRangeOrientationTagAreUpright(int exif)
        {
            var adapter = new ImageAdapter(XImage.FromStream(() => new MemoryStream(OrientedImageFixture.Jpeg(exif))));
            Assert.Equal(1, adapter.ExifOrientation);
        }

        // ----------------------------------------------------------------- cascade

        private static async Task<CssBox> BoxAsync(string html, string id)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            return LayoutHarness.FindById(root, id)!;
        }

        [Fact]
        public async Task Cascade_InitialValueIsFromImage()
        {
            Assert.Equal("from-image", (await BoxAsync("<div id='a'></div>", "a")).ImageOrientation);
        }

        [Fact]
        public async Task Cascade_IsInherited()
        {
            var child = await BoxAsync("<div style='image-orientation:90deg flip'><div><span id='a'>x</span></div></div>", "a");
            Assert.Equal("90deg flip", child.ImageOrientation);
        }

        [Fact]
        public async Task Cascade_ChildOverridesAndGlobalKeywordsWork()
        {
            Assert.Equal("none", (await BoxAsync("<div style='image-orientation:90deg'><p id='a' style='image-orientation:none'></p></div>", "a")).ImageOrientation);
            Assert.Equal("90deg", (await BoxAsync("<div style='image-orientation:90deg'><p id='a' style='image-orientation:inherit'></p></div>", "a")).ImageOrientation);
            Assert.Equal("from-image", (await BoxAsync("<div style='image-orientation:90deg'><p id='a' style='image-orientation:initial'></p></div>", "a")).ImageOrientation);
            Assert.Equal("90deg", (await BoxAsync("<div style='image-orientation:90deg'><p id='a' style='image-orientation:unset'></p></div>", "a")).ImageOrientation);
            Assert.Equal("from-image", (await BoxAsync("<div style='image-orientation:90deg'><p id='a' style='all:initial'></p></div>", "a")).ImageOrientation);
        }

        [Fact]
        public async Task Cascade_InvalidDeclarationIsDropped()
        {
            Assert.Equal("90deg", (await BoxAsync("<div style='image-orientation:90deg'><p id='a' style='image-orientation:bogus'></p></div>", "a")).ImageOrientation);
            Assert.Equal("from-image", (await BoxAsync("<p id='a' style='image-orientation:90'></p>", "a")).ImageOrientation);
        }

        [Fact]
        public async Task Cascade_StylesheetRuleApplies()
        {
            var box = await BoxAsync("<style>.r { image-orientation: flip 180deg }</style><div id='a' class='r'></div>", "a");
            Assert.Equal("flip 180deg", box.ImageOrientation);
        }

        // ----------------------------------------------------------------- sizing

        private static async Task<(double W, double H)> ImgSizeAsync(string src, string style = "")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<img id='i' style='display:block;{style}' src='{src}'>"));
            var box = LayoutHarness.FindById(root, "i")!;
            var word = box.Words[0];
            return (word.Width, word.Height);
        }

        [Fact]
        public async Task Sizing_Upright_UsesStoredDimensions()
        {
            var (w, h) = await ImgSizeAsync(Jpeg(1));
            Assert.Equal(24 * 0.75, w, 3);
            Assert.Equal(16 * 0.75, h, 3);
        }

        [Theory]
        [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
        public async Task Sizing_QuarterTurnExif_SwapsTheIntrinsicSize(int exif)
        {
            var (w, h) = await ImgSizeAsync(Jpeg(exif));
            Assert.Equal(16 * 0.75, w, 3);
            Assert.Equal(24 * 0.75, h, 3);
        }

        [Theory]
        [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public async Task Sizing_HalfTurnOrMirrorExif_KeepsTheIntrinsicSize(int exif)
        {
            var (w, h) = await ImgSizeAsync(Jpeg(exif));
            Assert.Equal(24 * 0.75, w, 3);
            Assert.Equal(16 * 0.75, h, 3);
        }

        [Fact]
        public async Task Sizing_NoneIgnoresExif_AndAnExplicitAngleReplacesIt()
        {
            var (nw, nh) = await ImgSizeAsync(Jpeg(6), "image-orientation:none");
            Assert.Equal((24 * 0.75, 16 * 0.75), (nw, nh));

            var (rw, rh) = await ImgSizeAsync(Jpeg(1), "image-orientation:90deg");
            Assert.Equal((16 * 0.75, 24 * 0.75), (rw, rh));

            var (fw, fh) = await ImgSizeAsync(Jpeg(6), "image-orientation:flip");
            Assert.Equal((24 * 0.75, 16 * 0.75), (fw, fh));
        }

        [Fact]
        public async Task Sizing_OneAxisSetKeepsTheOrientedAspectRatio()
        {
            var (w, h) = await ImgSizeAsync(Jpeg(6), "width:48pt");
            Assert.Equal(48, w, 3);
            Assert.Equal(72, h, 3); // 16:24 upright, not 24:16
        }

        // ----------------------------------------------------------------- painting

        private static async Task<TestRecordingGraphics> PaintAsync(string body)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var recorder = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, container.Root!, recorder);
            return recorder;
        }

        public enum Corner { TopLeft, TopRight, BottomRight, BottomLeft }

        private static Vector2 CornerOf(Rect r, Corner c) => c switch
        {
            Corner.TopLeft => new Vector2((float)r.Left, (float)r.Top),
            Corner.TopRight => new Vector2((float)r.Right, (float)r.Top),
            Corner.BottomRight => new Vector2((float)r.Right, (float)r.Bottom),
            _ => new Vector2((float)r.Left, (float)r.Bottom),
        };

        private static void AssertNear(Vector2 expected, Vector2 actual) =>
            Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

        private static string[] TransformOrder(TestRecordingGraphics g) => g.Log
            .Where(e => e is TestRecordingGraphics.PushTransformCall or TestRecordingGraphics.DrawImageCall or TestRecordingGraphics.PopTransformCall)
            .Select(e => e.GetType().Name).ToArray();

        // Where the stored raster's top-left, top-right and bottom-right corners land in the upright destination.
        public static TheoryData<int, Corner, Corner, Corner> ExifCorners => new()
        {
            { 2, Corner.TopRight, Corner.TopLeft, Corner.BottomLeft },
            { 3, Corner.BottomRight, Corner.BottomLeft, Corner.TopLeft },
            { 4, Corner.BottomLeft, Corner.BottomRight, Corner.TopRight },
            { 5, Corner.TopLeft, Corner.BottomLeft, Corner.BottomRight },
            { 6, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft },
            { 7, Corner.BottomRight, Corner.TopRight, Corner.TopLeft },
            { 8, Corner.BottomLeft, Corner.TopLeft, Corner.TopRight },
        };

        [Fact]
        public async Task Img_UprightExifMakesTheOriginalPlainCall()
        {
            var g = await PaintAsync($"<img style='width:40pt;height:30pt' src='{Jpeg(1)}'>");

            Assert.Empty(g.PushTransformCalls);
            Assert.Equal(0, g.PopTransformCount);
            var plain = Assert.Single(g.DrawImageCalls);
            Assert.Equal(40, plain.DestRect.Width, 3);
            Assert.Equal(30, plain.DestRect.Height, 3);
        }

        [Theory]
        [MemberData(nameof(ExifCorners))]
        public async Task Img_PaintsEachExifOrientationThroughItsMatrix(int exif, Corner topLeft, Corner topRight, Corner bottomRight)
        {
            var g = await PaintAsync($"<img style='width:40pt;height:30pt' src='{Jpeg(exif)}'>");

            var push = Assert.Single(g.Log.OfType<TestRecordingGraphics.PushTransformCall>());
            var local = Assert.Single(g.DrawImageCalls).DestRect;
            Assert.Equal(1, g.PopTransformCount);
            Assert.Equal(["PushTransformCall", "DrawImageCall", "PopTransformCall"], TransformOrder(g));

            // The stored raster is drawn unrotated: 40x30 for a mirror/half turn, 30x40 for a quarter turn.
            var swaps = ImageOrientation.FromExif(exif).SwapsAxes;
            Assert.Equal(swaps ? 30 : 40, local.Width, 3);
            Assert.Equal(swaps ? 40 : 30, local.Height, 3);

            // The upright rectangle is the one the matrix maps onto: 40 wide, 30 tall, about the same centre.
            var centre = new Vector2((float)(local.X + local.Width / 2), (float)(local.Y + local.Height / 2));
            var upright = new Rect(centre.X - 20, centre.Y - 15, 40, 30);

            AssertNear(CornerOf(upright, topLeft), Vector2.Transform(CornerOf(local, Corner.TopLeft), push.Matrix));
            AssertNear(CornerOf(upright, topRight), Vector2.Transform(CornerOf(local, Corner.TopRight), push.Matrix));
            AssertNear(CornerOf(upright, bottomRight), Vector2.Transform(CornerOf(local, Corner.BottomRight), push.Matrix));
        }

        [Fact]
        public async Task Img_NoneAndAnUntaggedImageMakeNoTransform()
        {
            var none = await PaintAsync($"<img style='width:40pt;height:30pt;image-orientation:none' src='{Jpeg(6)}'>");
            Assert.Empty(none.PushTransformCalls);
            var draw = Assert.Single(none.DrawImageCalls);
            Assert.Equal((40, 30), (Math.Round(draw.DestRect.Width, 3), Math.Round(draw.DestRect.Height, 3)));

            var untagged = await PaintAsync($"<img style='width:40pt;height:30pt' src='{DataUri(OrientedImageFixture.Png(0), "image/png")}'>");
            Assert.Empty(untagged.PushTransformCalls);
        }

        [Fact]
        public async Task Img_ExplicitAngleAndFlipReplaceExif()
        {
            // exif 6 would be a quarter turn; "90deg flip" is the transpose (exif 5) instead.
            var g = await PaintAsync($"<img style='width:40pt;height:30pt;image-orientation:90deg flip' src='{Jpeg(6)}'>");
            var push = Assert.Single(g.PushTransformCalls);
            var local = Assert.Single(g.DrawImageCalls).DestRect;
            var centre = new Vector2((float)(local.X + local.Width / 2), (float)(local.Y + local.Height / 2));
            var upright = new Rect(centre.X - 20, centre.Y - 15, 40, 30);

            AssertNear(CornerOf(upright, Corner.TopLeft), Vector2.Transform(CornerOf(local, Corner.TopLeft), push));
            AssertNear(CornerOf(upright, Corner.BottomLeft), Vector2.Transform(CornerOf(local, Corner.TopRight), push));
        }

        [Fact]
        public async Task Img_AnExplicitAngleIsInheritedAndAppliesToAnImageWithoutExif()
        {
            var png = DataUri(OrientedImageFixture.Png(0), "image/png");
            var g = await PaintAsync($"<div style='image-orientation:180deg'><img style='width:40pt;height:30pt' src='{png}'></div>");
            var push = Assert.Single(g.PushTransformCalls);
            var local = Assert.Single(g.DrawImageCalls).DestRect;
            Assert.Equal((40, 30), (Math.Round(local.Width, 3), Math.Round(local.Height, 3)));
            AssertNear(new Vector2((float)local.Right, (float)local.Bottom), Vector2.Transform(CornerOf(local, Corner.TopLeft), push));
        }

        [Fact]
        public async Task ObjectFit_UsesTheOrientedRatio()
        {
            // 24x16 stored, exif 6 -> 16x24 upright (portrait). contain in a 60x60 box: 40 wide x 60 tall, centred.
            var g = await PaintAsync($"<img style='width:60pt;height:60pt;object-fit:contain' src='{Jpeg(6)}'>");
            var push = Assert.Single(g.PushTransformCalls);
            var local = Assert.Single(g.DrawImageCalls).DestRect;
            // local is the stored (landscape) rect: 60 wide x 40 tall; the matrix maps it to the 40 x 60 upright rect.
            Assert.Equal((60, 40), (Math.Round(local.Width, 3), Math.Round(local.Height, 3)));
            var a = Vector2.Transform(CornerOf(local, Corner.TopLeft), push);
            var b = Vector2.Transform(CornerOf(local, Corner.BottomRight), push);
            Assert.Equal(40, Math.Abs(a.X - b.X), 3);
            Assert.Equal(60, Math.Abs(a.Y - b.Y), 3);
        }

        [Fact]
        public async Task Background_PaintsOrientedAndSizesFromTheOrientedImage()
        {
            var g = await PaintAsync($"<div style='width:80pt;height:80pt;background-image:url({Jpeg(6)});background-repeat:no-repeat'></div>");
            var push = Assert.Single(g.PushTransformCalls);
            var draw = Assert.Single(g.DrawImageCalls);
            Assert.Equal(1, g.PopTransformCount);

            // Upright natural size is 16 x 24 px = 12 x 18 pt; the stored rect drawn is 18 x 12 pt.
            Assert.Equal((18, 12), (Math.Round(draw.DestRect.Width, 3), Math.Round(draw.DestRect.Height, 3)));
            var a = Vector2.Transform(CornerOf(draw.DestRect, Corner.TopLeft), push);
            var b = Vector2.Transform(CornerOf(draw.DestRect, Corner.BottomRight), push);
            Assert.Equal(12, Math.Abs(a.X - b.X), 3);
            Assert.Equal(18, Math.Abs(a.Y - b.Y), 3);
            // The whole stored raster is drawn (no source crop).
            Assert.True(draw.SrcRect is null || (draw.SrcRect.Value.Width == 24 && draw.SrcRect.Value.Height == 16));
        }

        [Fact]
        public async Task Background_Repeating_TransformsEveryTile()
        {
            var g = await PaintAsync($"<div style='width:36pt;height:36pt;background-image:url({Jpeg(6)});background-size:12pt 18pt'></div>");
            Assert.True(g.DrawImageCalls.Count > 1);
            Assert.Equal(g.DrawImageCalls.Count, g.PushTransformCount);
            Assert.Equal(g.PushTransformCount, g.PopTransformCount);
        }

        [Fact]
        public async Task Background_NoneIsTheRawRasterWithNoTransform()
        {
            var g = await PaintAsync($"<div style='width:80pt;height:80pt;background-image:url({Jpeg(6)});background-repeat:no-repeat;image-orientation:none'></div>");
            Assert.Empty(g.PushTransformCalls);
            var draw = Assert.Single(g.DrawImageCalls);
            Assert.Equal((18, 12), (Math.Round(draw.DestRect.Width, 3), Math.Round(draw.DestRect.Height, 3)));
        }

        [Fact]
        public async Task Background_GradientIsNeverOriented()
        {
            var g = await PaintAsync("<div style='width:40pt;height:40pt;background-image:linear-gradient(red,blue);image-orientation:90deg'></div>");
            Assert.Empty(g.PushTransformCalls);
        }

        [Fact]
        public async Task BorderImage_CutsSlicesFromTheOrientedPictureAndTurnsEachOne()
        {
            var style = $"width:40pt;height:40pt;border:8pt solid black;border-image-source:url({Jpeg(6)});border-image-slice:4;border-image-width:8pt";
            var g = await PaintAsync($"<div style='{style}'></div>");

            Assert.NotEmpty(g.DrawImageCalls);
            Assert.Equal(g.DrawImageCalls.Count, g.PushTransformCount);
            Assert.Equal(g.PushTransformCount, g.PopTransformCount);

            // Every region is drawn between a push and a pop, with its source rectangle inside the stored 24 x 16 raster.
            var order = TransformOrder(g);
            for (var i = 0; i + 2 < order.Length; i += 3)
                Assert.Equal(["PushTransformCall", "DrawImageCall", "PopTransformCall"], order.Skip(i).Take(3));

            foreach (var d in g.DrawImageCalls)
            {
                var src = d.SrcRect!.Value;
                Assert.True(src.Left >= -1e-3 && src.Top >= -1e-3 && src.Right <= 24 + 1e-3 && src.Bottom <= 16 + 1e-3, $"source {src} outside the stored raster");
            }

            // A corner slice of 4 oriented px (the oriented picture is 16 x 24) is 4 x 4 stored px.
            Assert.Contains(g.DrawImageCalls, d => Math.Abs(d.SrcRect!.Value.Width - 4) < 1e-3 && Math.Abs(d.SrcRect!.Value.Height - 4) < 1e-3);
        }

        [Fact]
        public async Task BorderImage_NoneUsesTheStoredPictureDirectly()
        {
            var style = $"width:40pt;height:40pt;border:8pt solid black;border-image-source:url({Jpeg(6)});border-image-slice:4;border-image-width:8pt;image-orientation:none";
            var g = await PaintAsync($"<div style='{style}'></div>");
            Assert.NotEmpty(g.DrawImageCalls);
            Assert.Empty(g.PushTransformCalls);
        }

        // ----------------------------------------------------------------- end to end

        private static readonly Regex QuarterTurnCm =
            new(@"(?m)^\s*0(\.0+)?\s+-?\d+(\.\d+)?\s+-?\d+(\.\d+)?\s+0(\.0+)?\s+-?[\d.]+\s+-?[\d.]+\s+cm\b");

        private static async Task<string> PdfTextAsync(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task EndToEnd_AnExifRotatedJpegKeepsItsStoredRasterAndGainsARotatedCm()
        {
            var rotated = await PdfTextAsync($"<html><body><img style='width:60pt;height:90pt' src='{Jpeg(6)}'></body></html>");
            var none = await PdfTextAsync($"<html><body><img style='width:90pt;height:60pt;image-orientation:none' src='{Jpeg(6)}'></body></html>");

            // The image XObject is the stored raster (24 x 16), not a rotated 16 x 24 re-encode.
            Assert.Matches(new Regex(@"/Width\s+24\b"), rotated);
            Assert.Matches(new Regex(@"/Height\s+16\b"), rotated);

            // The quarter turn is a cm with a zero diagonal; the "none" document has no such matrix.
            Assert.Matches(QuarterTurnCm, rotated);
            Assert.DoesNotMatch(QuarterTurnCm, none);
        }

        [Fact]
        public async Task EndToEnd_ExifOrientedPngStaysAPassThroughAndIsRotatedByCm()
        {
            var png = OrientedImageFixture.Png(6);
            var pdf = await PdfTextAsync($"<html><body><img style='width:60pt;height:90pt' src='{DataUri(png, "image/png")}'></body></html>");

            Assert.Matches(new Regex(@"/Width\s+24\b"), pdf);
            Assert.Matches(new Regex(@"/Height\s+16\b"), pdf);
            Assert.Matches(QuarterTurnCm, pdf);
        }
    }
}
