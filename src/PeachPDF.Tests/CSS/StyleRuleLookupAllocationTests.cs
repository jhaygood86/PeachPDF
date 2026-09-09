using System;
using System.Linq;
using PeachPDF.CSS;
using Xunit;

namespace PeachPDF.Tests.CSS
{
    /// <summary>
    /// Reading a rule's selector or declaration block allocates nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were <c>Children.OfType&lt;T&gt;().FirstOrDefault()</c>. <c>Children</c> hands the child
    /// list back as <see cref="System.Collections.Generic.IEnumerable{T}"/>, so that query allocates
    /// an <c>OfType</c> iterator and boxes a list enumerator — on <b>every read</b>, of a property.
    /// </para>
    /// <para>
    /// It matters because of how often it is read: selector matching asks each candidate rule for its
    /// selector once per box, so a document with many rules and many boxes pays this per pair. Over a
    /// 26-document corpus it was 39 MB, 5% of everything the engine allocated.
    /// </para>
    /// <para>
    /// Asserted on the property rather than through a rendered document because allocation over a
    /// document scales with whatever font a machine resolves; here the getters are the only thing
    /// running, which makes the assertion exact and the same everywhere.
    /// </para>
    /// </remarks>
    public class StyleRuleLookupAllocationTests
    {
        [Fact]
        public void ReadingSelectorAndStyle_AllocatesNothing()
        {
            var sheet = new StylesheetParser().Parse(".a .b > .c, #d { color: red; margin: 1px }");
            var rule = sheet.Rules.OfType<StyleRule>().Single();

            // Warm: JIT both getters before anything is counted.
            for (var i = 0; i < 3; i++)
            {
                _ = rule.Selector;
                _ = rule.Style;
            }

            // Per THREAD, not process-wide: this suite runs collections in parallel, so
            // GC.GetTotalAllocatedBytes would count whatever every other test is allocating.
            const int reads = 2000;
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < reads; i++)
            {
                _ = rule.Selector;
                _ = rule.Style;
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(allocated < 4096,
                $"{reads * 2:N0} property reads allocated {allocated:N0} bytes. They should allocate "
                + "nothing: the child list is exposed as IEnumerable, so a LINQ lookup over it builds "
                + "an iterator and boxes an enumerator every time.");
        }

        /// <summary>
        /// The indexed walk still finds the same children the LINQ form did — including after the
        /// selector has been replaced, which is the one case where the child is not the one the
        /// constructor put there.
        /// </summary>
        [Fact]
        public void TheLookupsStillFindTheRightChildren()
        {
            var sheet = new StylesheetParser().Parse(".a { color: red }");
            var rule = sheet.Rules.OfType<StyleRule>().Single();

            Assert.NotNull(rule.Selector);
            Assert.Equal(".a", rule.Selector.Text);
            Assert.NotNull(rule.Style);
            Assert.Equal("rgb(255, 0, 0)", rule.Style.GetPropertyValue("color"));

            rule.SelectorText = ".b .c";

            Assert.Equal(".b .c", rule.Selector.Text);
            Assert.Equal(".b .c", rule.SelectorText);
            Assert.NotNull(rule.Style);
            Assert.Equal("rgb(255, 0, 0)", rule.Style.GetPropertyValue("color"));
        }

        /// <summary>
        /// With its child gone, each lookup returns null rather than walking into the other's child —
        /// the same answer <c>OfType&lt;T&gt;().FirstOrDefault()</c> gave. A rule carries exactly one
        /// selector and one declaration block, so an indexed walk that matched on the wrong type would
        /// find the sibling instead of stopping.
        /// </summary>
        [Fact]
        public void EachLookupIsNullOnceItsOwnChildIsGone()
        {
            var rule = new StyleRule(new StylesheetParser());

            Assert.NotNull(rule.Selector);
            Assert.NotNull(rule.Style);

            rule.RemoveChild(rule.Style);

            Assert.Null(rule.Style);
            Assert.NotNull(rule.Selector);

            rule.RemoveChild(rule.Selector);

            Assert.Null(rule.Selector);
            Assert.Equal(string.Empty, rule.SelectorText);
        }
    }
}
