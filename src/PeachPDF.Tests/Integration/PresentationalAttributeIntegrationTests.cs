using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Deprecated presentational HTML attributes (<c>align</c>, <c>nowrap</c>) that <c>DomParser</c>
    /// translates directly into typed <c>CssProperty</c> values rather than going through the CSS
    /// cascade. See <see cref="BorderStylePaintIntegrationTests"/> for the sibling <c>border</c>
    /// attribute coverage.
    /// </summary>
    public class PresentationalAttributeIntegrationTests
    {
        [Fact]
        public async Task ImgAlignLeft_SetsFloatLeft()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='left' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(Floating.Left, img.Float.Value);
        }

        [Fact]
        public async Task ImgAlignRight_SetsFloatRight()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='right' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(Floating.Right, img.Float.Value);
        }

        [Fact]
        public async Task AlignAttributeLeft_OnNonImgElement_SetsTextAlignLeft()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='left'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Left, div.TextAlign.Value);
        }

        [Fact]
        public async Task AlignAttributeCenter_OnNonImgElement_SetsTextAlignCenter()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='center'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Center, div.TextAlign.Value);
        }

        [Fact]
        public async Task AlignAttributeRight_OnNonImgElement_SetsTextAlignRight()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='right'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Right, div.TextAlign.Value);
        }

        [Fact]
        public async Task AlignAttributeJustify_OnNonImgElement_SetsTextAlignJustify()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='justify'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Justify, div.TextAlign.Value);
        }

        [Fact]
        public async Task NowrapAttribute_SetsWhiteSpaceNoWrap()
        {
            var (root, _) = await BuildAndLayout(Wrap("<td id='d' nowrap='nowrap'>x</td>"));
            var td = FindById(root, "d")!;

            Assert.Equal(Whitespace.NoWrap, td.WhiteSpace.Value);
        }

        [Fact]
        public async Task ImgAlignLeft_SetsVerticalAlignTop()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='left' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(VerticalAlignment.Top, img.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task ImgAlignTop_SetsVerticalAlignTop()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='top' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(VerticalAlignment.Top, img.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task ImgAlignBottom_SetsVerticalAlignBaseline()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='bottom' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(VerticalAlignment.Baseline, img.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task ImgAlignMiddle_SetsVerticalAlignToThePeachBaselineMiddleSentinel()
        {
            // -peachpdf-baseline-middle (same idea as -webkit-baseline-middle) is a PeachPDF-internal
            // sentinel, never produced by parsing authored CSS text - see
            // Keywords.PeachBaselineMiddle and VerticalAlignment.PeachBaselineMiddle. It has no
            // distinct inline-layout effect (CssLayoutEngine.ApplyVerticalAlignment's switch falls
            // through to the baseline default), matching this attribute's pre-existing behavior.
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' align='middle' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal(VerticalAlignment.PeachBaselineMiddle, img.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task ValignAttribute_OnATableCell_SetsVerticalAlign()
        {
            var (root, _) = await BuildAndLayout(Wrap("<table><tr><td id='d' valign='top'>x</td></tr></table>"));
            var td = FindById(root, "d")!;

            Assert.Equal(VerticalAlignment.Top, td.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task AlignAttribute_OnNonImgElement_WithAVerticalAlignKeyword_SetsVerticalAlign()
        {
            // The generic `align` attribute on a non-img element only maps to text-align for its four
            // horizontal keywords (left/center/right/justify) - anything else (e.g. a vertical-align
            // keyword, historically seen in the wild on table cells) falls through to the same
            // unvalidated-string-turned-typed path `valign` uses.
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='middle'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(VerticalAlignment.Middle, div.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task ValignAttribute_WithAnUnrecognizedValue_LeavesTheCascadedValueInPlace()
        {
            // Regression for issue #642: an unrecognized valign value used to force baseline
            // unconditionally (CssProperty<T>.FromCssText's own fallback), overwriting whatever the CSS
            // cascade had already produced - here, the UA default stylesheet's `td, th { vertical-align:
            // inherit }` from a `middle`-declaring <tr> (CssDefaults.cs).
            var (root, _) = await BuildAndLayout(Wrap("<table><tr><td id='d' valign='bogus'>x</td></tr></table>"));
            var td = FindById(root, "d")!;

            Assert.Equal(VerticalAlignment.Middle, td.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task ValignAttribute_WithAnUnrecognizedValue_DoesNotOverrideAnAuthoredStylesheetRule()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>#d { vertical-align: top; }</style>" +
                "<table><tr><td id='d' valign='bogus'>x</td></tr></table>"));
            var td = FindById(root, "d")!;

            Assert.Equal(VerticalAlignment.Top, td.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public async Task AlignAttribute_Uppercase_StillMapsToTextAlign()
        {
            // Legacy markup commonly authors align="LEFT"/"CENTER" etc. - a case-sensitive comparison
            // used to miss these and fall into the vertical-align branch instead (issue #642).
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' align='LEFT'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal(HorizontalAlignment.Left, div.TextAlign.Value);
        }

        [Fact]
        public async Task BackgroundAttribute_SetsBackgroundImage()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' background='x.png'>x</div>"));
            var div = FindById(root, "d")!;

            var layer = Assert.Single(div.BackgroundImages!);
            var urlImage = Assert.IsType<CssImage.Url>(layer);
            Assert.Equal("x.png", urlImage.Href);
        }

        [Fact]
        public async Task BgcolorAttribute_SetsBackgroundColor()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' bgcolor='red'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal("red", div.BackgroundColor);
        }

        [Fact]
        public async Task BorderAttribute_OnTable_SetsSolidBorderOnTheTableItself()
        {
            // Exercises TranslateBorder's tag.Name == "table" branch, which (attempts to) cascade a 1px
            // solid border onto every cell via ApplyTableBorder/SetForAllCells - which has the same
            // pre-existing #636 gap as CellpaddingAttribute_DoesNotYetReachTableCells above and
            // PresentationalBorderAttribute_OnAPlainElement_ForcesSolidOnAllSides's comment in
            // BorderStylePaintIntegrationTests, so only the table's own border is asserted here.
            var (root, _) = await BuildAndLayout(Wrap("<table id='t' border='1'><tr><td>x</td></tr></table>"));
            var table = FindById(root, "t")!;

            Assert.Equal(LineStyle.Solid, table.BorderTopStyle.Value);
            Assert.Equal("1px", table.BorderTopWidth);
        }

        [Fact]
        public async Task BordercolorAttribute_SetsAllBorderColors()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' bordercolor='red'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal("red", div.BorderLeftColor);
            Assert.Equal("red", div.BorderTopColor);
            Assert.Equal("red", div.BorderRightColor);
            Assert.Equal("red", div.BorderBottomColor);
        }

        [Fact]
        public async Task CellspacingAttribute_SetsBorderSpacing()
        {
            var (root, _) = await BuildAndLayout(Wrap("<table id='t' cellspacing='5'><tr><td>x</td></tr></table>"));
            var table = FindById(root, "t")!;

            Assert.Equal("5px", table.BorderSpacing);
        }

        [Fact]
        public async Task CellpaddingAttribute_DoesNotYetReachTableCells()
        {
            // ApplyTablePadding's TD-cascading path (SetForAllCells) has the same pre-existing gap as
            // ApplyTableBorder's (see PresentationalBorderAttribute_OnAPlainElement_ForcesSolidOnAllSides's
            // comment in BorderStylePaintIntegrationTests): it doesn't traverse the anonymous
            // table-row-group box CSS inserts around a bare <tr>, so cellpadding never actually reaches
            // the cell today - tracked separately as issue #636, out of scope here. This documents the
            // current (pre-existing) behavior rather than silently leaving the `cellpadding` switch-case
            // uncovered.
            var (root, _) = await BuildAndLayout(Wrap("<table id='t' cellpadding='5'><tr><td id='c'>x</td></tr></table>"));
            var cell = FindById(root, "c")!;

            Assert.Equal("0", cell.PaddingLeft);
        }

        [Fact]
        public async Task ColorAttribute_SetsColor()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' color='blue'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal("blue", div.Color);
        }

        [Fact]
        public async Task HeightAttribute_SetsHeight()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' height='50'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal("50px", div.Height);
        }

        [Fact]
        public async Task HspaceAttribute_SetsLeftAndRightMargin()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' hspace='10' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal("10px", img.MarginLeft.ToString());
            Assert.Equal("10px", img.MarginRight.ToString());
        }

        [Fact]
        public async Task VspaceAttribute_SetsTopAndBottomMargin()
        {
            var (root, _) = await BuildAndLayout(Wrap("<img id='i' vspace='10' src='x.png' width='10' height='10'>"));
            var img = FindById(root, "i")!;

            Assert.Equal("10px", img.MarginTop.ToString());
            Assert.Equal("10px", img.MarginBottom.ToString());
        }

        [Fact]
        public async Task WidthAttribute_SetsWidth()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='d' width='50'>x</div>"));
            var div = FindById(root, "d")!;

            Assert.Equal("50px", div.Width);
        }

        [Fact]
        public async Task SizeAttribute_OnHr_SetsHeight()
        {
            var (root, _) = await BuildAndLayout(Wrap("<hr id='h' size='3'>"));
            var hr = FindById(root, "h")!;

            Assert.Equal("3px", hr.Height);
        }

        [Fact]
        public async Task SizeAttribute_OnFont_WithValidValue_SetsFontSize()
        {
            var (root, _) = await BuildAndLayout(Wrap("<font id='f' size='large'>x</font>"));
            var font = FindById(root, "f")!;

            Assert.Equal("large", font.FontSize.ToString());
        }

        [Fact]
        public async Task SizeAttribute_OnFont_WithInvalidValue_LeavesFontSizeUnchanged()
        {
            // A bare legacy HTML size scale value ("+2", or "1"-"7") matches neither a CSS font-size
            // keyword nor a valid length, so it must be left alone rather than forced to "medium" -
            // see the comment on TranslateFontSize (issue #642's accepted-gap convention).
            var (root, _) = await BuildAndLayout(Wrap("<font id='f' size='+2'>x</font>"));
            var font = FindById(root, "f")!;

            Assert.Equal("medium", font.FontSize.ToString());
        }

        [Fact]
        public async Task FaceAttribute_WithResolvableFont_SetsFontFamily()
        {
            // Deliberately NOT DefaultFontResolver.DefaultFont: DerivedStyle lazily resolves a null/empty
            // FontFamily to the default font once layout runs (see the "unresolvable" test below), so
            // asserting the default font here wouldn't distinguish "GetFontFamilyByName actually
            // resolved this" from "resolution was skipped entirely and the fallback papered over it".
            // Picking an installed family that ISN'T the platform default keeps this test meaningful
            // across the CI matrix (Windows/macOS/Linux each resolve a different default).
            var adapter = new PdfSharpAdapter();
            var alternativeFont = DefaultFontResolver.GetInstalledFontFamilyNames()
                .First(f => !f.Equals(DefaultFontResolver.DefaultFont, StringComparison.OrdinalIgnoreCase) && adapter.IsFontExists(f));

            var (root, _) = await BuildAndLayout(Wrap($"<font id='f' face='{alternativeFont}'>x</font>"));
            var font = FindById(root, "f")!;

            Assert.Equal(alternativeFont, font.FontFamily);
            Assert.Equal(alternativeFont, font.FontFamilyList);
        }

        [Fact]
        public async Task FaceAttribute_WithUnresolvableFont_FallsBackToDefaultFontButKeepsRawList()
        {
            // GetFontFamilyByName returns null when no candidate resolves (see
            // CssValueParserFontFamilyTests), but DerivedStyle lazily resolves a null/empty FontFamily to
            // DefaultFontResolver.DefaultFont once layout runs (the same fallback CSS's own unresolvable
            // font-family gets) - FontFamilyList is untouched by that fallback, so it still holds the raw
            // attribute text.
            var (root, _) = await BuildAndLayout(Wrap("<font id='f' face='__DefinitelyNotARealFontFamily__'>x</font>"));
            var font = FindById(root, "f")!;

            Assert.Equal(DefaultFontResolver.DefaultFont, font.FontFamily);
            Assert.Equal("__DefinitelyNotARealFontFamily__", font.FontFamilyList);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
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

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
