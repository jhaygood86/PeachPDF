using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The break-value classifier. These assert against the css-break-3 §3.1/§3.2 value sets directly
    /// rather than against whatever the layout sites happen to compare, since the whole point of the
    /// class is that every site asks the same question the same way.
    /// </summary>
    public class BreakValuesTests
    {
        // §3.1's complete break-before/break-after value set. `page` forces a page break outright and
        // the four directional values force one or two of them; `column`/`region` force a break in a
        // fragmentation context a page break is not a substitute for, and the avoid family forces none.
        [Theory]
        [InlineData("page", true)]
        [InlineData("left", true)]
        [InlineData("right", true)]
        [InlineData("recto", true)]
        [InlineData("verso", true)]
        [InlineData("auto", false)]
        [InlineData("avoid", false)]
        [InlineData("avoid-page", false)]
        [InlineData("avoid-column", false)]
        [InlineData("avoid-region", false)]
        [InlineData("column", false)]
        [InlineData("region", false)]
        [InlineData(null, false)]
        public void IsForcedPageBreak_MatchesTheSpecValueSet(string? value, bool expected) =>
            Assert.Equal(expected, BreakValues.IsForcedPageBreak(value));

        // "always" only ever reaches a box through the legacy page-break-* alias, which CssUtils
        // rewrites to "page" - so the classifier must not accept it in its own right.
        [Fact]
        public void IsForcedPageBreak_RejectsTheLegacyAlwaysSpelling() =>
            Assert.False(BreakValues.IsForcedPageBreak("always"));

        // PageSide is internal, so the theory rows name it and this maps them back.
        private static PageSide Side(string name) => name switch
        {
            "left" => PageSide.Left,
            "right" => PageSide.Right,
            _ => PageSide.Any
        };

        [Theory]
        [InlineData("left", null, "left")]
        [InlineData("verso", null, "left")]
        [InlineData("right", null, "right")]
        [InlineData("recto", null, "right")]
        [InlineData(null, "left", "left")]
        [InlineData(null, "recto", "right")]
        [InlineData("auto", "verso", "left")]
        [InlineData("page", "page", "any")]
        [InlineData("auto", "avoid", "any")]
        [InlineData(null, null, "any")]
        public void RequiredSide_ReadsEitherSideOfTheBreakPoint(string? before, string? after, string expected) =>
            Assert.Equal(Side(expected), BreakValues.RequiredSide(before, after));

        // A directional value beats a plain `page` on the other side of the pair: honoring the
        // directional one satisfies both requirements, honoring `page` alone would not.
        [Theory]
        [InlineData("page", "right", "right")]
        [InlineData("verso", "page", "left")]
        public void RequiredSide_DirectionalBeatsAPlainPageOnTheOtherSide(string? before, string? after, string expected) =>
            Assert.Equal(Side(expected), BreakValues.RequiredSide(before, after));

        // Conflicting directional values are unsatisfiable; the later box's own break-before wins.
        [Fact]
        public void RequiredSide_ConflictingDirectionalValues_TakeTheBreakBefore() =>
            Assert.Equal(PageSide.Right, BreakValues.RequiredSide("right", "left"));

        [Theory]
        [InlineData("avoid", true)]
        [InlineData("avoid-page", true)]
        [InlineData("avoid-column", false)]
        [InlineData("avoid-region", false)]
        [InlineData("auto", false)]
        [InlineData("page", false)]
        [InlineData("column", false)]
        [InlineData("region", false)]
        [InlineData(null, false)]
        public void AvoidsPageBreak_CoversOnlyThePageContext(string? value, bool expected) =>
            Assert.Equal(expected, BreakValues.AvoidsPageBreak(value));

        // Slot k is page k+1, and the first page is a right page.
        [Theory]
        [InlineData(0, "right", true)]
        [InlineData(0, "left", false)]
        [InlineData(1, "left", true)]
        [InlineData(1, "right", false)]
        [InlineData(2, "right", true)]
        [InlineData(0, "any", true)]
        [InlineData(1, "any", true)]
        public void SlotIsOn_AlternatesFromARightFirstPage(int slotIndex, string side, bool expected) =>
            Assert.Equal(expected, BreakValues.SlotIsOn(slotIndex, Side(side)));

        // The parity itself, stated independently of the helper it is implemented over: page 1 is a
        // right page and the sides alternate from there, which is what `@page :left`/`:right` mean and
        // therefore what a directional break has to target.
        [Theory]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, false)]
        [InlineData(11, true)]
        public void IsRightPage_AlternatesFromARightFirstPage(int pageNumber, bool expected) =>
            Assert.Equal(expected, PageRuleResolver.IsRightPage(pageNumber));
    }
}
