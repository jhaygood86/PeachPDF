using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for two bugs found while investigating a suspected <c>font-size</c> inheritance issue,
    /// both in how a parent-relative <c>font-size</c> (<c>em</c>, a percentage, or the <c>smaller</c>/
    /// <c>larger</c> keywords) resolves against its parent's already-resolved size:
    /// <list type="bullet">
    /// <item>
    /// <b>Magnitude</b>: <see cref="DerivedStyle.ActualFont"/>'s <c>parentSize</c>/<c>remSize</c> inputs
    /// (read from <c>parentBox.ActualFont.Size</c>/<c>GetRemHeight()</c>) are in the adapter's
    /// device-scaled font-measurement space, not true CSS points - <c>PdfSharpAdapter.CreateFontInt</c>
    /// divides a requested size by <c>PixelsPerPoint</c> once to get there. Feeding that already-divided
    /// value back in as a relative-resolution reference, whose own result is then divided by
    /// <c>PixelsPerPoint</c> *again* when its font is created, produces a font roughly
    /// <c>PixelsPerPoint</c> times too small - invisible only because most of this suite (and the
    /// library's own default <see cref="PeachPDF.PdfGenerateConfig"/>) happens to use a
    /// <c>PixelsPerPoint</c> of <c>1.0</c>. These tests deliberately build through a bare
    /// <see cref="PdfSharpAdapter"/> (default <c>PixelsPerPoint</c> of 72) to actually exercise the
    /// scaling.
    /// </item>
    /// <item>
    /// <b>Compounding across non-overriding descendants</b>: <c>CssBox</c>'s <c>Font</c> area is inherited
    /// by reference (<c>InheritStyle</c> adopts the whole area from parent to child), so whatever string
    /// sits in <c>FontSize</c> at that moment is what every non-overriding descendant, at any depth, ends
    /// up with. A relative value left as raw text for lazy resolution is indistinguishable from a value
    /// merely copied down unchanged, so a box several generations below the one that actually declared,
    /// say, <c>150%</c> would re-resolve that same raw text against its own immediate parent, compounding
    /// the multiplier once per generation instead of applying it once.
    /// </item>
    /// </list>
    /// Both are fixed together: every parent-relative form is eagerly resolved to an absolute point value
    /// in the <c>FontSize</c> setter (<c>CssBox.StyleProperties.cs</c>), using the parent's actual size
    /// with the device-scaling undone.
    /// </summary>
    public class FontSizeInheritanceIntegrationTests
    {
        [Fact]
        public async Task FontSize_PlainEm_ResolvesToTheCorrectAbsoluteMagnitude()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="font-size: 20pt">
                  <div id="child" style="font-size: 1.2em"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var parent = FindById(root, "parent");
            var child = FindById(root, "child");

            Assert.NotNull(parent);
            Assert.NotNull(child);
            Assert.Equal(parent!.ActualFont.Size * 1.2, child!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task FontSize_PlainEx_ResolvesToTheCorrectAbsoluteMagnitude()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="font-size: 20pt">
                  <div id="child" style="font-size: 4ex"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var parent = FindById(root, "parent");
            var child = FindById(root, "child");

            Assert.NotNull(parent);
            Assert.NotNull(child);
            // Length.ToPixels resolves ex as emFactor / 2 * value - "4ex" against parent's font size is
            // 4 * (parent / 2) = 2 * parent.
            Assert.Equal(parent!.ActualFont.Size * 2, child!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task FontSize_ExThreeLevelsDeep_DoesNotCompoundAcrossNonOverridingDescendants()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="grandparent" style="font-size: 20pt">
                  <div id="middle" style="font-size: 4ex">
                    <div id="leaf"></div>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var grandparent = FindById(root, "grandparent");
            var middle = FindById(root, "middle");
            var leaf = FindById(root, "leaf");

            Assert.NotNull(grandparent);
            Assert.NotNull(middle);
            Assert.NotNull(leaf);
            Assert.Equal(grandparent!.ActualFont.Size * 2, middle!.ActualFont.Size, 6);
            Assert.Equal(middle.ActualFont.Size, leaf!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task FontSize_ChThreeLevelsDeep_DoesNotCompoundAcrossNonOverridingDescendants()
        {
            // ch shares ex's exact 0.5em-per-unit formula (Length.ToPixels), and needs the exact same
            // eager cascade-time resolution for the exact same reason (CssBox.StyleProperties.cs'
            // ResolveFontSizeValueComputation) - this mirrors FontSize_ExThreeLevelsDeep_... above.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="grandparent" style="font-size: 20pt">
                  <div id="middle" style="font-size: 4ch">
                    <div id="leaf"></div>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var grandparent = FindById(root, "grandparent");
            var middle = FindById(root, "middle");
            var leaf = FindById(root, "leaf");

            Assert.NotNull(grandparent);
            Assert.NotNull(middle);
            Assert.NotNull(leaf);
            // "4ch" against parent's font size is 4 * (parent * 0.5) = 2 * parent.
            Assert.Equal(grandparent!.ActualFont.Size * 2, middle!.ActualFont.Size, 6);
            Assert.Equal(middle.ActualFont.Size, leaf!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task ActualFontSize_MirrorsActualFontSizeValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="font-size: 20pt">
                  <div id="child" style="font-size: 1.2em"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(child!.ActualFont.Size, child.DerivedStyle.ActualFontSize);
        }

        [Fact]
        public async Task FontSize_PlainPercentage_ResolvesToTheCorrectAbsoluteMagnitude()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="font-size: 20pt">
                  <div id="child" style="font-size: 150%"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var parent = FindById(root, "parent");
            var child = FindById(root, "child");

            Assert.NotNull(parent);
            Assert.NotNull(child);
            Assert.Equal(parent!.ActualFont.Size * 1.5, child!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task FontSize_PercentageThreeLevelsDeep_DoesNotCompoundAcrossNonOverridingDescendants()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="grandparent" style="font-size: 20pt">
                  <div id="middle" style="font-size: 150%">
                    <div id="leaf"></div>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var grandparent = FindById(root, "grandparent");
            var middle = FindById(root, "middle");
            var leaf = FindById(root, "leaf");

            Assert.NotNull(grandparent);
            Assert.NotNull(middle);
            Assert.NotNull(leaf);
            // middle actually resolves 150% of grandparent - not just "whatever leaf also ends up with".
            Assert.Equal(grandparent!.ActualFont.Size * 1.5, middle!.ActualFont.Size, 6);
            // The leaf never declares its own font-size, so its computed font-size must equal the
            // middle's (the nearest ancestor that actually specified one) - not middle's size scaled by
            // another 150%.
            Assert.Equal(middle.ActualFont.Size, leaf!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task FontSize_SmallerKeywordThreeLevelsDeep_DoesNotCompoundAcrossNonOverridingDescendants()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="grandparent" style="font-size: 20pt">
                  <div id="middle" style="font-size: smaller">
                    <div id="leaf"></div>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var grandparent = FindById(root, "grandparent");
            var middle = FindById(root, "middle");
            var leaf = FindById(root, "leaf");

            Assert.NotNull(grandparent);
            Assert.NotNull(middle);
            Assert.NotNull(leaf);
            Assert.True(middle!.ActualFont.Size < grandparent!.ActualFont.Size);
            Assert.Equal(middle.ActualFont.Size, leaf!.ActualFont.Size, 6);
        }

        [Fact]
        public async Task FontSize_LargerKeywordThreeLevelsDeep_DoesNotCompoundAcrossNonOverridingDescendants()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="grandparent" style="font-size: 20pt">
                  <div id="middle" style="font-size: larger">
                    <div id="leaf"></div>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var grandparent = FindById(root, "grandparent");
            var middle = FindById(root, "middle");
            var leaf = FindById(root, "leaf");

            Assert.NotNull(grandparent);
            Assert.NotNull(middle);
            Assert.NotNull(leaf);
            Assert.True(middle!.ActualFont.Size > grandparent!.ActualFont.Size);
            Assert.Equal(middle.ActualFont.Size, leaf!.ActualFont.Size, 6);
        }

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
