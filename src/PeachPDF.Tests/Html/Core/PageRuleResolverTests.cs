using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using System.Collections.Generic;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Tests for <see cref="PageRuleResolver"/>'s two named-page attribution policies. The cascade
    /// itself (rule matching/specificity/ordering) is covered exhaustively by
    /// <c>PdfGeneratorSelectPageRuleTests</c> through the delegating shims; these pin the policy
    /// split introduced when geometry moved to layout time: paint-time selection keeps the
    /// "active by page END" semantics, while the geometry table uses "active at slot START" so a
    /// slot's band height can never depend on registrations inside the slot itself.
    /// </summary>
    public class PageRuleResolverTests
    {
        [Fact]
        public void ActiveNameAtPageEnd_ElementRegisteringMidPage_NamesTheWholePage()
        {
            var elements = new List<NamedPageElement> { new("chapter", 400) };

            // Page [0, 800): the element's Y falls inside, so the page-end policy adopts it.
            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 0, pageHeight: 800));
        }

        [Fact]
        public void ActiveNameAtSlotStart_ElementRegisteringMidSlot_DoesNotNameTheSlot()
        {
            var elements = new List<NamedPageElement> { new("chapter", 400) };

            // Slot starting at 0: the element registered at 400, after the slot start - the
            // slot-start policy must NOT see it (its band was already fixed when layout crossed 0).
            Assert.Null(PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 0));
        }

        [Fact]
        public void ActiveNameAtSlotStart_ElementFlushAtSlotTop_NamesTheSlot()
        {
            // A name change forces a break, so the named element lands exactly at a slot top - the
            // epsilon makes that flush registration count as this slot's name.
            var elements = new List<NamedPageElement> { new("chapter", 800) };

            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 800));
            Assert.Null(PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 799));
        }

        [Fact]
        public void BothPolicies_TakeTheHighestApplicableY()
        {
            var elements = new List<NamedPageElement> { new("one", 100), new("two", 500) };

            Assert.Equal("two", PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 0, pageHeight: 800));
            Assert.Equal("two", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 600));
            Assert.Equal("one", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 300));
        }

        [Fact]
        public void ReversionEntry_ShadowsEarlierNamedPage_ForLaterSlots()
        {
            // The used value of `page` reverts to the default when content leaves a named page's
            // subtree - that reversion is registered as an empty-name entry (issue #126). Both policies
            // must adopt it for slots/pages at or after it, so a named page's margins/margin boxes stop
            // applying once content reverts, instead of leaking forward indefinitely.
            var elements = new List<NamedPageElement> { new("chapter", 800), new(string.Empty, 1600) };

            // Before the reversion: still "chapter".
            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 800));
            Assert.Equal("chapter", PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 800, pageHeight: 800));

            // At/after the reversion Y: reverted to the empty (default) name, NOT "chapter".
            Assert.Equal(string.Empty, PageRuleResolver.ActiveNameAtSlotStart(elements, slotTop: 1600));
            Assert.Equal(string.Empty, PageRuleResolver.ActiveNameAtPageEnd(elements, pageY: 1600, pageHeight: 800));
        }
    }
}
