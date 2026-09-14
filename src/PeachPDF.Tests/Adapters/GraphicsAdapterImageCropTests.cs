using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// <see cref="GraphicsAdapter.DrawImage(RImage, RRect, RRect)"/> must draw only the requested portion of
    /// the image. PDF has no "draw this sub-rectangle of an XObject" operator and PdfSharpCore's own
    /// <c>srcRect</c> overload never implemented one - it silently drew the whole image into the destination
    /// rectangle, so every <c>border-image</c> slice painted the entire source squashed into its own ninth of
    /// the frame. The crop is therefore a clip plus a deliberately off-destination placement of the whole
    /// image, and that is what these tests assert: the arithmetic directly, and the emitted operators
    /// structurally (a clip is established before the <c>Do</c> it bounds).
    /// </summary>
    public class GraphicsAdapterImageCropTests
    {
        private static (PdfDocument Document, XGraphics PageGfx, GraphicsAdapter Graphics, PdfSharpAdapter Adapter) NewPage()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            // A new PdfPage defaults to A4 or Letter depending on RegionInfo.CurrentRegion.IsMetric, so a
            // test asserting literal page-space coordinates has to pin the size - otherwise it passes in a
            // metric region and fails on a US-region machine (as CI's runners are) purely on page height.
            page.Size = PageSize.A4;
            var pageGfx = XGraphics.FromPdfPage(page);
            var adapter = new PdfSharpAdapter();
            var graphics = new GraphicsAdapter(adapter, pageGfx, 1.0);
            return (document, pageGfx, graphics, adapter);
        }

        private static RImage NewTile(GraphicsAdapter graphics, RAdapter adapter, double size, RColor color)
        {
            var (tileGraphics, image) = graphics.CreateTile(size, size)!.Value;
            tileGraphics.DrawRectangle(adapter.GetSolidBrush(color), 0, 0, size, size);
            tileGraphics.Dispose();
            return image;
        }

        private static string Serialize(PdfDocument document)
        {
            var ms = new MemoryStream();
            document.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public void ComputeCroppedPlacement_SourceCoveringAQuarter_PlacesTheWholeImageAtFourTimesTheArea()
        {
            // The bottom-right quarter of a 40x40 image drawn into a 20x20 destination: the whole image
            // must land at its natural scale (20/20), shifted up and left by the slice's own offset so
            // that quarter comes to rest exactly on the destination.
            var placement = GraphicsAdapter.ComputeCroppedPlacement(
                new RRect(10, 10, 20, 20), new RRect(20, 20, 20, 20), naturalWidth: 40, naturalHeight: 40);

            Assert.Equal(-10, placement.X, 4);
            Assert.Equal(-10, placement.Y, 4);
            Assert.Equal(40, placement.Width, 4);
            Assert.Equal(40, placement.Height, 4);
        }

        [Fact]
        public void ComputeCroppedPlacement_ScalesEachAxisIndependently()
        {
            // A border-image edge slice: 2 source pixels tall stretched over a 12pt border, 8 wide over
            // 96pt - a 6x vertical and 12x horizontal scale of the same 12x12 source.
            var placement = GraphicsAdapter.ComputeCroppedPlacement(
                new RRect(0, 0, 96, 12), new RRect(2, 0, 8, 2), naturalWidth: 12, naturalHeight: 12);

            Assert.Equal(-24, placement.X, 4);   // 2 source px left of the destination, at 12x
            Assert.Equal(0, placement.Y, 4);
            Assert.Equal(144, placement.Width, 4);  // 12 px at 12x
            Assert.Equal(72, placement.Height, 4);  // 12 px at 6x
        }

        [Fact]
        public void ComputeCroppedPlacement_TopLeftSlice_KeepsTheDestinationOrigin()
        {
            var placement = GraphicsAdapter.ComputeCroppedPlacement(
                new RRect(30, 40, 10, 10), new RRect(0, 0, 5, 5), naturalWidth: 20, naturalHeight: 20);

            Assert.Equal(30, placement.X, 4);
            Assert.Equal(40, placement.Y, 4);
            Assert.Equal(40, placement.Width, 4);
            Assert.Equal(40, placement.Height, 4);
        }

        [Fact]
        public void CroppedDraw_ClipsAroundTheDestinationBeforeInvokingTheForm()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var tile = NewTile(graphics, adapter, 40, RColor.FromArgb(255, 0, 0));

            graphics.DrawImage(tile, new RRect(10, 10, 20, 20), new RRect(20, 20, 20, 20));
            pageGfx.Dispose();

            var text = Serialize(document);

            // The clip - a 20pt square at the destination, in PDF's y-up space, so the pinned A4 page's
            // 842pt height puts its edges at 832 and 812 - has to be established before the single form
            // invocation it bounds: what falls outside the destination is cut away, not squeezed into it.
            var clipThenDraw = Regex.Match(text, @"10 832 m.*?W\*? n(?:(?!W\*? n).)*?/\w+ Do", RegexOptions.Singleline);
            Assert.True(clipThenDraw.Success, "the cropped draw must clip to its destination before invoking the form");
            Assert.Single(Regex.Matches(text, @"/\w+ Do\b"));

            // ...and the form itself is placed whole at its natural 1:1 scale (the source quarter is the
            // same 20pt the destination is), shifted so that quarter comes to rest on the destination -
            // NOT scaled down to squeeze all 40pt of it into the 20pt destination.
            Assert.Contains("1 0 0 1 -10 812 cm", text);
        }

        [Fact]
        public void WholeImageSource_DrawsWithoutAnyCropClip()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var tile = NewTile(graphics, adapter, 40, RColor.FromArgb(255, 0, 0));

            // Every background layer passes its image in full (BackgroundImageDrawHandler) - that must stay
            // the plain, un-clipped draw it has always been.
            graphics.DrawImage(tile, new RRect(10, 10, 20, 20), new RRect(0, 0, 40, 40));
            pageGfx.Dispose();

            var text = Serialize(document);
            var pageContent = Regex.Match(text, @"stream\r?\n(.*?)endstream", RegexOptions.Singleline).Groups[1].Value;

            Assert.DoesNotContain("re W n", pageContent);
            Assert.Single(Regex.Matches(text, @"/\w+ Do\b"));
        }

        [Fact]
        public void DegenerateSource_DrawsNothing()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var tile = NewTile(graphics, adapter, 40, RColor.FromArgb(255, 0, 0));

            graphics.DrawImage(tile, new RRect(10, 10, 20, 20), new RRect(0, 0, 0, 0));
            pageGfx.Dispose();

            Assert.Empty(Regex.Matches(Serialize(document), @"/\w+ Do\b"));
        }
    }
}
