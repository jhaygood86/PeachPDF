using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An author (or inline) <c>display</c> declaration on a table-tagged element must override the
    /// UA-stylesheet <c>display: table-*</c> per the CSS cascade
    /// (<see href="https://www.w3.org/TR/css-cascade-5/#cascade-origin">CSS Cascade 5 §6.3</see>) and
    /// <see href="https://www.w3.org/TR/css-display-3/">CSS Display 3</see>. PeachPDF previously blocked
    /// this via a deliberate gate (<c>DomParser.IsStyleOnElementAllowed</c>) that forced
    /// <c>table</c>/<c>tr</c>/<c>td</c>/<c>th</c>/<c>thead</c>/<c>tbody</c> back to their default table
    /// display. Removing that gate is what lets a CSS framework like Charts.css reset a semantic
    /// <c>&lt;table&gt;</c> to <c>display:block</c>/<c>flex</c>. Everything downstream is already driven by
    /// the computed <c>Display</c> value (anonymous-table fixup, layout-engine selection), so the override
    /// flows through correctly.
    /// </summary>
    public class TableDisplayOverrideTests
    {
        [Theory]
        [InlineData("block")]
        [InlineData("flex")]
        [InlineData("inline-block")]
        [InlineData("none")]
        public async Task InlineDisplay_OnTable_OverridesUaTableDisplay(string display)
        {
            var root = await BuildOnly($"""
                <!DOCTYPE html><html><head></head><body>
                <table class="t" style="display:{display}"></table>
                </body></html>
                """);

            Assert.Equal(display, FindByClass(root, "t")!.Display);
        }

        [Fact]
        public async Task InlineDisplayFlex_OnTableCell_OverridesTableCell()
        {
            var root = await BuildOnly("""
                <!DOCTYPE html><html><head></head><body>
                <table><tr><td class="c" style="display:flex"></td></tr></table>
                </body></html>
                """);

            Assert.Equal(CssConstants.Flex, FindByClass(root, "c")!.Display);
        }

        [Fact]
        public async Task StylesheetRule_ResetsWholeTableToBlock()
        {
            // The Charts.css reset shape: `table.x { display:block }` on the table plus a descendant
            // rule resetting every table-internal element. Each must beat its UA table-* default.
            var root = await BuildOnly("""
                <!DOCTYPE html><html><head><style>
                table.x { display: block }
                table.x thead, table.x tbody, table.x tr, table.x th, table.x td { display: block }
                </style></head><body>
                <table class="x">
                  <thead><tr><th class="h">H</th></tr></thead>
                  <tbody><tr class="r"><td class="d">D</td></tr></tbody>
                </table>
                </body></html>
                """);

            Assert.Equal(CssConstants.Block, FindByClass(root, "x")!.Display);
            Assert.Equal(CssConstants.Block, FindByClass(root, "r")!.Display);
            Assert.Equal(CssConstants.Block, FindByClass(root, "d")!.Display);
            Assert.Equal(CssConstants.Block, FindByClass(root, "h")!.Display);
        }

        [Fact]
        public async Task TableWithoutOverride_StillDefaultsToTableDisplay()
        {
            // Guard: with no author override, the UA default still wins (the fix removes a gate, it does
            // not change UA defaults).
            var root = await BuildOnly("""
                <!DOCTYPE html><html><head></head><body>
                <table class="t"><tr class="r"><td class="d">D</td></tr></table>
                </body></html>
                """);

            Assert.Equal(CssConstants.Table, FindByClass(root, "t")!.Display);
            Assert.Equal(CssConstants.TableRow, FindByClass(root, "r")!.Display);
            Assert.Equal(CssConstants.TableCell, FindByClass(root, "d")!.Display);
        }

        // --- Partial overrides: only a subset of the table's elements is reset (the common, natural case,
        // and the one that regressed). A proper table child left under a now-non-table parent must still be
        // wrapped in an anonymous table box (CSS2.1 §17.2.1 rule 3.2) so its content survives layout. These
        // drive real layout (not just the parsed Display value) so content loss cannot hide. ---

        [Theory]
        [InlineData("""<table style="display:block"><tr><td class="d">CELL</td></tr></table>""")]
        [InlineData("""<table><tr style="display:block"><td class="d">CELL</td></tr></table>""")]
        [InlineData("""<table style="display:inline"><tr><td class="d">CELL</td></tr></table>""")]
        public async Task PartialOverride_KeepsDescendantContent(string tableMarkup)
        {
            var (root, _) = await BuildAndLayout($"<!DOCTYPE html><html><body>{tableMarkup}</body></html>");

            var words = CollectWords(root);
            Assert.Contains("CELL", words);

            // The proper table child under the non-table parent must have been re-wrapped in a synthesized
            // (tag-less) anonymous table box so the table formatting context — and the content — is preserved.
            Assert.Contains(EnumerateBoxes(root),
                b => b.HtmlTag is null && b.Display is CssConstants.Table or CssConstants.InlineTable);
        }

        [Fact]
        public async Task DisplayFlexTable_LaysChildrenOutAsAFlexContainer()
        {
            // A table reset to display:flex with two block children must lay them out side by side (distinct,
            // increasing X), i.e. behave as a real flex container rather than a table.
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><head><style>
                .row { display: flex }
                .row > div { width: 40px; height: 20px }
                </style></head><body>
                <table class="row"><div class="a">A</div><div class="b">B</div></table>
                </body></html>
                """);

            var a = FindByClass(root, "a")!;
            var b = FindByClass(root, "b")!;

            Assert.Equal(CssConstants.Flex, FindByClass(root, "row")!.Display);
            Assert.True(b.Location.X > a.Location.X,
                $"flex items should lay out left-to-right, but a.X={a.Location.X} b.X={b.Location.X}");
            Assert.Equal(a.Location.Y, b.Location.Y, 3);
        }

        private static IEnumerable<CssBox> EnumerateBoxes(CssBox box)
        {
            yield return box;
            foreach (var child in box.Boxes)
                foreach (var descendant in EnumerateBoxes(child))
                    yield return descendant;
        }

        private static List<string?> CollectWords(CssBox box) =>
            EnumerateBoxes(box).SelectMany(b => b.Words.Select(w => w.Text)).ToList();

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static async Task<CssBox> BuildOnly(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindByClass(CssBox box, string className)
        {
            var val = box.HtmlTag?.TryGetAttribute("class", "");
            if (val != null && val.Split(' ').Contains(className))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindByClass(child, className);
                if (found != null) return found;
            }

            return null;
        }
    }
}
