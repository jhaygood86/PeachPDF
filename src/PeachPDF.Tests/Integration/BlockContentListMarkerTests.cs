using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A list item's <c>outside</c> <c>::marker</c> sits beside its principal block box whether the item's
    /// content is inline or block-level (CSS 2.1 §12.5.1 / CSS Lists Level 3 §3). Re-parented into the
    /// anonymous block the parser builds for an item's inline run, it became a <i>grand</i>child of the item
    /// and the one call that positions it — <c>CssBox.LayoutOutsideMarker</c>, which scans <i>direct</i>
    /// children — never found it: <c>&lt;li&gt;&lt;p&gt;…&lt;/p&gt;&lt;/li&gt;</c> ended layout with its marker
    /// at <c>(0, 0)</c>, claimed by no fragment and drawn on no page (issue #467).
    /// </summary>
    public class BlockContentListMarkerTests
    {
        private const string BlockItemList =
            "<ol style='margin:0;padding-left:40pt'>"
            + "<li id='block'><p style='margin:0'>block content here</p></li>"
            + "<li id='inline'>inline content</li></ol>";

        /// <summary>
        /// The visible symptom: a lost marker is not a mispositioned number, it is a number that is never
        /// drawn at all. Before the fix the only marker string this document painted was <c>2.</c>.
        /// </summary>
        [Fact]
        public async Task AnItemWhoseContentIsBlockLevel_DrawsItsMarker()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(BlockItemList));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var drawn = g.DrawStringCalls.Select(c => c.Text).ToList();

            Assert.Equal(1, drawn.Count(t => t == "1."));
            Assert.Equal(1, drawn.Count(t => t == "2."));
        }

        /// <summary>
        /// Taking the marker out of the anonymous block leaves it a direct child of the item, and it is
        /// <c>Boxes[0]</c> — so it is what the item's first real child resolves its own top against unless
        /// the in-flow sibling walk steps over it (<c>DomUtils.GetPreviousSibling</c>). It does not carry a
        /// usable one: a marker is positioned <i>after</i> the item's children, so its
        /// <c>ActualBottom</c> is still 0 when they ask. Left in, the block child was placed at the top of
        /// the document and the item laid out with no height at all, so the next item drew straight over it.
        /// </summary>
        [Fact]
        public async Task AnItemWhoseContentIsBlockLevel_LaysThatContentOutBelowTheItemsTop()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(BlockItemList));

            var block = LayoutHarness.FindById(root, "block")!;
            var inline = LayoutHarness.FindById(root, "inline")!;
            var paragraph = block.Boxes.Single(b => b.HtmlTag?.Name == "p");

            Assert.Equal(block.ClientTop, paragraph.Location.Y, 3);
            Assert.True(block.ActualBottom > block.Location.Y,
                "the block-content item must have a height of its own");
            Assert.True(inline.Location.Y >= block.ActualBottom - 0.001,
                "the next item must start below the block-content item, not on top of it");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var drawn = g.DrawStringCalls.Select(c => c.Text).ToList();

            Assert.Contains("block", drawn);
            Assert.Contains("inline", drawn);
        }

        /// <summary>
        /// The same statement made of the fragment tree rather than of paint, since a marker that no
        /// fragment claims is the state paint is reading when it draws nothing.
        /// </summary>
        [Fact]
        public async Task AnItemWhoseContentIsBlockLevel_HasItsMarkerClaimedExactlyOnce()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(BlockItemList));

            var claims = ClaimsByWord(container);

            foreach (var item in ListItems(root))
            {
                var word = Assert.Single(MarkerOf(item).Words);

                Assert.True(claims.TryGetValue(word, out var slots),
                    $"the marker of '{Id(item)}' is claimed by no fragment at all");
                Assert.Single(slots!);
            }
        }

        /// <summary>
        /// Why it was lost, stated directly: the marker is a <b>direct</b> child of its item, not a child of
        /// the anonymous block the parser builds for an inline run. Every scan that finds a marker — the one
        /// that positions it and the one that paints it (<c>FragmentPainter.FindMarkerFragment</c>) — looks
        /// only among direct children, so this is the structural fact the rest of this file rests on.
        /// </summary>
        [Fact]
        public async Task AnOutsideMarker_IsNotWrappedInTheItemsAnonymousBlock()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(BlockItemList));

            foreach (var item in ListItems(root))
            {
                Assert.Single(item.Boxes, b => b.IsMarkerPseudoElement);
            }
        }

        /// <summary>
        /// The marker lands where an inline-content item's marker lands: outside the item's content edge,
        /// beside its first line. Asserted as one shared offset across both items, which is the statement
        /// that the block-content item is not being treated as a different kind of thing.
        /// </summary>
        [Fact]
        public async Task AnItemWhoseContentIsBlockLevel_PositionsItsMarkerLikeAnInlineOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(BlockItemList));

            var offsets = new List<double>();

            foreach (var item in ListItems(root))
            {
                var word = Assert.Single(MarkerOf(item).Words);

                Assert.InRange(word.Top, item.Location.Y, item.Location.Y + item.ActualLineHeight);
                Assert.True(word.Right <= item.ClientLeft + 0.001,
                    $"the marker of '{Id(item)}' overlaps its item's content edge");

                offsets.Add(item.ClientLeft - word.Left);
            }

            Assert.Equal(2, offsets.Count);
            Assert.Single(offsets.Select(o => Math.Round(o, 3)).Distinct());
        }

        /// <summary>
        /// An item mixing inline and block content still gets the anonymous block its inline run needs — the
        /// marker is taken out of that run, not the run out of the box tree. Both items are numbered.
        /// </summary>
        [Fact]
        public async Task AnItemMixingInlineAndBlockContent_KeepsItsAnonymousBlockAndItsMarker()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='margin:0;padding-left:40pt'>"
                + "<li id='mixed'>leading text<p style='margin:0'>and a block</p></li>"
                + "<li id='plain'>plain</li></ol>"));

            var mixed = LayoutHarness.FindById(root, "mixed")!;

            // The marker, then the anonymous block holding "leading text", then the <p>.
            Assert.Equal(3, mixed.Boxes.Count);
            Assert.True(mixed.Boxes[0].IsMarkerPseudoElement);
            Assert.Null(mixed.Boxes[1].HtmlTag);
            Assert.Contains("leading", mixed.Boxes[1].Boxes.SelectMany(b => b.Words).Select(w => w.Text));
            Assert.Equal("p", mixed.Boxes[2].HtmlTag?.Name);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var drawn = g.DrawStringCalls.Select(c => c.Text).ToList();

            Assert.Equal(1, drawn.Count(t => t == "1."));
            Assert.Equal(1, drawn.Count(t => t == "2."));
        }

        /// <summary>
        /// The narrowing is deliberate: only an <c>outside</c> marker is kept out of the anonymous block. An
        /// <c>inside</c> marker is an ordinary flowed inline, and browsers give it its own anonymous block
        /// above a block-level child rather than putting it on that child's first line — so it stays wrapped.
        /// </summary>
        [Fact]
        public async Task AnInsideMarker_IsStillWrappedWithTheItemsInlineRun()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='margin:0;padding-left:40pt;list-style-position:inside'>"
                + "<li id='block'><p style='margin:0'>block content here</p></li></ol>"));

            var item = LayoutHarness.FindById(root, "block")!;

            Assert.DoesNotContain(item.Boxes, b => b.IsMarkerPseudoElement);

            var wrapper = item.Boxes[0];

            Assert.Null(wrapper.HtmlTag);
            Assert.Contains(wrapper.Boxes, b => b.IsMarkerPseudoElement);
        }

        /// <summary>
        /// An item whose only content asks to start on the next page has nothing to keep here, so css-break-3
        /// §3.1 moves the whole item — "a break before a container's own first in-flow child is the break
        /// point before the container". The marker is <c>Boxes[0]</c>, so counting it left the <c>&lt;p&gt;</c>
        /// looking like a *second* in-flow child and that rule never fired for a list item: the item stayed
        /// behind as an empty stub with its bullet beside nothing, and its content carried on unnumbered.
        /// </summary>
        [Fact]
        public async Task AnItemDeferredBeforeItsContentWasEverFlowed_TravelsWholeWithItsMarker()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p style='margin:0'>before</p>"
                + "<ol style='margin:0;padding-left:40pt'>"
                + "<li id='deferred'><p style='margin:0;break-before:page'>content here</p></li></ol>"));

            var item = LayoutHarness.FindById(root, "deferred")!;
            var word = Assert.Single(MarkerOf(item).Words);
            var fragments = FragmentsOf(container, item);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");

            // One fragment, on the page the item's content is on — no stub left behind on the page it was
            // declined on, and the marker with it.
            var fragment = Assert.Single(fragments);

            Assert.True(fragment.FragmentainerIndex > 0);
            Assert.Contains(fragment.Children, c => c.Box.IsMarkerPseudoElement);
            Assert.Single(ClaimsByWord(container)[word]);
        }

        /// <summary>
        /// An item can keep content that carries no words at all — a run of empty block children with heights
        /// of their own. Asking "did this pass keep anything?" of the item's <i>words</i> answers no for such
        /// an item, and its marker was handed to a later fragmentainer than the one its first line is in
        /// (<c>CssBox.HasPlacedContent</c>). Stated across a column boundary, which is where the answer is
        /// acted on.
        /// </summary>
        [Fact]
        public async Task AnItemWhoseKeptContentCarriesNoWords_KeepsItsMarkerWhereItBegins()
        {
            var items = string.Join("", Enumerable.Range(0, 3).Select(i =>
                $"<li id='li{i}' style='margin:0'>"
                + string.Join("", Enumerable.Range(0, 4).Select(_ => "<div style='height:40pt'></div>"))
                + "</li>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div style='column-count:2'><ul style='margin:0;padding-left:40pt'>{items}</ul></div>"),
                pageHeight: 300, margin: 10);

            var claims = ClaimsByWord(container);
            var crossings = 0;

            foreach (var item in ListItems(root))
            {
                var word = Assert.Single(MarkerOf(item).Words);
                var fragments = FragmentsOf(container, item);

                if (fragments.Count > 1) crossings++;

                Assert.True(claims.TryGetValue(word, out var slots),
                    $"the marker of '{Id(item)}' is claimed by no fragment at all");
                Assert.Single(slots!);
                Assert.Equal(0, fragments.FindIndex(f => f.Children.Any(c => c.Box.IsMarkerPseudoElement)));
            }

            Assert.True(crossings > 0, "the fixture must have some item cross a column boundary");
        }

        private static CssBox MarkerOf(CssBox item) => item.Boxes.Single(b => b.IsMarkerPseudoElement);

        /// <summary>Every fragment <paramref name="box"/> produced, in fill order.</summary>
        private static List<BoxFragment> FragmentsOf(HtmlContainerInt container, CssBox box) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => ReferenceEquals(f.Box, box))
                .ToList();

        private static List<CssBox> ListItems(CssBox root) =>
            LayoutHarness.Descendants(root)
                .Where(b => b.Display.Value == DisplayMode.ListItem)
                .ToList();

        private static string? Id(CssBox box) => box.HtmlTag?.TryGetAttribute("id");

        private static Dictionary<CssRect, List<int>> ClaimsByWord(HtmlContainerInt container)
        {
            var claims = new Dictionary<CssRect, List<int>>(ReferenceEqualityComparer.Instance);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in Flatten(fragmentainer.Root).SelectMany(f => f.Words))
                {
                    if (!claims.TryGetValue(word.Word, out var slots))
                    {
                        claims[word.Word] = slots = [];
                    }

                    slots.Add(fragmentainer.SlotIndex);
                }
            }

            return claims;
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
