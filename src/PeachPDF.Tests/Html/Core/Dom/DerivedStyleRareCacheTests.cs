using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// The paint-side and shaping caches of <see cref="DerivedStyle"/> live in side objects, allocated only for a box that
    /// uses them; these pin that an ordinary box does not pay for them and that a box that does gets the same values as before.
    /// </summary>
    public class DerivedStyleRareCacheTests
    {
        [Fact]
        public async Task APlainBox_AfterLayout_HoldsNoPaintExtras()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<div id='d'><p id='p'>plain text</p></div>"));

            foreach (var id in new[] { "d", "p" })
            {
                var box = LayoutHarness.FindById(root, id)!;

                Assert.False(box.DerivedStyle.HasPaintExtras);
                Assert.False(box.DerivedStyle.IsTransformed);
                Assert.False(box.DerivedStyle.HasTransform);
                Assert.Equal(0, box.DerivedStyle.ActualPerspective);
                Assert.Null(box.DerivedStyle.ActualTransform4);
                Assert.False(box.DerivedStyle.HasPaintExtras);
            }
        }

        [Fact]
        public async Task ATransformedBox_ComputesItsMatrix_AndKeepsItsExtras()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='transform: translate(10pt, 20pt); perspective: 300pt; width: 100pt; height: 50pt'>x</div>"));

            var box = LayoutHarness.FindById(root, "t")!;

            Assert.True(box.DerivedStyle.HasTransform);
            Assert.True(box.DerivedStyle.IsTransformed);
            Assert.Equal(10, box.DerivedStyle.ActualTransformMatrix.M31, 3);
            Assert.Equal(20, box.DerivedStyle.ActualTransformMatrix.M32, 3);
            Assert.Equal(300, box.DerivedStyle.ActualPerspective, 3);
            Assert.True(box.DerivedStyle.HasPaintExtras);
        }

        [Fact]
        public async Task ARoundedBox_ReportsItsRadii_AndAnInvalidationRecomputesThem()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='r' style='border-radius: 8pt; width: 100pt; height: 50pt'>x</div>"));

            var box = LayoutHarness.FindById(root, "r")!;

            Assert.Equal(8, box.DerivedStyle.ActualBorderTopLeftRadiusX, 3);
            Assert.True(box.DerivedStyle.HasPaintExtras);

            box.BorderTopLeftRadius = "2pt";
            Assert.Equal(2, box.DerivedStyle.ActualBorderTopLeftRadiusX, 3);
        }

        [Fact]
        public async Task ABoxWithABorder_CachesItsBorderColours_WithoutTheOtherPaintExtras()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='border: 2pt solid #112233; width: 100pt; height: 50pt'>x</div>"));

            var box = LayoutHarness.FindById(root, "b")!;
            var style = box.DerivedStyle;

            var before = style.ActualBorderTopColor;
            Assert.Equal(PaintColor.FromArgb(0x11, 0x22, 0x33), before);
            Assert.Equal(before, style.ActualBorderRightColor);
            Assert.Equal(before, style.ActualBorderBottomColor);
            Assert.Equal(before, style.ActualBorderLeftColor);
            Assert.False(style.HasPaintExtras);

            // An invalidated side is worked out again from the cascaded value; the others keep what they had.
            box.BorderTopColor = "#445566";
            box.BorderLeftColor = "#778899";
            Assert.Equal(PaintColor.FromArgb(0x44, 0x55, 0x66), style.ActualBorderTopColor);
            Assert.Equal(PaintColor.FromArgb(0x77, 0x88, 0x99), style.ActualBorderLeftColor);
            Assert.Equal(before, style.ActualBorderRightColor);
            Assert.Equal(before, style.ActualBorderBottomColor);

            box.BorderRightColor = "#aabbcc";
            box.BorderBottomColor = "#ddeeff";
            Assert.Equal(PaintColor.FromArgb(0xaa, 0xbb, 0xcc), style.ActualBorderRightColor);
            Assert.Equal(PaintColor.FromArgb(0xdd, 0xee, 0xff), style.ActualBorderBottomColor);
            Assert.False(style.HasPaintExtras);
        }

        [Fact]
        public void InvalidatingABoxThatNeverAllocatedExtras_AllocatesNothing()
        {
            var box = new CssBox(null, null);

            box.DerivedStyle.InvalidateTransform();
            box.DerivedStyle.InvalidateOutlineWidth();
            box.DerivedStyle.InvalidateBorderTopColor();
            box.DerivedStyle.InvalidateBorderTopLeftRadius();

            Assert.False(box.DerivedStyle.HasPaintExtras);
            Assert.False(box.DerivedStyle.HasShapingExtras);
        }
    }
}
