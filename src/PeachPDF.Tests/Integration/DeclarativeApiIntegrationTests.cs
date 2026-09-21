using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Layout;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-assertion tests for the declarative document-building API (<see cref="PdfGenerator.CreateDocument"/>)
    /// core: the root-attachment seam, property decorators, text/rich-text, and Row/Column flex
    /// composition. Drives the internal builders directly and lays out via <see cref="HtmlContainerInt"/>
    /// (mirroring <see cref="PdfGenerator"/>'s own <c>AddDeclarativePage</c> up to - but not including -
    /// PDF rendering), the same way <c>FlexboxIntegrationTests</c> drives <see cref="HtmlContainerInt"/>
    /// directly for the HTML path, so assertions land on real post-layout <see cref="CssBox"/> geometry
    /// and resolved style values rather than merely "didn't throw".
    /// </summary>
    public class DeclarativeApiIntegrationTests
    {
        // ─── Row/Column flex composition - regression coverage for the empty-anonymous-box bug ────────
        //
        // DomUtils.GeneratesFlexOrGridItem excludes a box from becoming a flex item when it has no
        // HtmlTag AND is IsSpaceOrEmpty (no text anywhere in its own subtree) - a check meant to drop a
        // genuine whitespace-only anonymous box HTML parsing can produce. Every anonymous box the
        // declarative builders create (row/column containers, row/column items, line elements, rich-text
        // spans) used to pass a null HtmlTag, so a Row/Column item with only Background/Width/Height and
        // no Text/Image content - an extremely ordinary usage - was silently dropped from layout
        // entirely. CssPropertyFactory.CreateAnonymousBox fixes this by giving every such box a synthetic
        // HtmlTag. These tests fail loudly if that regresses.

        [Fact]
        public async Task Row_ItemsWithNoTextContent_StillBecomeFlexItemsAndAreLaidOut()
        {
            CssBox? item1 = null;
            CssBox? item2 = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Row(row =>
                    {
                        row.Spacing(5);
                        item1 = ((ContainerBuilder)row.Item().Background(PdfColor.Blue).Width(60).Height(30)).Box;
                        item2 = ((ContainerBuilder)row.Item().Background(PdfColor.Green).Width(60).Height(30)).Box;
                    });
                });
            });

            Assert.NotNull(item1);
            Assert.NotNull(item2);

            // Both items must actually be descendants of the laid-out root - excluded flex items never
            // reach a real Location at all (this is the exact symptom the bug produced: the row and its
            // items rendered as nothing).
            Assert.True(LayoutHarnessContains(root, item1!));
            Assert.True(LayoutHarnessContains(root, item2!));

            Assert.InRange(item1!.ActualBoxSizingWidth, 58, 62);
            Assert.InRange(item2!.ActualBoxSizingWidth, 58, 62);
            Assert.True(item1.Location.X < item2.Location.X, "row items must be arranged left-to-right");
            Assert.Equal(item1.Location.Y, item2.Location.Y, 1.0);

            Assert.Equal(0, item1.ActualBackgroundColor.R);
            Assert.Equal(0, item1.ActualBackgroundColor.G);
            Assert.Equal(255, item1.ActualBackgroundColor.B);
            Assert.Equal(0, item2.ActualBackgroundColor.R);
            Assert.Equal(128, item2.ActualBackgroundColor.G);
            Assert.Equal(0, item2.ActualBackgroundColor.B);
        }

        [Fact]
        public async Task Column_ItemsWithNoTextContent_StillBecomeFlexItemsAndAreLaidOut()
        {
            CssBox? item1 = null;
            CssBox? item2 = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        column.Spacing(4);
                        item1 = ((ContainerBuilder)column.Item().Background(PdfColor.Red).Width(40).Height(20)).Box;
                        item2 = ((ContainerBuilder)column.Item().Background(PdfColor.Red).Width(40).Height(20)).Box;
                    });
                });
            });

            Assert.True(LayoutHarnessContains(root, item1!));
            Assert.True(LayoutHarnessContains(root, item2!));
            Assert.True(item1!.Location.Y < item2!.Location.Y, "column items must be stacked top-to-bottom");
            Assert.InRange(item2.Location.Y - item1.ActualBottom, 3, 6);
        }

        [Fact]
        public async Task Row_ItemGrow_DistributesRemainingSpace()
        {
            CssBox? fixedItem = null;
            CssBox? growItem = null;

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(400));
                page.Margin(0);
                page.Content(c =>
                {
                    c.Row(row =>
                    {
                        fixedItem = ((ContainerBuilder)row.Item().Width(100).Height(20)).Box;
                        growItem = ((ContainerBuilder)row.Item().Grow().Height(20)).Box;
                    });
                });
            });

            Assert.NotNull(fixedItem);
            Assert.NotNull(growItem);
            Assert.InRange(fixedItem!.ActualBoxSizingWidth, 98, 102);
            // A 400pt page with no margin, minus the 100pt fixed item, leaves ~300pt for the grow item.
            Assert.InRange(growItem!.ActualBoxSizingWidth, 290, 310);
        }

        // ─── Padding / border / background / corner radius ──────────────────────────────────────────

        [Fact]
        public async Task Padding_Border_Background_CornerRadius_ReachResolvedBox()
        {
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container
                        .Padding(20)
                        .Border(2, PdfColor.FromRgb(10, 20, 30))
                        .Background(PdfColor.FromRgb(200, 210, 220))
                        .CornerRadius(8);
                    box = cb.Box;
                });
            });

            Assert.NotNull(box);
            Assert.InRange(box!.ActualPaddingTop, 19, 21);
            Assert.InRange(box.ActualPaddingLeft, 19, 21);
            Assert.InRange(box.ActualBorderTopWidth, 1.5, 2.5);
            Assert.Equal(10, box.ActualBorderTopColor.R);
            Assert.Equal(20, box.ActualBorderTopColor.G);
            Assert.Equal(30, box.ActualBorderTopColor.B);
            Assert.Equal(200, box.ActualBackgroundColor.R);
            Assert.Equal(210, box.ActualBackgroundColor.G);
            Assert.Equal(220, box.ActualBackgroundColor.B);
            Assert.True(box.ActualBorderTopLeftRadiusX > 0);
        }

        // ─── Table ───────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Table_ColumnsHeaderAndColspan_LayOutCorrectly()
        {
            CssBox? headerCell1 = null;
            CssBox? bodyCell1 = null;
            CssBox? bodyCell2 = null;
            CssBox? spanningCell = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(400));
                page.Margin(0);
                page.Content(container =>
                {
                    container.Table(table =>
                    {
                        table.Columns(columns =>
                        {
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                        });
                        table.Header(header =>
                        {
                            var h1 = (ContainerBuilder)header.Cell();
                            h1.Text("Head1");
                            headerCell1 = h1.Box;
                            ((ContainerBuilder)header.Cell()).Text("Head2");
                        });
                        table.Row(row =>
                        {
                            var c1 = (ContainerBuilder)row.Cell();
                            c1.Text("A");
                            bodyCell1 = c1.Box;
                            var c2 = (ContainerBuilder)row.Cell();
                            c2.Text("B");
                            bodyCell2 = c2.Box;
                        });
                        table.Row(row =>
                        {
                            var spanCell = (ContainerBuilder)row.Cell(columnSpan: 2);
                            spanCell.Text("Spanning");
                            spanningCell = spanCell.Box;
                        });
                    });
                });
            });

            // Note: a table's header/footer row group is repeated per page via a CssProxyBox wrapper
            // (see CLAUDE.md's fragment-tree architecture note), so the *original* header/footer boxes
            // this test holds are not necessarily reachable from a plain CssBox.Boxes walk after layout -
            // their own Location/size are still resolved directly on them, which is what's asserted here.

            // Column 1 (weight 1) is roughly half the width of column 2 (weight 2).
            Assert.InRange(bodyCell2!.ActualBoxSizingWidth / bodyCell1!.ActualBoxSizingWidth, 1.7, 2.3);

            // The header row sits above both body rows, in document order.
            Assert.True(headerCell1!.Location.Y < bodyCell1.Location.Y);
            Assert.True(bodyCell1.Location.Y < spanningCell!.Location.Y);

            // A colspan=2 cell spans (approximately) both columns combined.
            var combinedWidth = bodyCell1.ActualBoxSizingWidth + bodyCell2.ActualBoxSizingWidth;
            Assert.InRange(spanningCell.ActualBoxSizingWidth, combinedWidth - 5, combinedWidth + 5);
        }

        [Fact]
        public async Task Table_FixedColumnAndFooter_ReachResolvedBox()
        {
            CssBox? fixedCell = null;
            CssBox? footerCell = null;

            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(400));
                page.Margin(0);
                page.Content(container =>
                {
                    container.Table(table =>
                    {
                        table.Columns(columns =>
                        {
                            columns.FixedColumn(80);
                            columns.RelativeColumn();
                        });
                        table.Row(row =>
                        {
                            var c1 = (ContainerBuilder)row.Cell();
                            c1.Text("Fixed");
                            fixedCell = c1.Box;
                            ((ContainerBuilder)row.Cell()).Text("Rest");
                        });
                        table.Footer(footer =>
                        {
                            var f1 = (ContainerBuilder)footer.Cell();
                            f1.Text("Foot1");
                            footerCell = f1.Box;
                            ((ContainerBuilder)footer.Cell()).Text("Foot2");
                        });
                    });
                });
            });

            Assert.InRange(fixedCell!.ActualBoxSizingWidth, 75, 85);

            Assert.True(footerCell!.Location.Y > fixedCell.Location.Y, "the footer row must render below the body row");
        }

        // ─── Lists ───────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task OrderedList_ItemsGetSequentialDecimalMarkers()
        {
            CssBox? item1 = null;
            CssBox? item2 = null;
            CssBox? item3 = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.OrderedList(list =>
                    {
                        var i1 = (ContainerBuilder)list.Item();
                        i1.Text("First");
                        item1 = i1.Box;
                        var i2 = (ContainerBuilder)list.Item();
                        i2.Text("Second");
                        item2 = i2.Box;
                        var i3 = (ContainerBuilder)list.Item();
                        i3.Text("Third");
                        item3 = i3.Box;
                    });
                });
            });

            var marker1 = Assert.Single(item1!.Boxes, b => b.IsMarkerPseudoElement);
            var marker2 = Assert.Single(item2!.Boxes, b => b.IsMarkerPseudoElement);
            var marker3 = Assert.Single(item3!.Boxes, b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", marker1.Text);
            Assert.Equal("2.", marker2.Text);
            Assert.Equal("3.", marker3.Text);
        }

        [Fact]
        public async Task UnorderedList_DefaultsToDiscMarker()
        {
            CssBox? item1 = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.UnorderedList(list =>
                    {
                        var i1 = (ContainerBuilder)list.Item();
                        i1.Text("Bullet");
                        item1 = i1.Box;
                    });
                });
            });

            var marker = Assert.Single(item1!.Boxes, b => b.IsMarkerPseudoElement);
            Assert.Equal("disc", ((CssBoxMarker)marker).MarkerShape);

            // Position, not just shape: an "outside" marker (the CSS default) needs real room to its
            // left to draw into - one a declarative list must supply itself with its own padding-left,
            // since there is no UA stylesheet ul/ol default to inherit it from. Without that, the marker
            // computes a negative, off-page X (CssBoxMarker.PerformLayoutImp) and never actually renders,
            // even though its shape/content resolved correctly - exactly the kind of gap a shape-only
            // assertion misses (see ContainerBuilder.BuildList's own padding-left default).
            Assert.True(marker.Location.X >= 0, $"marker rendered off-page at X={marker.Location.X}");
        }

        [Fact]
        public async Task UnorderedList_MarkerTextProducesLiteralMarker()
        {
            CssBox? item1 = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.UnorderedList(list =>
                    {
                        list.MarkerText("-> ");
                        var i1 = (ContainerBuilder)list.Item();
                        i1.Text("Arrow item");
                        item1 = i1.Box;
                    });
                });
            });

            var marker = Assert.Single(item1!.Boxes, b => b.IsMarkerPseudoElement);
            Assert.Equal("-> ", marker.Text);
        }

        [Fact]
        public async Task OrderedList_RomanMarkerType_ProducesRomanNumerals()
        {
            CssBox? item1 = null;
            CssBox? item2 = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.OrderedList(list =>
                    {
                        var i1 = (ContainerBuilder)list.Item();
                        i1.Text("One");
                        item1 = i1.Box;
                        var i2 = (ContainerBuilder)list.Item();
                        i2.Text("Two");
                        item2 = i2.Box;
                    }, PdfListMarkerType.UpperRoman);
                });
            });

            var marker1 = Assert.Single(item1!.Boxes, b => b.IsMarkerPseudoElement);
            var marker2 = Assert.Single(item2!.Boxes, b => b.IsMarkerPseudoElement);
            Assert.Equal("I.", marker1.Text);
            Assert.Equal("II.", marker2.Text);
        }

        [Fact]
        public async Task List_PositionInside_SetsListStylePositionOnItems()
        {
            CssBox? item1 = null;

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    c.UnorderedList(list =>
                    {
                        list.Position(PdfListMarkerPosition.Inside);
                        var i1 = (ContainerBuilder)list.Item();
                        i1.Text("Item");
                        item1 = i1.Box;
                    });
                });
            });

            Assert.Equal("inside", item1!.ListStylePosition);
        }

        [Fact]
        public void List_MarkerImageOverloads_SetListStyleImageOnTheListBox()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var descriptor = DocumentBuilder.BuildPage(page =>
                page.Content(c =>
                {
                    c.UnorderedList(list =>
                    {
                        list.MarkerImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 });
                        list.Item();
                    });
                }), properties);

            var listBox = Assert.Single(descriptor.RootBox.Boxes);
            var image = Assert.IsType<CssImage.Url>(listBox.ListStyleImage);
            Assert.StartsWith("data:image/png;base64,", image.Href);

            var byStreamDescriptor = DocumentBuilder.BuildPage(page =>
                page.Content(c => c.UnorderedList(list =>
                {
                    list.MarkerImage(new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }));
                    list.Item();
                })), properties);
            var byStreamImage = Assert.IsType<CssImage.Url>(Assert.Single(byStreamDescriptor.RootBox.Boxes).ListStyleImage);
            Assert.StartsWith("data:image/jpeg;base64,", byStreamImage.Href);

            var byUriDescriptor = DocumentBuilder.BuildPage(page =>
                page.Content(c => c.UnorderedList(list =>
                {
                    list.MarkerImage(new Uri("https://example.com/marker.png"));
                    list.Item();
                })), properties);
            var byUriImage = Assert.IsType<CssImage.Url>(Assert.Single(byUriDescriptor.RootBox.Boxes).ListStyleImage);
            Assert.Equal("https://example.com/marker.png", byUriImage.Href);

            var byPathDescriptor = DocumentBuilder.BuildPage(page =>
                page.Content(c => c.UnorderedList(list =>
                {
                    list.MarkerImage("C:/marker.png");
                    list.Item();
                })), properties);
            var byPathImage = Assert.IsType<CssImage.Url>(Assert.Single(byPathDescriptor.RootBox.Boxes).ListStyleImage);
            Assert.Equal("C:/marker.png", byPathImage.Href);
        }

        [Fact]
        public void PdfListMarkerType_ToKeyword_ProducesANonEmptyKeywordForEveryValue()
        {
            foreach (PdfListMarkerType markerType in Enum.GetValues<PdfListMarkerType>())
            {
                Assert.False(string.IsNullOrEmpty(markerType.ToKeyword()));
            }
        }

        [Fact]
        public async Task TextSpan_DecorationStyle_AllValuesResolve()
        {
            foreach (var style in Enum.GetValues<PdfTextDecorationStyle>())
            {
                CssBox? spanBox = null;
                await BuildAndLayoutPage(page =>
                {
                    page.Content(container =>
                    {
                        container.Text(t =>
                        {
                            var span = t.Span("x");
                            span.Underline();
                            span.DecorationStyle(style);
                            spanBox = ((TextStyleApplier)span).Box;
                        });
                    });
                });
                Assert.NotNull(spanBox);
            }
        }

        [Fact]
        public async Task LineHorizontalAndVertical_ProduceFilledRuleBoxes()
        {
            CssBox? hLine = null;
            CssBox? vLine = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        var hContainer = (ContainerBuilder)column.Item();
                        hContainer.LineHorizontal(3, PdfColor.FromRgb(11, 12, 13));
                        hLine = Assert.Single(hContainer.Box.Boxes);

                        var vContainer = (ContainerBuilder)column.Item();
                        vContainer.LineVertical(4, PdfColor.FromRgb(14, 15, 16));
                        vLine = Assert.Single(vContainer.Box.Boxes);
                    });
                });
            });

            Assert.True(LayoutHarnessContains(root, hLine!));
            Assert.InRange(hLine!.ActualBottom - hLine.Location.Y, 2.5, 3.5);
            Assert.Equal(11, hLine.ActualBackgroundColor.R);

            Assert.True(LayoutHarnessContains(root, vLine!));
            Assert.InRange(vLine!.ActualBoxSizingWidth, 3.5, 4.5);
            Assert.Equal(14, vLine.ActualBackgroundColor.R);
        }

        [Fact]
        public async Task LineHorizontalAndVertical_Dashed_ProduceDashedBorderRuleBoxes()
        {
            CssBox? hLine = null;
            CssBox? vLine = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        var hContainer = (ContainerBuilder)column.Item();
                        hContainer.LineHorizontal(3, PdfColor.FromRgb(11, 12, 13), dashed: true);
                        hLine = Assert.Single(hContainer.Box.Boxes);

                        var vContainer = (ContainerBuilder)column.Item();
                        vContainer.LineVertical(4, PdfColor.FromRgb(14, 15, 16), dashed: true);
                        vLine = Assert.Single(vContainer.Box.Boxes);
                    });
                });
            });

            Assert.True(LayoutHarnessContains(root, hLine!));
            Assert.Equal(PeachPDF.CSS.LineStyle.Dashed, hLine!.BorderTopStyle.Value);
            Assert.InRange(hLine.ActualBorderTopWidth, 2.5, 3.5);
            Assert.Equal(11, hLine.ActualBorderTopColor.R);
            Assert.False(RenderUtils.IsColorVisible(hLine.ActualBackgroundColor));

            Assert.True(LayoutHarnessContains(root, vLine!));
            Assert.Equal(PeachPDF.CSS.LineStyle.Dashed, vLine!.BorderLeftStyle.Value);
            Assert.InRange(vLine.ActualBorderLeftWidth, 3.5, 4.5);
            Assert.Equal(14, vLine.ActualBorderLeftColor.R);
            Assert.False(RenderUtils.IsColorVisible(vLine.ActualBackgroundColor));
        }

        [Fact]
        public async Task LineHorizontalAndVertical_DashedWithPercentageThickness_Throws()
        {
            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        var hContainer = (ContainerBuilder)column.Item();
                        Assert.Throws<ArgumentException>(() =>
                            hContainer.LineHorizontal(PdfLength.Percent(50), dashed: true));

                        var vContainer = (ContainerBuilder)column.Item();
                        Assert.Throws<ArgumentException>(() =>
                            vContainer.LineVertical(PdfLength.Percent(50), dashed: true));
                    });
                });
            });
        }

        // ─── Text / rich text ────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task PlainText_SetsTextAndProducesWordsAfterLayout()
        {
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Text("Hello world");
                });
            });

            // Text() creates a genuine child box for the text rather than setting box.Text directly - a
            // CssBox must never hold both its own words and child boxes at once, and the wrapped box can
            // already have a child it never advertises (a list item's synthesized ::marker, most
            // notably), so this holds even for a container with no such child.
            var textBox = Assert.Single(cb!.Box.Boxes);
            Assert.Equal("Hello world", textBox.Text);
            Assert.True(textBox.Words.Count >= 2, "SetDeclarativeRoot must run ParseToWords over the built tree");
        }

        [Fact]
        public async Task TextSpan_BoldAndColor_ReachResolvedBox()
        {
            CssBox? spanBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Text(t =>
                    {
                        var span = t.Span("styled");
                        span.Bold().FontColor(PdfColor.FromRgb(1, 2, 3));
                        spanBox = ((TextStyleApplier)span).Box;
                    });
                });
            });

            Assert.NotNull(spanBox);
            Assert.Equal("styled", spanBox!.Text);
            Assert.Equal(700, spanBox.ActualNumericWeight);
            Assert.Equal(1, spanBox.ActualColor.R);
            Assert.Equal(2, spanBox.ActualColor.G);
            Assert.Equal(3, spanBox.ActualColor.B);
        }

        // ─── Hyperlink / bookmark ───────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Hyperlink_ProducesClickableAnchorBox()
        {
            CssBox? linkBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var link = (ContainerBuilder)container.Hyperlink("https://example.com/");
                    link.Text("click me");
                    linkBox = link.Box;
                });
            });

            Assert.NotNull(linkBox);
            Assert.True(linkBox!.IsClickable);
            Assert.Equal("a", linkBox.HtmlTag!.Name, ignoreCase: true);
            Assert.Equal("https://example.com/", linkBox.HtmlTag.TryGetAttribute("href", ""));
        }

        // ─── Header / Footer ────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task HeaderAndFooter_RepeatAsRealMarginBoxContentOnEveryPage()
        {
            CssBox? headerBox = null;
            CssBox? footerBox = null;

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(300));
                page.Margin(20);
                page.Header(h =>
                {
                    var hb = (ContainerBuilder)h;
                    hb.Text("HEADER");
                    headerBox = hb.Box;
                });
                page.Footer(f =>
                {
                    var fb = (ContainerBuilder)f;
                    fb.Text("FOOTER");
                    footerBox = fb.Box;
                });
                page.Content(c =>
                {
                    c.Column(column =>
                    {
                        for (var i = 0; i < 40; i++)
                        {
                            column.Item().Height(30).Text($"Line {i}");
                        }
                    });
                });
            });

            Assert.NotNull(container.FragmentTree);
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must actually paginate, or this proves nothing about repeating headers/footers");

            foreach (var fragmentainer in container.FragmentTree.Fragmentainers)
            {
                var headerMarginBox = Assert.Single(fragmentainer.MarginBoxes, m => m.BoxName == "top-center");
                Assert.Same(headerBox, headerMarginBox.Content.Box);

                var footerMarginBox = Assert.Single(fragmentainer.MarginBoxes, m => m.BoxName == "bottom-center");
                Assert.Same(footerBox, footerMarginBox.Content.Box);

                // Rect/position assertions, not just presence: a header/footer box with no explicit
                // `display` defaults to inline (there is no UA stylesheet to give it `block` the way a
                // real HTML heading/footer element would get), which measures to a degenerate,
                // zero-width rect glued to the page's top-left corner instead of the actual top-center/
                // bottom-center margin-box slot - exactly the kind of bug a same-object-identity check
                // alone would miss (BuildRunningElement sets `display: block` explicitly to avoid this).
                Assert.True(headerMarginBox.Content.Rect.Width > 0, "header margin box collapsed to zero width");
                Assert.True(footerMarginBox.Content.Rect.Width > 0, "footer margin box collapsed to zero width");
                Assert.True(headerMarginBox.Content.Rect.Y < footerMarginBox.Content.Rect.Y,
                    "header must sit above footer, not overlap it at the same position");
            }
        }

        [Fact]
        public async Task Footer_CurrentPageNumberAndTotalPages_ResolvePerPage()
        {
            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(300));
                page.Margin(20);
                page.Footer(f =>
                {
                    f.Text(t =>
                    {
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
                page.Content(c =>
                {
                    c.Column(column =>
                    {
                        for (var i = 0; i < 40; i++)
                        {
                            column.Item().Height(30).Text($"Line {i}");
                        }
                    });
                });
            });

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 2,
                "need at least 3 pages to distinguish a per-page counter from a constant value");

            var totalPages = container.FragmentTree.Fragmentainers.Count;
            var seenPageTexts = new System.Collections.Generic.HashSet<string>();

            foreach (var fragmentainer in container.FragmentTree.Fragmentainers)
            {
                var footerMarginBox = Assert.Single(fragmentainer.MarginBoxes, m => m.BoxName == "bottom-center");

                // Read from the frozen per-page BoxFragment tree, not the live CssBox: RefreshPageCounterContent
                // mutates the same shared counter box's own Words/Text in place on every page, so by the
                // time all pages have been laid out, box.Text reflects only the LAST page's value - the
                // fragment tree is what actually keeps each page's own resolved text distinct.
                var footerText = string.Concat(CollectFragmentWords(footerMarginBox.Content));
                Assert.EndsWith($"of{totalPages}", footerText);
                seenPageTexts.Add(footerText);
            }

            Assert.Equal(totalPages, seenPageTexts.Count);
        }

        [Fact]
        public async Task Footer_PageNumberWithinSectionAndTotalPagesWithinSection_ResolvePerPageAgainstTheMarkedSection()
        {
            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(300));
                page.Margin(20);
                page.Footer(f =>
                {
                    f.Text(t =>
                    {
                        t.Span("S:");
                        t.PageNumberWithinSection("body");
                        t.Span("/");
                        t.TotalPagesWithinSection("body");
                    });
                });
                page.Content(c =>
                {
                    c.Column(column =>
                    {
                        // Enough lines before the section starts to spill onto a second document page
                        // before the section's own first page (260pt of content per page / 30pt items
                        // fits 8 per page), so its own first page isn't document page 1 - otherwise a bug
                        // that always returned the document's own page number (ignoring the begin marker
                        // entirely) could pass by coincidence.
                        for (var i = 0; i < 12; i++)
                        {
                            column.Item().Height(30).Text($"Before {i}");
                        }

                        for (var i = 0; i < 40; i++)
                        {
                            var item = column.Item().Height(30);
                            if (i == 0) item = item.BeginPageNumberOfSection("body");
                            if (i == 39) item = item.EndPageNumberOfSection("body");
                            item.Text($"Body {i}");
                        }

                        // And enough lines after the section ends to spill onto a further page, for the
                        // same reason in reverse.
                        for (var i = 0; i < 12; i++)
                        {
                            column.Item().Height(30).Text($"After {i}");
                        }
                    });
                });
            });

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 5,
                "need pages before, spanning, and after the section to actually distinguish this from CurrentPageNumber()/TotalPages()");

            var currents = new System.Collections.Generic.List<int>();
            var totals = new System.Collections.Generic.List<int>();

            foreach (var fragmentainer in container.FragmentTree.Fragmentainers)
            {
                var footerMarginBox = Assert.Single(fragmentainer.MarginBoxes, m => m.BoxName == "bottom-center");
                var footerText = string.Concat(CollectFragmentWords(footerMarginBox.Content));

                // "S:{current}/{total}"
                var match = System.Text.RegularExpressions.Regex.Match(footerText, @"^S:(-?\d+)/(\d+)$");
                Assert.True(match.Success, $"unexpected footer text '{footerText}'");
                currents.Add(int.Parse(match.Groups[1].Value));
                totals.Add(int.Parse(match.Groups[2].Value));
            }

            // The total is a fixed fact about the section (its own page span), not the current page -
            // every page agrees on it.
            Assert.All(totals, t => Assert.Equal(totals[0], t));
            var total = totals[0];

            // The current-page-within-section number climbs by exactly one per document page,
            // unconditionally - including before the section starts (non-positive) and after it ends
            // (beyond the total). This is the documented, deliberately unclamped behavior: a section is
            // defined entirely by its own begin/end markers' pages, with no special-casing for a caller
            // reading it outside that range.
            for (var i = 1; i < currents.Count; i++)
            {
                Assert.Equal(currents[i - 1] + 1, currents[i]);
            }

            // Exactly one page has current == 1 (the section's own first page) and exactly one has
            // current == total (its own last page) - the begin/end markers' own pages, found without
            // assuming exactly how many lines fit per page.
            Assert.Equal(1, currents.Count(v => v == 1));
            Assert.Equal(1, currents.Count(v => v == total));
            Assert.True(currents[0] <= 0, "the section starts after document page 1, so its first page's footer should show a non-positive current value");
            Assert.True(currents[^1] > total, "the section ends before the last document page, so its last page's footer should show a value past the total");
        }

        [Fact]
        public async Task Footer_PageNumberWithinSection_SinglePageSection_CurrentAndTotalAreBothOne()
        {
            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(300));
                page.Margin(20);
                page.Footer(f => f.Text(t =>
                {
                    t.PageNumberWithinSection("solo");
                    t.Span("/");
                    t.TotalPagesWithinSection("solo");
                }));
                page.Content(c => c.Column(column =>
                {
                    column.Item().BeginPageNumberOfSection("solo").EndPageNumberOfSection("solo").Text("Only line in the section.");
                }));
            });

            Assert.Single(container.FragmentTree!.Fragmentainers);
            var footerMarginBox = Assert.Single(container.FragmentTree.Fragmentainers[0].MarginBoxes, m => m.BoxName == "bottom-center");
            Assert.Equal("1/1", string.Concat(CollectFragmentWords(footerMarginBox.Content)));
        }

        [Fact]
        public async Task Footer_PageNumberWithinSection_UnknownSectionId_FallsBackToOne()
        {
            // BeginPageNumberOfSection/EndPageNumberOfSection were never called for this id (a typo, or a
            // section the caller forgot to mark) - resolves to the same "1" placeholder a target-counter(_,
            // page) with an unresolved target already falls back to, rather than throwing.
            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Footer(f => f.Text(t =>
                {
                    t.PageNumberWithinSection("never-marked");
                    t.Span("/");
                    t.TotalPagesWithinSection("never-marked");
                }));
                page.Content(c => c.Text("Just some content."));
            });

            var footerMarginBox = Assert.Single(container.FragmentTree!.Fragmentainers[0].MarginBoxes, m => m.BoxName == "bottom-center");
            Assert.Equal("1/1", string.Concat(CollectFragmentWords(footerMarginBox.Content)));
        }

        [Fact]
        public async Task Footer_TotalPagesWithinSection_BeginMarkerWithNoMatchingEndMarker_FallsBackToOne()
        {
            // A caller called BeginPageNumberOfSection but forgot the matching EndPageNumberOfSection -
            // PageNumberWithinSection (which only ever needs the begin marker) still resolves correctly;
            // only TotalPagesWithinSection (which also needs the end marker) falls back.
            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Footer(f => f.Text(t =>
                {
                    t.PageNumberWithinSection("unclosed");
                    t.Span("/");
                    t.TotalPagesWithinSection("unclosed");
                }));
                page.Content(c => c.Column(column =>
                {
                    column.Item().BeginPageNumberOfSection("unclosed").Text("No EndPageNumberOfSection call follows.");
                }));
            });

            var footerMarginBox = Assert.Single(container.FragmentTree!.Fragmentainers[0].MarginBoxes, m => m.BoxName == "bottom-center");
            Assert.Equal("1/1", string.Concat(CollectFragmentWords(footerMarginBox.Content)));
        }

        [Fact]
        public async Task PageNumberWithinSection_UsedOutsideHeaderOrFooter_RendersEmpty()
        {
            // Matches CurrentPageNumber()/TotalPages()'s own documented behavior for the same reason: both
            // are resolved only by the per-page running-element refresh, which never runs outside a
            // Header/Footer's own content.
            CssBox? span = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Text(t =>
                {
                    t.Span("Before ");
                    span = ((TextStyleApplier)t.PageNumberWithinSection("body")).Box;
                    t.Span(" after");
                }));
            });

            Assert.True(LayoutHarnessContains(root, span!));
            Assert.True(string.IsNullOrEmpty(span!.Text));
        }

        [Fact]
        public async Task BeginAndEndPageNumberOfSection_TagTheExistingContainer_DoNotInsertAnExtraFlexItem()
        {
            // Regression: an earlier design placed a dedicated zero-size marker box per Begin/End call,
            // which - even at zero size - still consumed its own flex item slot, adding a full unwanted
            // Column.Spacing() gap on each side of it (CSS row-gap applies between every pair of adjacent
            // flex items regardless of their own size). BeginPageNumberOfSection/EndPageNumberOfSection
            // now tag whatever container the caller already has instead of creating a new one, so the gap
            // between consecutive items should be exactly one Spacing() value throughout - not two around
            // a tagged item.
            CssBox? itemA = null, itemB = null, itemC = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Column(column =>
                {
                    column.Spacing(12);
                    itemA = ((ContainerBuilder)column.Item().Height(20).Background(PdfColor.Red)).Box;

                    var b = (ContainerBuilder)column.Item().Height(20).BeginPageNumberOfSection("gap-check");
                    itemB = b.Box;

                    itemC = ((ContainerBuilder)column.Item().Height(20).EndPageNumberOfSection("gap-check")).Box;
                }));
            });

            Assert.NotNull(itemA);
            Assert.NotNull(itemB);
            Assert.NotNull(itemC);

            var gapAB = itemB!.Location.Y - itemA!.ActualBottom;
            var gapBC = itemC!.Location.Y - itemB.ActualBottom;

            Assert.Equal(12, gapAB, precision: 1);
            Assert.Equal(12, gapBC, precision: 1);
        }

        [Fact]
        public async Task Bookmark_SetsLevelAndLabel_ReadableByBookmarkOutlineBuilder()
        {
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Bookmark("My \"Quoted\" Heading", level: 2);
                    cb.Text("Heading");
                    box = cb.Box;
                });
            });

            Assert.NotNull(box);
            Assert.Equal("2", box!.BookmarkLevel);
            Assert.Equal("My \"Quoted\" Heading", CssContentEngine.ResolveBookmarkLabel(box));
        }

        // ─── Page size / margin config fallback ────────────────────────────────────────────────────

        [Fact]
        public async Task Page_WithNoExplicitSizeOrMargin_FallsBackToConfig()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A5,
                MarginTop = 30,
                MarginBottom = 25,
                MarginLeft = 15,
                MarginRight = 12
            };

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Text("x"));
            }, config);

            Assert.Equal(30, container.MarginTop);
            Assert.Equal(25, container.MarginBottom);
            Assert.Equal(15, container.MarginLeft);
            Assert.Equal(12, container.MarginRight);
        }

        [Fact]
        public async Task Page_WithExplicitSizeAndMargin_OverridesConfig()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A5, MarginTop = 30 };

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(300), PdfLength.Points(500));
                page.Margin(10);
                page.Content(c => c.Text("x"));
            }, config);

            Assert.Equal(10, container.MarginTop);
        }

        [Fact]
        public async Task CreateDocument_WithNoConfigAtAll_ProducesARealNonEmptyPage()
        {
            // Regression coverage for PdfGenerator.AddPages's default-config construction: passing no
            // config at all must not silently fall back to PdfGenerateConfig's own bare field defaults
            // (PageSize.Undefined, 0pt margins), which would build a 0x0-point page.
            var generator = new PdfGenerator();

            var document = await generator.CreateDocument(doc =>
            {
                doc.Page(page =>
                {
                    page.Content(container => container.Text("Hello"));
                });
            });

            using var ms = new System.IO.MemoryStream();
            document.Save(ms);
            Assert.True(ms.Length > 0);
        }

        [Fact]
        public async Task CreateDocument_PageOrientationLandscape_SwapsPhysicalDimensions()
        {
            var generator = new PdfGenerator();

            var document = await generator.CreateDocument(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PdfLength.Points(400), PdfLength.Points(600));
                    page.Orientation(PageOrientation.Landscape);
                    page.Content(c => c.Text("x"));
                });
            });

            Assert.Equal(600, document.PdfDocument.Pages[0].Width.Point, 1);
            Assert.Equal(400, document.PdfDocument.Pages[0].Height.Point, 1);
        }

        // ─── Remaining container decorators / image overloads ──────────────────────────────────────

        [Fact]
        public async Task RemainingContainerDecorators_ReachResolvedBox()
        {
            ContainerBuilder? cb = null;
            CssBox? linkBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container
                        .PaddingHorizontal(5)
                        .PaddingVertical(12.5) // a real double literal, not int - exercises PdfLength's double implicit operator
                        .BorderHorizontal(1, PdfColor.FromRgb(9, 8, 7))
                        .BorderVertical(1, PdfColor.FromRgb(9, 8, 7))
                        .BorderColor(PdfColor.FromRgb(3, 3, 3))
                        .BackgroundLinearGradient(45, PdfColor.Red, PdfColor.Blue)
                        .AlignLeft()
                        .AlignCenter()
                        .AlignRight()
                        .DefaultTextStyle(style => style.FontSize(11));

                    var link = (ContainerBuilder)cb.Hyperlink(new Uri("https://example.com/page"));
                    link.Text("linked");
                    linkBox = link.Box;
                });
            });

            Assert.NotNull(cb);
            Assert.InRange(cb!.Box.ActualPaddingLeft, 4, 6);
            Assert.InRange(cb.Box.ActualPaddingRight, 4, 6);
            Assert.InRange(cb.Box.ActualPaddingTop, 11.5, 13.5);
            Assert.InRange(cb.Box.ActualPaddingBottom, 11.5, 13.5);
            Assert.InRange(cb.Box.ActualBorderLeftWidth, 0.5, 1.5);
            Assert.InRange(cb.Box.ActualBorderRightWidth, 0.5, 1.5);
            Assert.InRange(cb.Box.ActualBorderTopWidth, 0.5, 1.5);
            Assert.InRange(cb.Box.ActualBorderBottomWidth, 0.5, 1.5);
            Assert.Equal(3, cb.Box.ActualBorderTopColor.R);
            Assert.Equal(3, cb.Box.ActualBorderLeftColor.R);
            Assert.True(cb.Box.BackgroundImages is { Count: > 0 });

            Assert.NotNull(linkBox);
            Assert.True(linkBox!.IsClickable);
            Assert.Equal("https://example.com/page", linkBox.HtmlTag!.TryGetAttribute("href", ""));
        }

        [Fact]
        public void ContainerBuilder_SecondTerminalCall_Throws()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            DocumentBuilder.BuildPage(page =>
                page.Content(c =>
                {
                    c.Text("first");
                    Assert.Throws<InvalidOperationException>(() => c.Text("second"));
                }), properties);
        }

        // A genuine, minimal, decodable 1x1 PNG - Tier 1 (Image(byte[])/Image(Stream)/Image(PdfImage))
        // decodes eagerly at declarative-build time, unlike the old data-URI-wrapping design, so a test
        // exercising it needs bytes a real decoder actually accepts, not just PNG-shaped magic bytes.
        private static readonly byte[] MinimalPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC");

        [Fact]
        public void ContainerImage_ByteStreamOverloads_DecodeEagerlyIntoARealCssBoxImage()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var byBytes = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Image(MinimalPng)), properties);
            var imgFromBytes = Assert.Single(byBytes.RootBox.Boxes);
            // The box must be a genuine CssBoxImage - not just an "img"-tagged plain CssBox (the pre-
            // existing bug this PR fixes: CssBox.CreateBox(CssBox, HtmlTag) - the overload every other
            // terminal here uses - never dispatches by tag name, so it silently produced a plain CssBox
            // that FragmentContentPainters.For's CssBoxImage => ImagePainter arm never matched, and the
            // image never painted at all).
            var imageBox = Assert.IsType<CssBoxImage>(imgFromBytes);
            Assert.NotNull(imageBox.Image);
            // No src at all - decoded directly, no data-URI/ImageLoadHandler round trip.
            Assert.True(string.IsNullOrEmpty(imageBox.HtmlTag!.TryGetAttribute("src", "")));

            var byStream = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Image(new MemoryStream(MinimalPng))), properties);
            var imageFromStream = Assert.IsType<CssBoxImage>(Assert.Single(byStream.RootBox.Boxes));
            Assert.NotNull(imageFromStream.Image);
        }

        [Fact]
        public async Task ContainerImage_ReachesTheFragmentTreePaintActuallyConsumes()
        {
            // Regression coverage for the exact bug this PR fixes: a declarative Image() box reaching
            // paint at all. A CssBoxImage existing in the CssBox tree (the other tests in this file) is
            // not by itself proof it paints - FragmentContentPainters.For dispatches by box TYPE, so a
            // plain CssBox with an "img" tag (what CssBox.CreateBox(CssBox,HtmlTag) silently produced
            // before this fix) would never reach ImagePainter, and the image would render as nothing, with
            // every structural assertion above still passing. Laying out for real and confirming the box
            // actually produces a fragment is what proves it reaches the paint consumers rely on.
            CssBox? imageBox = null;

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    var cb = (ContainerBuilder)c.Width(50).Height(50);
                    cb.Image(MinimalPng);
                    imageBox = Assert.Single(cb.Box.Boxes);
                });
            });

            Assert.IsType<CssBoxImage>(imageBox);
            // FragmentOf itself is the meaningful assertion here (it fails the test if the box produced
            // no fragment at all on this page) - a box excluded from the fragment tree can never reach
            // paint regardless of its own C# type.
            FragmentPaintHarness.FragmentOf(container, imageBox!);
        }

        [Fact]
        public void ContainerImage_UriAndFilePath_StillGoThroughTheLazySrcPath()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var byUri = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Image(new Uri("https://example.com/pic.png"))), properties);
            var imgFromUri = Assert.IsType<CssBoxImage>(Assert.Single(byUri.RootBox.Boxes));
            Assert.Equal("https://example.com/pic.png", imgFromUri.HtmlTag!.TryGetAttribute("src", ""));

            var byPath = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Image("C:/some/local/path.png")), properties);
            var imgFromPath = Assert.IsType<CssBoxImage>(Assert.Single(byPath.RootBox.Boxes));
            Assert.Equal("C:/some/local/path.png", imgFromPath.HtmlTag!.TryGetAttribute("src", ""));
        }

        [Fact]
        public void ContainerImage_SharedPdfImage_ResolvesOnceAndReusesTheSameDecodedImage()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var sharedImage = PdfImage.FromBytes(MinimalPng);
            var byPdfImage1 = DocumentBuilder.BuildPage(page => page.Content(c => c.Image(sharedImage)), properties);
            var byPdfImage2 = DocumentBuilder.BuildPage(page => page.Content(c => c.Image(sharedImage)), properties);
            var img1 = Assert.IsType<CssBoxImage>(Assert.Single(byPdfImage1.RootBox.Boxes));
            var img2 = Assert.IsType<CssBoxImage>(Assert.Single(byPdfImage2.RootBox.Boxes));

            Assert.NotNull(img1.Image);
            // The whole point of a shared PdfImage: the second placement reuses the exact same decoded
            // RImage instance rather than decoding the bytes a second time.
            Assert.Same(img1.Image, img2.Image);
        }

        private const string MinimalSvg =
            """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10" fill="red"/></svg>""";

        [Fact]
        public void ContainerSvg_StringStreamAndBytesOverloads_ParseIntoARealSvgDocument()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var byString = DocumentBuilder.BuildPage(page => page.Content(c => c.Svg(MinimalSvg)), properties);
            var imgFromString = Assert.IsType<CssBoxImage>(Assert.Single(byString.RootBox.Boxes));
            Assert.NotNull(imgFromString.SvgDocument);
            Assert.Null(imgFromString.Image);

            var byStream = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Svg(new MemoryStream(Encoding.UTF8.GetBytes(MinimalSvg)))), properties);
            var imgFromStream = Assert.IsType<CssBoxImage>(Assert.Single(byStream.RootBox.Boxes));
            Assert.NotNull(imgFromStream.SvgDocument);

            var byBytes = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Svg(Encoding.UTF8.GetBytes(MinimalSvg))), properties);
            var imgFromBytes = Assert.IsType<CssBoxImage>(Assert.Single(byBytes.RootBox.Boxes));
            Assert.NotNull(imgFromBytes.SvgDocument);
        }

        [Fact]
        public void ContainerSvg_ByteAndStreamOverloads_StripALeadingUtf8Bom()
        {
            // Encoding.UTF8.GetString (used before this test's own fix) leaves a leading BOM as a literal
            // U+FEFF character, which XElement.Parse rejects outright - same bug, same fix, as
            // PdfImage.Resolve's own BOM handling (see ContainerImage_PdfImageFromSvgBytes_...WithBom).
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);
            var bomPrefixedBytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(MinimalSvg)).ToArray();

            var byBytes = DocumentBuilder.BuildPage(page => page.Content(c => c.Svg(bomPrefixedBytes)), properties);
            var imgFromBytes = Assert.IsType<CssBoxImage>(Assert.Single(byBytes.RootBox.Boxes));
            Assert.NotNull(imgFromBytes.SvgDocument);

            var byStream = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Svg(new MemoryStream(bomPrefixedBytes))), properties);
            var imgFromStream = Assert.IsType<CssBoxImage>(Assert.Single(byStream.RootBox.Boxes));
            Assert.NotNull(imgFromStream.SvgDocument);
        }

        [Fact]
        public void ContainerSvg_Stream_NeverClosesTheCallersStream()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MinimalSvg));

            DocumentBuilder.BuildPage(page => page.Content(c => c.Svg(stream)), properties);

            // Every other Stream-accepting overload in this API (Image(Stream), PdfImage.FromStream) reads
            // the caller's stream fully but never disposes it - Svg(Stream) used to differ by wrapping it
            // directly in a StreamReader, whose Dispose (via `using`) closes the underlying stream too.
            Assert.True(stream.CanRead);
        }

        [Fact]
        public async Task ContainerSvg_ReachesTheFragmentTreePaintActuallyConsumes()
        {
            CssBox? svgBox = null;

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    var cb = (ContainerBuilder)c.Width(50).Height(50);
                    cb.Svg(MinimalSvg);
                    svgBox = Assert.Single(cb.Box.Boxes);
                });
            });

            Assert.IsType<CssBoxImage>(svgBox);
            FragmentPaintHarness.FragmentOf(container, svgBox!);
        }

        [Fact]
        public void ContainerImage_PdfImageFromSvgBytes_DetectsSvgAutomatically()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var sharedSvg = PdfImage.FromBytes(Encoding.UTF8.GetBytes(MinimalSvg));
            var descriptor = DocumentBuilder.BuildPage(page => page.Content(c => c.Image(sharedSvg)), properties);
            var imgBox = Assert.IsType<CssBoxImage>(Assert.Single(descriptor.RootBox.Boxes));

            Assert.NotNull(imgBox.SvgDocument);
            Assert.Null(imgBox.Image);
        }

        [Fact]
        public void ContainerImage_PdfImageFromSvgBytes_DetectsSvgPastLeadingWhitespaceAndBom()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var leadingWhitespace = "  \n\t"u8.ToArray();
            var bytes = bom.Concat(leadingWhitespace).Concat(Encoding.UTF8.GetBytes(MinimalSvg)).ToArray();

            var sharedSvg = PdfImage.FromBytes(bytes);
            var descriptor = DocumentBuilder.BuildPage(page => page.Content(c => c.Image(sharedSvg)), properties);
            var imgBox = Assert.IsType<CssBoxImage>(Assert.Single(descriptor.RootBox.Boxes));

            Assert.NotNull(imgBox.SvgDocument);
        }

        [Fact]
        public async Task DynamicImageAndSvg_ReceiveThePdfSizeTheContainerResolvesTo()
        {
            PdfSize? receivedRasterSize = null;
            PdfSize? receivedSvgSize = null;
            CssBox? rasterBox = null;
            CssBox? svgBox = null;

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    c.Column(column =>
                    {
                        var rasterCb = (ContainerBuilder)column.Item().Width(120).Height(80);
                        rasterCb.Image(size =>
                        {
                            receivedRasterSize = size;
                            return MinimalPng;
                        });
                        rasterBox = Assert.Single(rasterCb.Box.Boxes);

                        var svgCb = (ContainerBuilder)column.Item().Width(64).Height(32);
                        svgCb.Svg(size =>
                        {
                            receivedSvgSize = size;
                            return MinimalSvg;
                        });
                        svgBox = Assert.Single(svgCb.Box.Boxes);
                    });
                });
            });

            Assert.NotNull(receivedRasterSize);
            Assert.Equal(120, receivedRasterSize!.Value.Width, precision: 1);
            Assert.Equal(80, receivedRasterSize.Value.Height, precision: 1);
            Assert.NotNull(((CssBoxImage)rasterBox!).Image);

            Assert.NotNull(receivedSvgSize);
            Assert.Equal(64, receivedSvgSize!.Value.Width, precision: 1);
            Assert.Equal(32, receivedSvgSize.Value.Height, precision: 1);
            Assert.NotNull(((CssBoxImage)svgBox!).SvgDocument);

            // Fill-by-default: the dynamic content box itself resolves to its parent's own size - asserted
            // on the real, post-layout resolved replaced-element word (Words[0].Width/Height, the
            // established convention for an inline replaced element - see
            // ReplacedElementIntrinsicSizeTests - since an inline-level box like <img> never commits its
            // own Size.Width/Height the way a block box does; that geometry lives on the word instead),
            // not just that a fragment happened to exist. CssBoxImage.ResolveDynamicContent writes this
            // box's own width/height as absolute points once TryResolveDefiniteSize knows them, rather
            // than leaving them as a percentage - MeasureIntrinsicSize's own percentage-width/height
            // resolution for a replaced element turned out to be independently broken (confirmed against
            // a plain HTML <img style="width:100%"> too - a pre-existing bug outside this PR's own scope,
            // flagged separately) and would otherwise size this box to 0 regardless of this feature.
            Assert.InRange(rasterBox!.Words[0].Width, 119, 121);
            Assert.InRange(rasterBox.Words[0].Height, 79, 81);
            Assert.InRange(svgBox!.Words[0].Width, 63, 65);
            Assert.InRange(svgBox.Words[0].Height, 31, 33);

            FragmentPaintHarness.FragmentOf(container, rasterBox!);
            FragmentPaintHarness.FragmentOf(container, svgBox!);
        }

        [Fact]
        public async Task DynamicImage_CallbackRunsExactlyOnce_EvenAcrossMultipleLayoutPasses()
        {
            var callCount = 0;

            await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    var cb = (ContainerBuilder)c.Width(100).Height(100);
                    cb.Image(_ =>
                    {
                        callCount++;
                        return MinimalPng;
                    });
                });
            });

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task DynamicImage_RowGrowItemWithExplicitHeightOnly_ResolvesTheFlexDistributedWidth()
        {
            // The unverified case the plan flagged: a flex-grow item's own width comes from flex
            // distribution, not a literal declared length or an already-settled ancestor size the way a
            // block containing block's width is. Confirmed empirically here (not assumed) that it still
            // resolves correctly - CSS flex layout settles an item's own main-size (width, in a row) before
            // resolving its cross-size content, the same top-down-width ordering block layout has.
            PdfSize? received = null;

            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(300));
                page.Margin(0);
                page.Content(c =>
                {
                    c.Row(row =>
                    {
                        var growItem = (ContainerBuilder)row.Item().Grow().Height(80);
                        growItem.Image(size =>
                        {
                            received = size;
                            return MinimalPng;
                        });
                    });
                });
            });

            Assert.NotNull(received);
            Assert.Equal(400, received!.Value.Width, precision: 1);
            Assert.Equal(80, received.Value.Height, precision: 1);
        }

        [Fact]
        public async Task DynamicImage_GrowItemWithNoExplicitHeight_StillResolvesAPageAwareFallbackHeight()
        {
            // Verified empirically (not assumed): a bare Grow() row item's own auto cross-size (height)
            // still resolves here, rather than throwing - CssLayoutEngine.GetBoxHeight's own root/page-
            // awareness (an ancestor's auto height is never truly unbounded once it bottoms out at the
            // page's own band height) reaches even a flex item nested this shallowly. The
            // InvalidOperationException path (thrown when TryResolveDefiniteSize genuinely fails) is
            // still real defensive code for a container this test doesn't reach - a deeper/differently
            // nested indefinite ancestor - documented as a real possibility in this method's own doc
            // comment, not asserted against a specific repro here.
            PdfSize? received = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    c.Row(row =>
                    {
                        var growItem = (ContainerBuilder)row.Item().Grow();
                        growItem.Image(size =>
                        {
                            received = size;
                            return MinimalPng;
                        });
                    });
                });
            });

            Assert.NotNull(received);
        }

        [Fact]
        public void DataUri_SniffsMimeTypeFromSignature_ForEachKnownFormat()
        {
            Assert.StartsWith("data:image/png;base64,", DataUri.FromBytes([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0]));
            Assert.StartsWith("data:image/jpeg;base64,", DataUri.FromBytes([0xFF, 0xD8, 0xFF, 0, 0]));
            Assert.StartsWith("data:image/gif;base64,", DataUri.FromBytes([(byte)'G', (byte)'I', (byte)'F', (byte)'8', 0, 0]));
            Assert.StartsWith("data:image/bmp;base64,", DataUri.FromBytes([(byte)'B', (byte)'M']));
            Assert.StartsWith("data:image/webp;base64,", DataUri.FromBytes(
                [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P']));
            Assert.StartsWith("data:image/png;base64,", DataUri.FromBytes([1, 2, 3]));
        }

        // ─── Page descriptor geometry/style setters ─────────────────────────────────────────────────

        [Fact]
        public async Task PageDescriptor_AllGeometryAndStyleSetters_Execute()
        {
            var (root, container) = await BuildAndLayoutPage(page =>
            {
                page.Size(PageSize.Letter);
                page.Orientation(PageOrientation.Portrait);
                page.MarginHorizontal(11);
                page.MarginVertical(13);
                page.MarginTop(14);
                page.MarginBottom(15);
                page.MarginLeft(16);
                page.MarginRight(17);
                page.Background(PdfColor.FromRgb(250, 250, 250));
                page.DefaultTextStyle(style => style.FontSize(11));
                page.Content(c => c.Text("x"));
            });

            Assert.Equal(14, container.MarginTop);
            Assert.Equal(15, container.MarginBottom);
            Assert.Equal(16, container.MarginLeft);
            Assert.Equal(17, container.MarginRight);
            Assert.Equal(250, root.ActualBackgroundColor.R);
        }

        [Fact]
        public void TextSpanContainer_Alignment_AllValuesExecute()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            foreach (TextAlignment alignment in Enum.GetValues<TextAlignment>())
            {
                var descriptor = DocumentBuilder.BuildPage(page =>
                    page.Content(container =>
                    {
                        container.Text(t =>
                        {
                            t.Alignment(alignment);
                            t.Span("x");
                        });
                    }), properties);

                Assert.NotNull(descriptor.RootBox);
            }
        }

        [Fact]
        public async Task TextStyle_RemainingSetters_ReachResolvedBox()
        {
            CssBox? spanBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Text(t =>
                    {
                        var span = t.Span("styled")
                            .Italic()
                            .FontFamily("Arial")
                            .FontFamily("Georgia", "serif")
                            .BackgroundColor(PdfColor.FromRgb(5, 6, 7))
                            .Underline()
                            .Overline()
                            .Strikethrough()
                            .LetterSpacing(2)
                            .WordSpacing(3)
                            .LineHeight(1.5);
                        spanBox = ((TextStyleApplier)span).Box;
                    });
                });
            });

            Assert.NotNull(spanBox);
            Assert.InRange(spanBox!.ActualLetterSpacing, 1.5, 2.5);
            // word-spacing adds to the font's own natural space width rather than replacing it, so the
            // resolved value is larger than the bare 3pt requested - just confirm it moved off the
            // font's zero-additional-spacing default.
            Assert.True(spanBox.ActualWordSpacing > 3, $"expected word-spacing to add at least 3pt, got {spanBox.ActualWordSpacing}");
            Assert.InRange(spanBox.ActualLineHeight, 1.4 * 11, 1.6 * 11 + 5);
            Assert.Equal(5, spanBox.ActualBackgroundColor.R);
        }

        [Fact]
        public async Task TextStyle_SubscriptSuperscriptFontFeatureDirectionBreakAnywhere_ReachResolvedBox()
        {
            CssBox? subBox = null;
            CssBox? superBox = null;
            CssBox? featureBox = null;
            CssBox? rtlBox = null;
            CssBox? breakBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Text(t =>
                    {
                        subBox = ((TextStyleApplier)t.Span("x").Subscript()).Box;
                        superBox = ((TextStyleApplier)t.Span("x").Superscript()).Box;
                        featureBox = ((TextStyleApplier)t.Span("x").FontFeature("liga").FontFeature("smcp", false)).Box;
                        rtlBox = ((TextStyleApplier)t.Span("x").Direction(PdfTextDirection.Rtl)).Box;
                        breakBox = ((TextStyleApplier)t.Span("x").BreakAnywhere()).Box;
                    });
                });
            });

            Assert.Contains("sub", subBox!.VerticalAlign.ToString());
            Assert.Contains("super", superBox!.VerticalAlign.ToString());
            Assert.Contains("liga", featureBox!.FontFeatureSettings.ToString());
            Assert.Contains("smcp", featureBox.FontFeatureSettings.ToString());
            Assert.Contains("rtl", rtlBox!.Direction.ToString());
            Assert.Contains("break-all", breakBox!.WordBreak.ToString());
        }

        [Fact]
        public async Task Direction_Auto_DetectsRtlAndLtrFromEachSpansOwnText()
        {
            CssBox? hebrewBox = null;
            CssBox? latinBox = null;
            CssBox? noStrongCharBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Text(t =>
                    {
                        hebrewBox = ((TextStyleApplier)t.Span("שלום").Direction(PdfTextDirection.Auto)).Box;
                        latinBox = ((TextStyleApplier)t.Span("hello").Direction(PdfTextDirection.Auto)).Box;
                        noStrongCharBox = ((TextStyleApplier)t.Span("123").Direction(PdfTextDirection.Auto)).Box;
                    });
                });
            });

            Assert.Contains("rtl", hebrewBox!.Direction.ToString());
            Assert.Contains("ltr", latinBox!.Direction.ToString());
            // No character with a strong direction (digits are direction-neutral) - defaults to ltr,
            // same as the HTML path's own dir="auto" default.
            Assert.Contains("ltr", noStrongCharBox!.Direction.ToString());
        }

        [Fact]
        public async Task Direction_Auto_SetViaDefaultTextStyleBeforeTextExists_StillResolvesCorrectly()
        {
            // DefaultTextStyle is a decorator, chained ahead of the terminal Text() call that actually
            // supplies the text to scan - regression coverage for the exact ordering problem that rules
            // out resolving Auto eagerly at Direction() call time.
            CssBox? containerBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.DefaultTextStyle(s => s.Direction(PdfTextDirection.Auto));
                    containerBox = cb.Box;
                    cb.Text("שלום");
                });
            });

            Assert.Contains("rtl", containerBox!.Direction.ToString());
        }

        [Fact]
        public async Task Direction_Auto_SkipsAPrecedingSpanWithNoStrongCharacterAndKeepsScanning()
        {
            // Regression coverage for the multi-child scan itself: the first span (digits only) has no
            // character with a strong direction, so the scan must continue past it into the second span
            // rather than stopping (or wrongly defaulting to ltr) as soon as the first one comes up empty.
            CssBox? containerBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.DefaultTextStyle(s => s.Direction(PdfTextDirection.Auto));
                    containerBox = cb.Box;
                    cb.Text(t =>
                    {
                        t.Span("123");
                        t.Span("שלום");
                    });
                });
            });

            Assert.Contains("rtl", containerBox!.Direction.ToString());
        }

        [Fact]
        public async Task TextSpan_DecorationStyleColorThickness_ReachResolvedBox()
        {
            CssBox? spanBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Text(t =>
                    {
                        var span = t.Span("x");
                        span.Underline();
                        span.DecorationStyle(PdfTextDecorationStyle.Wavy)
                            .DecorationColor(PdfColor.FromRgb(9, 9, 9))
                            .DecorationThickness(3);
                        spanBox = ((TextStyleApplier)span).Box;
                    });
                });
            });

            Assert.Contains("wavy", spanBox!.TextDecorationStyle.ToString());
            Assert.Contains("9", spanBox.TextDecorationColor.ToString());
            Assert.Contains("3", spanBox.TextDecorationThickness.ToString());
        }

        [Fact]
        public async Task Container_ShadowBorderLinearGradientParagraphSpacingClampLines_ReachResolvedBox()
        {
            ContainerBuilder? shadowBox = null;
            ContainerBuilder? gradientBorderBox = null;
            ContainerBuilder? paragraphBox = null;
            ContainerBuilder? clampedBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        shadowBox = (ContainerBuilder)column.Item()
                            .Shadow(new PdfBoxShadow(PdfColor.Black, 2, 2, 4));
                        shadowBox.Text("shadowed");

                        gradientBorderBox = (ContainerBuilder)column.Item()
                            .BorderLinearGradient(4, 45, PdfColor.Red, PdfColor.Blue);
                        gradientBorderBox.Text("gradient border");

                        paragraphBox = (ContainerBuilder)column.Item()
                            .ParagraphFirstLineIndentation(12)
                            .ParagraphSpacing(8);
                        paragraphBox.Text("indented paragraph");

                        clampedBox = (ContainerBuilder)column.Item().ClampLines(2);
                        clampedBox.Text("a very long line of text that should be clamped after two lines of wrapping content");
                    });
                });
            });

            Assert.False(string.IsNullOrEmpty(shadowBox!.Box.BoxShadow));

            Assert.Contains("LinearGradient", gradientBorderBox!.Box.BorderImageSource?.GetType().Name ?? "");

            Assert.InRange(paragraphBox!.Box.ActualTextIndent, 11, 13);
            Assert.InRange(paragraphBox.Box.ActualMarginBottom, 7, 9);

            Assert.Contains("2", clampedBox!.Box.LineClamp.ToString());
        }

        [Fact]
        public async Task ClampLines_CustomEllipsis_SetsBlockEllipsis()
        {
            ContainerBuilder? customEllipsisBox = null;
            ContainerBuilder? noEllipsisBox = null;
            ContainerBuilder? defaultEllipsisBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        customEllipsisBox = (ContainerBuilder)column.Item().ClampLines(2, "[more]");
                        customEllipsisBox.Text("some long text that wraps across more than two lines of content");

                        noEllipsisBox = (ContainerBuilder)column.Item().ClampLines(2, "");
                        noEllipsisBox.Text("some long text that wraps across more than two lines of content");

                        defaultEllipsisBox = (ContainerBuilder)column.Item().ClampLines(2);
                        defaultEllipsisBox.Text("some long text that wraps across more than two lines of content");
                    });
                });
            });

            Assert.True(CssValueParser.TryParseSingleString(customEllipsisBox!.Box.BlockEllipsis, out var customText));
            Assert.Equal("[more]", customText);

            Assert.Equal("none", noEllipsisBox!.Box.BlockEllipsis, ignoreCase: true);

            Assert.Equal("auto", defaultEllipsisBox!.Box.BlockEllipsis, ignoreCase: true);
        }

        [Fact]
        public async Task TextSpanContainer_Element_InsertsInlineBlockChild()
        {
            CssBox? elementBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Text(t =>
                    {
                        t.Span("before ");
                        t.Element(c =>
                        {
                            var cb = (ContainerBuilder)c.Width(20).Height(20).Background(PdfColor.Red);
                            elementBox = cb.Box;
                        });
                        t.Span(" after");
                    });
                });
            });

            Assert.NotNull(elementBox);
            Assert.Contains("inline-block", elementBox!.Display.ToString());
            Assert.Equal(255, elementBox.ActualBackgroundColor.R);
        }

        [Fact]
        public void TextAlignment_StartAndEnd_ResolveAgainstDirection()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            foreach (var alignment in new[] { TextAlignment.Start, TextAlignment.End })
            {
                var descriptor = DocumentBuilder.BuildPage(page =>
                    page.Content(container => container.Text(t =>
                    {
                        t.Alignment(alignment);
                        t.Span("x");
                    })), properties);
                Assert.NotNull(descriptor.RootBox);
            }
        }

        // ─── PdfColor / PdfLength unit coverage ─────────────────────────────────────────────────────

        [Fact]
        public void PdfColor_FactoryMethodsAndNamedColors_ProduceExpectedChannelsAndText()
        {
            var argb = PdfColor.FromArgb(128, 10, 20, 30);
            Assert.Equal((byte)128, argb.A);
            Assert.Equal((byte)10, argb.R);
            Assert.Equal((byte)20, argb.G);
            Assert.Equal((byte)30, argb.B);

            var rgb3 = PdfColor.FromHex("#ABC");
            Assert.Equal((byte)0xAA, rgb3.R);
            Assert.Equal((byte)0xBB, rgb3.G);
            Assert.Equal((byte)0xCC, rgb3.B);
            Assert.Equal((byte)255, rgb3.A);

            var rgba4 = PdfColor.FromHex("ABCD");
            Assert.Equal((byte)0xAA, rgba4.R);
            Assert.Equal((byte)0xDD, rgba4.A);

            var rgb6 = PdfColor.FromHex("#112233");
            Assert.Equal((byte)0x11, rgb6.R);
            Assert.Equal((byte)0x22, rgb6.G);
            Assert.Equal((byte)0x33, rgb6.B);
            Assert.Equal((byte)255, rgb6.A);

            var rgba8 = PdfColor.FromHex("11223344");
            Assert.Equal((byte)0x44, rgba8.A);

            Assert.Throws<FormatException>(() => PdfColor.FromHex("12345"));

            Assert.Equal((byte)0, PdfColor.Transparent.A);
            Assert.Equal((byte)0, PdfColor.Black.R);
            Assert.Equal((byte)255, PdfColor.White.R);
            Assert.Equal((byte)128, PdfColor.Gray.R);

            Assert.Equal("rgb(10, 20, 30)", PdfColor.FromRgb(10, 20, 30).ToString());
            Assert.StartsWith("rgba(10, 20, 30,", PdfColor.FromArgb(128, 10, 20, 30).ToString());
        }

        [Fact]
        public void PdfLength_UnitFactoriesAndConversions_ResolveToExpectedPoints()
        {
            PdfLength fromDouble = 12.5;
            Assert.Equal(12.5, fromDouble.Value, 3);

            Assert.Equal(96, PdfLength.Pixels(128).ToPoints(), 0);
            Assert.Equal(144, PdfLength.Inches(2).ToPoints(), 0);
            Assert.InRange(PdfLength.Centimeters(2.54).ToPoints(), 71, 73);
            Assert.InRange(PdfLength.Millimeters(25.4).ToPoints(), 71, 73);
            Assert.Equal(24, PdfLength.Picas(2).ToPoints(), 0);
            Assert.Equal(0, PdfLength.Zero.Value);

            Assert.Throws<InvalidOperationException>(() => PdfLength.Percent(50).ToPoints());
            Assert.Throws<InvalidOperationException>(() => PdfLength.Em(1).ToPoints());
            Assert.Throws<InvalidOperationException>(() => PdfLength.Rem(1).ToPoints());

            Assert.Equal("12pt", ((PdfLength)12).ToString());
            Assert.Equal("50%", PdfLength.Percent(50).ToString());
            Assert.Equal("1em", PdfLength.Em(1).ToString());
            Assert.Equal("1rem", PdfLength.Rem(1).ToString());
        }

        [Fact]
        public void PdfSize_ExposesWidthHeightAndFormatsAsPoints()
        {
            var size = new PdfSize(120, 80);
            Assert.Equal(120, size.Width);
            Assert.Equal(80, size.Height);
            Assert.Equal("120pt x 80pt", size.ToString());
        }

        [Fact]
        public void PdfImage_FromFileAndFromStream_ResolveARealDecodedImage()
        {
            // PdfImage.FromFile's own decode is lazy (deferred to Resolve, since no adapter exists yet at
            // FromFile call time) but the FILE READ now happens eagerly the moment it's placed (inside
            // BuildPage, not at layout/paint time) - unlike Image(string filePath)'s own still-lazy
            // ImageLoadHandler path - so this needs a real file on disk, not just a path string.
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, MinimalPng);

                var fromFile = PdfImage.FromFile(tempFile);
                var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
                var properties = new CssPropertyFactory(adapter);
                var descriptor = DocumentBuilder.BuildPage(page => page.Content(c => c.Image(fromFile)), properties);
                var img = Assert.IsType<CssBoxImage>(Assert.Single(descriptor.RootBox.Boxes));
                Assert.NotNull(img.Image);
                Assert.True(string.IsNullOrEmpty(img.HtmlTag!.TryGetAttribute("src", "")));

                var fromStream = PdfImage.FromStream(new MemoryStream(MinimalPng));
                var streamDescriptor = DocumentBuilder.BuildPage(page => page.Content(c => c.Image(fromStream)), properties);
                var streamImg = Assert.IsType<CssBoxImage>(Assert.Single(streamDescriptor.RootBox.Boxes));
                Assert.NotNull(streamImg.Image);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        // ─── Test harness ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and lays out exactly one declarative page - mirroring <c>PdfGenerator.AddDeclarativePage</c>
        /// up to (but not including) <c>RenderPagesCore</c>'s PDF-writing tail - and returns the resulting
        /// root <see cref="CssBox"/> plus the <see cref="HtmlContainerInt"/> it was laid out in, for
        /// geometry/resolved-style assertions.
        /// </summary>
        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayoutPage(
            Action<IPageDescriptor> pageHandler, PdfGenerateConfig? config = null)
        {
            // Mirrors PdfGenerator.AddPages's own declarative-specific default (A4/20pt margins) -
            // PdfGenerateConfig's bare field defaults are PageSize.Undefined/0pt margins, which would
            // otherwise silently build a 0x0-point page.
            config ??= new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20
            };
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);
            var pageDescriptor = DocumentBuilder.BuildPage(pageHandler, properties);
            PdfGenerator.ResolvePendingAutoDirections(pageDescriptor.RootBox, properties);

            var orgPageSize = pageDescriptor.PageSizeOverride
                ?? (config.PageSize != PageSize.Undefined
                    ? PageSizeConverter.ToSize(config.PageSize)
                    : new XSize(config.ManualPageWidth, config.ManualPageHeight));

            using var container = new HtmlContainer(adapter)
            {
                MarginTop = pageDescriptor.MarginTopOverride ?? config.MarginTop,
                MarginBottom = pageDescriptor.MarginBottomOverride ?? config.MarginBottom,
                MarginLeft = pageDescriptor.MarginLeftOverride ?? config.MarginLeft,
                MarginRight = pageDescriptor.MarginRightOverride ?? config.MarginRight,
                PageSize = orgPageSize
            };
            container.HtmlContainerInt.PageRules = pageDescriptor.PageRules;

            await container.SetDeclarativeRoot(pageDescriptor.RootBox, config.DefaultLanguage);

            var contentPageSize = new XSize(
                orgPageSize.Width - container.MarginLeft - container.MarginRight,
                orgPageSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = contentPageSize;
            container.Location = new XPoint(container.MarginLeft, container.MarginTop);

            using var measure = XGraphics.CreateMeasureContext(orgPageSize, XGraphicsUnit.Point, XPageDirection.Downwards);
            container.MaxSize = new XSize(container.PageSize.Width, 0);
            await container.PerformLayout(measure);

            var containerInt = container.HtmlContainerInt;
            Assert.NotNull(containerInt.Root);
            return (containerInt.Root!, containerInt);
        }

        private static bool LayoutHarnessContains(CssBox root, CssBox target)
        {
            if (ReferenceEquals(root, target)) return true;
            foreach (var child in root.Boxes)
            {
                if (LayoutHarnessContains(child, target)) return true;
            }
            return false;
        }

        private static System.Collections.Generic.IEnumerable<string> CollectFragmentWords(BoxFragment fragment)
        {
            foreach (var word in fragment.Words)
            {
                if (word.Word.Text is { } text) yield return text;
            }

            foreach (var child in fragment.Children)
            {
                foreach (var text in CollectFragmentWords(child))
                {
                    yield return text;
                }
            }
        }

        // ─── Text alignment (a shorthand the registry drops) and table-cell alignment ───────────────

        [Fact]
        public void TextAlignShorthand_IsRejectedByTheRegistry_SoBuildersMustSetTheLonghands()
        {
            // The trap this whole group of tests exists for: CssPropertyFactory.Set reaches the generated,
            // per-longhand CssPropertyRegistry, which returns false for the text-align shorthand - and the
            // builders ignored that, so Alignment(...) aligned nothing. If the registry ever learns the
            // shorthand this flips, and CssPropertyFactory.SetTextAlign can go back to a single Set.
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var parser = new CssValueParser(adapter);
            var parent = DocumentBuilder.BuildPage(page => page.Content(c => c.Text("x")), new CssPropertyFactory(adapter)).RootBox;

            Assert.False(CssPropertyRegistry.TrySet(parser, CssBox.CreateBox(parent, new HtmlTag("div", false)), "text-align", "right"));
            Assert.True(CssPropertyRegistry.TrySet(parser, CssBox.CreateBox(parent, new HtmlTag("div", false)), "text-align-all", "right"));
            Assert.True(CssPropertyRegistry.TrySet(parser, CssBox.CreateBox(parent, new HtmlTag("div", false)), "text-align-last", "auto"));
        }

        [Fact]
        public async Task Alignment_SetsTextAlignAll_AndResetsTextAlignLastToAuto_LikeTheShorthand()
        {
            CssBox? paragraph = null;
            await BuildAndLayoutPage(page => page.Content(c =>
            {
                var container = (ContainerBuilder)c;
                container.Text(t => { t.Alignment(TextAlignment.Right); t.Span("x"); });
                paragraph = container.Box;
            }));

            Assert.Equal(PeachPDF.CSS.HorizontalAlignment.Right, paragraph!.ActualTextAlignAll);
            Assert.Equal(PeachPDF.CSS.TextAlignLast.Auto, paragraph.ActualTextAlignLast);
            // Recorded as builder-set, so a document-level stylesheet's plain rule cannot override it.
            Assert.Contains("text-align-all", paragraph.BuilderSetProperties!);
        }

        [Theory]
        [InlineData(TextAlignment.Left)]
        [InlineData(TextAlignment.Start)]
        [InlineData(TextAlignment.Center)]
        [InlineData(TextAlignment.Right)]
        [InlineData(TextAlignment.End)]
        public async Task TextAlignment_InATableCell_PlacesTheWordAgainstThatEdge(TextAlignment alignment)
        {
            // The reported symptom: every one of these rendered flush-left.
            var (cell, word) = await LayOutCell(cell => cell.Text(t => { t.Alignment(alignment); t.Span("RIGHT"); }));

            AssertWordAligned(cell, word, alignment, padding: 0);
        }

        [Theory]
        [InlineData(TextAlignment.Center)]
        [InlineData(TextAlignment.Right)]
        public async Task TextAlignment_InAPaddedTableCell_RespectsThePadding(TextAlignment alignment)
        {
            var (cell, word) = await LayOutCell(cell =>
                cell.Padding(6).Text(t => { t.Alignment(alignment); t.Span("RIGHT"); }));

            AssertWordAligned(cell, word, alignment, padding: 6);
        }

        [Fact]
        public async Task TextAlignment_InATableCellWithAFixedWidthChild_StillAlignsTheText()
        {
            var (cell, word) = await LayOutCell(cell =>
                cell.Padding(4).Width(PdfLength.Percent(100)).Text(t => { t.Alignment(TextAlignment.Right); t.Span("RIGHT"); }));

            AssertWordAligned(cell, word, TextAlignment.Right, padding: 4);
        }

        [Theory]
        [InlineData("left")]
        [InlineData("center")]
        [InlineData("right")]
        public async Task AlignX_OnATableCell_AlignsTheCellsText(string which)
        {
            // Margins do not apply to a table cell, so the box-positioning Align* methods mean "align the
            // cell's content" there.
            var (cell, word) = await LayOutCell(cell =>
            {
                var aligned = which switch
                {
                    "left" => cell.Padding(4).AlignLeft(),
                    "center" => cell.Padding(4).AlignCenter(),
                    _ => cell.Padding(4).AlignRight(),
                };
                aligned.Text("RIGHT");
            });

            var expected = which switch { "left" => TextAlignment.Left, "center" => TextAlignment.Center, _ => TextAlignment.Right };
            AssertWordAligned(cell, word, expected, padding: 4);
        }

        [Fact]
        public async Task AlignRight_OnAHeaderCell_AlignsTheHeaderText()
        {
            CssBox? headerCell = null;
            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Table(table =>
                {
                    table.Columns(cols => { cols.RelativeColumn(1); cols.RelativeColumn(1); });
                    table.Header(header =>
                    {
                        header.Cell().Text("Item");
                        var amount = (ContainerBuilder)header.Cell();
                        amount.AlignRight().Text("Amount");
                        headerCell = amount.Box;
                    });
                    table.Row(row =>
                    {
                        row.Cell().Text("Bread");
                        row.Cell().AlignRight().Text("3.20");
                    });
                }));
            });

            var word = LayoutHarnessDescendants(headerCell!).SelectMany(box => box.Words).Single();
            Assert.True(headerCell!.ActualRight - word.Right < 1.0, $"header word ends at {word.Right}, cell at {headerCell.ActualRight}");

            // And the body cell in the same column lines up with it - the reason to right-align amounts.
            var bodyWord = LayoutHarnessDescendants(root).Where(box => box.Words.Count > 0).Select(box => box.Words[0]).Single(w => (w.Text ?? "").StartsWith("3."));
            Assert.Equal(word.Right, bodyWord.Right, 1.0);
        }

        [Fact]
        public async Task ANeighbouringCellsAlignment_DoesNotLeakIntoTheOthers()
        {
            CssBox? left = null;
            CssBox? right = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Table(table =>
                {
                    table.Columns(cols => { cols.RelativeColumn(1); cols.RelativeColumn(1); });
                    table.Row(row =>
                    {
                        var l = (ContainerBuilder)row.Cell();
                        l.Text("left");
                        left = l.Box;
                        var r = (ContainerBuilder)row.Cell();
                        r.AlignRight().Text("right");
                        right = r.Box;
                    });
                }));
            });

            var leftWord = LayoutHarnessDescendants(left!).SelectMany(box => box.Words).Single();
            Assert.Equal(left!.Location.X, leftWord.Left, 1.0);
            Assert.Equal(PeachPDF.CSS.HorizontalAlignment.Right, right!.ActualTextAlignAll);
            Assert.Equal(PeachPDF.CSS.HorizontalAlignment.Start, left.ActualTextAlignAll);
        }

        [Fact]
        public async Task AlignRight_OnARowItem_StillPositionsTheContainerWithAutoMargins()
        {
            // Regression guard: the table-cell branch must not change what the Align methods do elsewhere.
            // On a row (main-axis) item the auto left margin absorbs the free space and pushes the box right.
            CssBox? item = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Row(row =>
                {
                    var b = (ContainerBuilder)row.Item();
                    b.Width(100).AlignRight().Text("x");
                    item = b.Box;
                }));
            });

            Assert.InRange(item!.Location.X, 299, 301);
            // A plain box alignment, not a text alignment: the text inside keeps its default.
            Assert.Equal(PeachPDF.CSS.HorizontalAlignment.Start, item.ActualTextAlignAll);
        }

        [Theory]
        [InlineData("left", 0)]
        [InlineData("center", 150)]
        [InlineData("right", 300)]
        public async Task AlignX_OnAColumnItem_PositionsTheContainerAcrossTheColumn(string which, double expectedLeft)
        {
            // The reported case: a column's cross axis is horizontal, so this needs cross-axis auto margins.
            CssBox? item = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Column(column =>
                {
                    var b = (ContainerBuilder)column.Item();
                    var sized = b.Width(100);
                    var aligned = which switch { "left" => sized.AlignLeft(), "center" => sized.AlignCenter(), _ => sized.AlignRight() };
                    aligned.Text("x");
                    item = b.Box;
                }));
            });

            Assert.Equal(expectedLeft, item!.Location.X, 0.5);
            Assert.Equal(100, item.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task AlignRight_OnAnAutoWidthColumnItem_ShrinksItToItsContentAndPushesItToTheEdge()
        {
            // `column.Item().AlignRight().Text("x")` with no Width at all - the item has to be fit-content,
            // not column-wide, for the auto margin to have anything to absorb.
            CssBox? item = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Column(column =>
                {
                    var b = (ContainerBuilder)column.Item();
                    b.AlignRight().Text("x");
                    item = b.Box;
                }));
            });

            Assert.True(item!.ActualBoxSizingWidth < 50, $"item should be as wide as its text, was {item.ActualBoxSizingWidth}");
            Assert.Equal(400, item.ActualRight, 0.5);
        }

        [Fact]
        public async Task AlignRight_OnAColumnItem_InsideATableCell_AlignsTheItemToTheCellsRightEdge()
        {
            // The report's fifth variant: r.Cell().Column(col => col.Item().AlignRight().Text("x")).
            CssBox? item = null;
            CssBox? cell = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Table(table =>
                {
                    table.Columns(cols => { cols.RelativeColumn(1); cols.RelativeColumn(1); });
                    table.Row(row =>
                    {
                        row.Cell().Text("label");
                        var second = (ContainerBuilder)row.Cell();
                        second.Column(col =>
                        {
                            var b = (ContainerBuilder)col.Item();
                            b.AlignRight().Text("x");
                            item = b.Box;
                        });
                        cell = second.Box;
                    });
                }));
            });

            Assert.Equal(cell!.ActualRight, item!.ActualRight, 1.0);
            Assert.True(item.Location.X > cell.Location.X + 100, "the item must sit at the right of its 200pt cell");
        }

        [Fact]
        public async Task TextAlignment_OutsideATableCell_AlignsTheParagraph()
        {
            CssBox? paragraph = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c =>
                {
                    var b = (ContainerBuilder)c;
                    b.Text(t => { t.Alignment(TextAlignment.Right); t.Span("RIGHT"); });
                    paragraph = b.Box;
                });
            });

            var word = LayoutHarnessDescendants(paragraph!).SelectMany(box => box.Words).Single();
            Assert.True(paragraph!.ActualRight - word.Right < 1.0, $"word ends at {word.Right}, paragraph at {paragraph.ActualRight}");
        }

        /// <summary>Lays out a two-column, 200pt-per-column table whose second cell is filled by <paramref name="fill"/>.</summary>
        private static async Task<(CssBox cell, CssRect word)> LayOutCell(Action<IContainer> fill)
        {
            CssBox? cell = null;
            await BuildAndLayoutPage(page =>
            {
                page.Size(PdfLength.Points(400), PdfLength.Points(200));
                page.Margin(0);
                page.Content(c => c.Table(table =>
                {
                    table.Columns(cols => { cols.RelativeColumn(1); cols.RelativeColumn(1); });
                    table.Row(row =>
                    {
                        row.Cell().Text("label");
                        var second = (ContainerBuilder)row.Cell();
                        fill(second);
                        cell = second.Box;
                    });
                }));
            });

            var word = LayoutHarnessDescendants(cell!).SelectMany(box => box.Words).Single();
            return (cell!, word);
        }

        private static void AssertWordAligned(CssBox cell, CssRect word, TextAlignment alignment, double padding)
        {
            var contentLeft = cell.Location.X + padding;
            var contentRight = cell.ActualRight - padding;
            const double tolerance = 1.0;

            switch (alignment)
            {
                case TextAlignment.Left:
                case TextAlignment.Start:
                    Assert.Equal(contentLeft, word.Left, tolerance);
                    break;
                case TextAlignment.Center:
                    Assert.Equal((contentLeft + contentRight) / 2, (word.Left + word.Right) / 2, tolerance);
                    Assert.True(word.Left > contentLeft + 10, "a centered word must not sit against the left edge");
                    break;
                default:
                    Assert.Equal(contentRight, word.Right, tolerance);
                    Assert.True(word.Left > contentLeft + 10, "a right-aligned word must not sit against the left edge");
                    break;
            }
        }

        private static System.Collections.Generic.IEnumerable<CssBox> LayoutHarnessDescendants(CssBox root)
        {
            yield return root;
            foreach (var child in root.Boxes)
            {
                foreach (var descendant in LayoutHarnessDescendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
