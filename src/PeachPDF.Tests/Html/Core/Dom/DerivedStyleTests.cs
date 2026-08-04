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
        [InlineData(CssConstants.Inline, CssConstants.Block)]
        [InlineData(CssConstants.InlineBlock, CssConstants.Block)]
        [InlineData(CssConstants.InlineTable, CssConstants.Table)]
        [InlineData(CssConstants.TableRow, CssConstants.Block)]
        [InlineData(CssConstants.TableRowGroup, CssConstants.Block)]
        [InlineData(CssConstants.TableColumn, CssConstants.Block)]
        [InlineData(CssConstants.TableColumnGroup, CssConstants.Block)]
        [InlineData(CssConstants.TableCell, CssConstants.Block)]
        [InlineData(CssConstants.TableCaption, CssConstants.Block)]
        [InlineData(CssConstants.TableHeaderGroup, CssConstants.Block)]
        [InlineData(CssConstants.TableFooterGroup, CssConstants.Block)]
        [InlineData(CssConstants.InlineFlex, CssConstants.Flex)]
        [InlineData(CssConstants.InlineGrid, CssConstants.Grid)]
        public void ActualDisplay_OnAFloatedBox_BlockifiesInlineLevelAndTableInternalValues(string display, string expected)
        {
            var box = new CssBox(null, null) { Display = display, Float = CssProperty<Floating>.FromValue(CssConstants.Left, Floating.Left) };

            Assert.Equal(expected, box.DerivedStyle.ActualDisplay);
        }

        [Theory]
        [InlineData(CssConstants.Block)]
        [InlineData(CssConstants.Flex)]
        [InlineData(CssConstants.Grid)]
        [InlineData(CssConstants.Table)]
        [InlineData(CssConstants.None)]
        public void ActualDisplay_OnAFloatedBox_LeavesAlreadyBlockLevelValuesUnchanged(string display)
        {
            var box = new CssBox(null, null) { Display = display, Float = CssProperty<Floating>.FromValue(CssConstants.Left, Floating.Left) };

            Assert.Equal(display, box.DerivedStyle.ActualDisplay);
        }

        [Fact]
        public void ActualDisplay_OnAnUnfloatedBox_ReturnsTheRawCascadedKeywordUnchanged()
        {
            var box = new CssBox(null, null) { Display = CssConstants.Inline, Float = CssProperty<Floating>.FromValue(CssConstants.None, Floating.None) };

            Assert.Equal(CssConstants.Inline, box.DerivedStyle.ActualDisplay);
        }

        [Fact]
        public void Display_OnAFloatedBox_StaysTheRawCascadedKeyword_NotBlockified()
        {
            var box = new CssBox(null, null) { Display = CssConstants.Inline, Float = CssProperty<Floating>.FromValue(CssConstants.Right, Floating.Right) };

            Assert.Equal(CssConstants.Inline, box.Display);
            Assert.Equal(CssConstants.Block, box.DerivedStyle.ActualDisplay);
        }
    }
}
