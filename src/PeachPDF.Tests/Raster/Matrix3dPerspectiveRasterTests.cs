using PeachDrawing;
using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;
using System.Globalization;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// The perspective entries of <c>matrix3d()</c> (4th, 8th and 12th values) are per CSS pixel, so a <c>matrix3d()</c> spelling of a
    /// <c>perspective()</c> chain must rasterize to the same pixels. The warp is what draws a projective transform, so these tests look at
    /// the pixels it produced rather than at the matrix handed to it.
    /// </summary>
    public class Matrix3dPerspectiveRasterTests
    {
        private const int Width = 240;
        private const int Height = 200;

        private static async Task<RasterCanvas> Paint(string transform)
        {
            var body = $"<div style=\"position:relative;margin:0;width:{Width}pt;height:{Height}pt\">" +
                       $"<div style=\"position:absolute;left:60pt;top:50pt;width:100pt;height:80pt;background:#e80;transform:{transform}\"></div></div>";
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);
            var page = new RasterCanvas(new PdfSharpAdapter(), new RasterSurface(Width, Height, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

        /// <summary><c>perspective(distance) rotateY(angle)</c> written as one matrix3d(): the perspective row folded into the rotation's columns.</summary>
        private static string PerspectiveRotateYAsMatrix3d(double distancePx, double degrees)
        {
            var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
            return $"matrix3d({Number(cos)},0,{Number(-sin)},{Number(sin / distancePx)}, 0,1,0,0, " +
                   $"{Number(sin)},0,{Number(cos)},{Number(-cos / distancePx)}, 0,0,0,1)";
        }

        private static int DifferingPixels(RasterCanvas a, RasterCanvas b)
        {
            var differing = 0;
            for (var y = 0; y < a.Surface.Height; y++)
            {
                var rowA = a.Surface.Row(y);
                var rowB = b.Surface.Row(y);
                for (var x = 0; x < a.Surface.Width; x++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        if (Math.Abs(rowA[x * 4 + channel] - rowB[x * 4 + channel]) > 8)
                        {
                            differing++;
                            break;
                        }
                    }
                }
            }
            return differing;
        }

        private static int CoveredHeight(RasterCanvas g, int x)
        {
            var n = 0;
            for (var y = 0; y < g.Surface.Height; y++)
                if (g.Surface.Row(y)[x * 4 + 3] > 127)
                    n++;
            return n;
        }

        [Fact]
        public async Task Matrix3dSpelling_OfAPerspectiveChain_PaintsTheSamePixels()
        {
            var viaFunction = await Paint("perspective(300px) rotateY(40deg)");
            var viaMatrix = await Paint(PerspectiveRotateYAsMatrix3d(300, 40));

            // The shape is a genuine trapezoid (the near edge is taller than the far one), so the comparison is not of two empty pages.
            var columns = Enumerable.Range(0, Width).Where(x => CoveredHeight(viaFunction, x) > 0).ToList();
            Assert.NotEmpty(columns);
            Assert.True(Math.Abs(CoveredHeight(viaFunction, columns.First() + 2) - CoveredHeight(viaFunction, columns.Last() - 2)) > 8);

            Assert.True(DifferingPixels(viaFunction, viaMatrix) <= 40,
                $"{DifferingPixels(viaFunction, viaMatrix)} pixels differ between perspective(300px) and its matrix3d() spelling");
        }

        [Fact]
        public async Task TheMatrix3dPerspectiveTerms_AreNotReadPerPoint()
        {
            // Read per point, -1/300 would be perspective(300pt) = perspective(400px), a visibly weaker foreshortening than perspective(300px).
            var perspective300px = await Paint("perspective(300px) rotateY(40deg)");
            var perspective400px = await Paint("perspective(400px) rotateY(40deg)");

            Assert.True(DifferingPixels(perspective300px, perspective400px) > 200,
                "the two distances should be distinguishable, otherwise the test above proves nothing");
        }
    }
}
