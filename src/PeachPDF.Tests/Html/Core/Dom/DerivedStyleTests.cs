using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Coverage for <see cref="DerivedStyle.ActualDisplay"/> - the CSS 2.1 §9.7 blockification a floated
    /// box's <c>display</c> undergoes, moved here from <see cref="CssBox"/>'s <c>Display</c> getter so that
    /// property stays a plain, mechanical read of the raw cascaded keyword. These tests exist because the
    /// move itself doesn't change behavior (every prior reader of <c>Display</c> got the blockified value;
    /// they now read <c>ActualDisplay</c> instead), so the blockification mapping needs its own direct
    /// coverage independent of whichever layout/paint call site happens to exercise it.
    /// </summary>
    public class DerivedStyleTests
    {
        [Theory]
        [InlineData(Keywords.Inline, Keywords.Block)]
        [InlineData(Keywords.InlineBlock, Keywords.Block)]
        [InlineData(Keywords.InlineTable, Keywords.Table)]
        [InlineData(Keywords.TableRow, Keywords.Block)]
        [InlineData(Keywords.TableRowGroup, Keywords.Block)]
        [InlineData(Keywords.TableColumn, Keywords.Block)]
        [InlineData(Keywords.TableColumnGroup, Keywords.Block)]
        [InlineData(Keywords.TableCell, Keywords.Block)]
        [InlineData(Keywords.TableCaption, Keywords.Block)]
        [InlineData(Keywords.TableHeaderGroup, Keywords.Block)]
        [InlineData(Keywords.TableFooterGroup, Keywords.Block)]
        [InlineData(Keywords.InlineFlex, Keywords.Flex)]
        [InlineData(Keywords.InlineGrid, Keywords.Grid)]
        public void ActualDisplay_OnAFloatedBox_BlockifiesInlineLevelAndTableInternalValues(string display, string expected)
        {
            var box = new CssBox(null, null)
            {
                Display = CssProperty<DisplayMode>.FromCssText(display, Map.DisplayModes, DisplayMode.Inline),
                Float = CssProperty<Floating>.FromValue(Keywords.Left, Floating.Left)
            };

            Assert.Equal(expected, box.DerivedStyle.ActualDisplay);
        }

        [Theory]
        [InlineData(Keywords.Block)]
        [InlineData(Keywords.Flex)]
        [InlineData(Keywords.Grid)]
        [InlineData(Keywords.Table)]
        [InlineData(Keywords.None)]
        public void ActualDisplay_OnAFloatedBox_LeavesAlreadyBlockLevelValuesUnchanged(string display)
        {
            var box = new CssBox(null, null)
            {
                Display = CssProperty<DisplayMode>.FromCssText(display, Map.DisplayModes, DisplayMode.Inline),
                Float = CssProperty<Floating>.FromValue(Keywords.Left, Floating.Left)
            };

            Assert.Equal(display, box.DerivedStyle.ActualDisplay);
        }

        [Fact]
        public void ActualDisplay_OnAnUnfloatedBox_ReturnsTheRawCascadedKeywordUnchanged()
        {
            var box = new CssBox(null, null)
            {
                Display = CssProperty<DisplayMode>.FromValue(Keywords.Inline, DisplayMode.Inline),
                Float = CssProperty<Floating>.FromValue(Keywords.None, Floating.None)
            };

            Assert.Equal(Keywords.Inline, box.DerivedStyle.ActualDisplay);
        }

        [Fact]
        public void Display_OnAFloatedBox_StaysTheRawCascadedKeyword_NotBlockified()
        {
            var box = new CssBox(null, null)
            {
                Display = CssProperty<DisplayMode>.FromValue(Keywords.Inline, DisplayMode.Inline),
                Float = CssProperty<Floating>.FromValue(Keywords.Right, Floating.Right)
            };

            Assert.Equal(DisplayMode.Inline, box.Display.Value);
            Assert.Equal(Keywords.Block, box.DerivedStyle.ActualDisplay);
        }
    }
}
