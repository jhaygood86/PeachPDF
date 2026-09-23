using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Layout;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-assertion tests for the declarative document-building API's newer surface: <c>Class</c>/<c>Id</c>/
    /// <c>Tag</c>/<c>PageName</c>, a document-level <see cref="IDocumentBuilder.Stylesheet"/> (selector matching,
    /// builder-vs-stylesheet precedence, and the <c>@font-face</c>/<c>@property</c>/<c>@page</c> at-rules it can
    /// carry), <see cref="PeachPdfCssContent"/> from <see cref="ReadOnlyMemory{T}"/>, and <c>IContainer.Html(...)</c>
    /// fragment splicing with slot callbacks. Uses the same <see cref="HtmlContainerInt"/>-driven harness as
    /// <c>DeclarativeApiIntegrationTests</c> so assertions land on real post-layout <see cref="CssBox"/> state.
    /// </summary>
    public class DeclarativeApiStylesheetAndHtmlIntegrationTests
    {
        // ─── Class / Id / Tag - inert without a stylesheet ─────────────────────────────────────────────

        [Fact]
        public async Task ClassIdTag_WithNoStylesheetAttached_AreInertMetadataOnly()
        {
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("card").Id("hero").Tag("li").Padding(20);
                    box = cb.Box;
                });
            });

            Assert.NotNull(box);
            Assert.Equal("card", box!.HtmlTag!.TryGetAttribute("class", ""));
            Assert.Equal("hero", box.HtmlTag.TryGetAttribute("id", ""));
            Assert.Equal("li", box.HtmlTag.Name, ignoreCase: true);
            // No stylesheet was attached, so no selector rule could possibly have applied - the explicit
            // Padding(20) call is the only thing that determined this value.
            Assert.InRange(box.ActualPaddingTop, 19, 21);
        }

        [Fact]
        public async Task Class_CalledTwice_Accumulates_IdCalledTwice_Replaces()
        {
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("a").Class("b").Id("first").Id("second");
                    box = cb.Box;
                });
            });

            Assert.Equal("a b", box!.HtmlTag!.TryGetAttribute("class", ""));
            Assert.Equal("second", box.HtmlTag.TryGetAttribute("id", ""));
        }

        [Fact]
        public async Task Class_OnThePageContentRoot_WorksDespiteNoDefaultHtmlTag()
        {
            // The page's own content root (PageDescriptorBuilder.Content) is the one declarative box built
            // with a null HtmlTag - EnsureHtmlTag's own lazy-attach path is what this exercises.
            CssBox? rootBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("page-root");
                    rootBox = cb.Box;
                    cb.Text("x");
                });
            });

            Assert.Equal("page-root", rootBox!.HtmlTag!.TryGetAttribute("class", ""));
        }

        // ─── Stylesheet: selector matching + precedence ────────────────────────────────────────────────

        [Fact]
        public async Task StylesheetClassSelector_AppliesAPropertyTheBuilderNeverTouched()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(".card { border-top-color: rgb(9, 8, 7); border-top-width: 2px; border-top-style: solid; }");
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("card");
                    box = cb.Box;
                });
            }, stylesheet);

            Assert.Equal(9, box!.ActualBorderTopColor.R);
            Assert.Equal(8, box.ActualBorderTopColor.G);
            Assert.Equal(7, box.ActualBorderTopColor.B);
        }

        [Fact]
        public async Task BuilderSetProperty_WinsOverNonImportantStylesheetRule()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(".card { padding-top: 8px; }");
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("card").PaddingTop(20);
                    box = cb.Box;
                });
            }, stylesheet);

            // The explicit builder call must win, exactly as an inline style="" would over an author rule.
            Assert.InRange(box!.ActualPaddingTop, 19, 21);
        }

        [Fact]
        public async Task ImportantStylesheetRule_WinsOverBuilderSetProperty()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(".card { padding-top: 8pt !important; }");
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("card").PaddingTop(20);
                    box = cb.Box;
                });
            }, stylesheet);

            Assert.InRange(box!.ActualPaddingTop, 7, 9);
        }

        [Fact]
        public async Task DescendantSelector_MatchesAcrossDeclarativelyBuiltAncestorAndChild()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(".parent .child { color: rgb(1, 2, 3); }");
            CssBox? childTextBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var parent = (ContainerBuilder)container.Class("parent");
                    parent.Column(column =>
                    {
                        var child = (ContainerBuilder)column.Item().Class("child");
                        child.Text("hi");
                        childTextBox = child.Box;
                    });
                });
            }, stylesheet);

            Assert.Equal("rgb(1, 2, 3)", childTextBox!.Color);
        }

        [Fact]
        public async Task Tag_ChangesWhichBareTypeSelectorMatches()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("li { color: rgb(4, 5, 6); }");
            CssBox? taggedBox = null;
            CssBox? untaggedBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        var tagged = (ContainerBuilder)column.Item().Tag("li");
                        tagged.Text("a");
                        taggedBox = tagged.Box;

                        var untagged = (ContainerBuilder)column.Item();
                        untagged.Text("b");
                        untaggedBox = untagged.Box;
                    });
                });
            }, stylesheet);

            Assert.Equal("rgb(4, 5, 6)", taggedBox!.Color);
            Assert.NotEqual("rgb(4, 5, 6)", untaggedBox!.Color);
        }

        [Fact]
        public async Task DocumentStylesheet_ThreadedThroughRealCreateDocumentPipeline_ProducesANonEmptyDocument()
        {
            // Wiring smoke test for the public entry point (IDocumentBuilder.Stylesheet -> DocumentBuilder ->
            // PdfGenerator.AddPages -> AddDeclarativePage), as opposed to the tests above, which call the
            // same underlying DomParser logic directly via the lower-level BuildAndLayoutPage harness.
            var generator = new PdfGenerator();
            var stylesheet = await generator.ParseStyleSheet(".card { background-color: rgb(200, 200, 200); }");

            var document = await generator.CreateDocument(doc =>
            {
                doc.Stylesheet(stylesheet);
                doc.Page(page =>
                {
                    page.Content(container => container.Class("card").Text("Hello"));
                });
            });

            using var ms = new System.IO.MemoryStream();
            document.Save(ms);
            Assert.True(ms.Length > 0);
        }

        [Fact]
        public async Task DocumentStylesheet_AtPageSize_ThroughRealCreateDocumentPipeline_ResizesTheActualPdfPage()
        {
            // Exercises PdfGenerator.AddDeclarativePage's own real "orgPageSize = container.CssPageSize.Value"
            // sync (Track A), not just the test harness's own parallel reimplementation of it - the page
            // builder never calls Size(...), so the stylesheet's base @page rule is what must resize the
            // actual physical PDF page produced by the real pipeline.
            var generator = new PdfGenerator();
            var stylesheet = await generator.ParseStyleSheet("@page { size: 300pt 500pt; }");

            var document = await generator.CreateDocument(doc =>
            {
                doc.Stylesheet(stylesheet);
                doc.Page(page => page.Content(c => c.Text("Hello")));
            });

            Assert.Equal(300, document.PdfDocument.Pages[0].Width.Point, 1);
            Assert.Equal(500, document.PdfDocument.Pages[0].Height.Point, 1);
        }

        [Fact]
        public async Task DocumentStylesheet_FontFeatureValues_ResolvesThroughTheDeclarativeApi()
        {
            // Regression: HtmlContainerInt.SetDeclarativeRoot (the declarative-API parse path) must build
            // the @font-feature-values registry the same way the HTML-string parse path (DomParser) does
            // - it previously rebuilt FontPaletteValues but not FontFeatureValues, so styleset()/etc.
            // silently resolved to nothing on this path.
            var generator = new PdfGenerator();
            var stylesheet = await generator.ParseStyleSheet(
                "@font-feature-values Arial { @styleset { nice-style: 1; } } " +
                ".alt { font-family: Arial; font-variant-alternates: styleset(nice-style); }");

            CssBox? box = null;
            await BuildAndLayoutPage(page =>
            {
                page.Content(c =>
                {
                    var cb = (ContainerBuilder)c.Class("alt");
                    cb.Text("hello");
                    box = cb.Box;
                });
            }, stylesheet);

            Assert.NotNull(box);
            Assert.Equal([("ss01", 1)], box!.ActualFontVariantAlternates);
        }

        [Fact]
        public async Task PeachPdfCssContent_AddStyleSheetReadOnlyMemory_EmptyMemory_IsANoOp()
        {
            var generator = new PdfGenerator();
            var content = await generator.ParseStyleSheet(".a { color: rgb(1, 1, 1); }");

            await content.AddStyleSheet(ReadOnlyMemory<char>.Empty);

            CssBox? aBox = null;
            await BuildAndLayoutPage(page => page.Content(c =>
            {
                var cb = (ContainerBuilder)c.Class("a");
                cb.Text("a");
                aBox = cb.Box;
            }), content);

            // Still just the original rule - an empty memory added nothing and, more importantly, didn't throw.
            Assert.Equal("rgb(1, 1, 1)", aBox!.Color);
        }

        // ─── @page: Track B merge - branches with only one side present ───────────────────────────────────

        [Fact]
        public async Task AtPageBaseRule_WithNoHeaderFooter_StillMergesItsOwnMarginBoxDeclarations()
        {
            // declarativeBase is null (no Header/Footer) but stylesheetBase is not, AND it declares its own
            // margin box - exercises BuildDeclarativePageRules' "only a stylesheet base" branch's own
            // margin-box copy loop, not just its page-level Style copy.
            var stylesheet = await new PdfGenerator().ParseStyleSheet(
                """@page { margin: 15pt; @top-center { content: "Confidential"; } }""");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Text("x"));
            }, stylesheet);

            var margins = PageRuleResolver.SelectApplicableMarginRules(container.PageRules, pageNumber: 1, activeNamedPage: null);
            Assert.Contains(margins, m => m.Selector!.Text!.Contains("top-center", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(15, container.MarginTop, 1);
        }

        [Fact]
        public async Task HeaderMarginBoxContent_SurvivesWhenTheStylesheetsOwnPageRulesAreAllNamed_NoBaseRule()
        {
            // declarativeBase is not null (Header used) but stylesheetBase IS null (the stylesheet's only
            // @page rule is named, not a base rule) - exercises BuildDeclarativePageRules' "only a
            // declarative base" branch.
            var stylesheet = await new PdfGenerator().ParseStyleSheet("@page chapter { size: 400pt 600pt; }");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Header(h => h.Text("HEADER"));
                page.Content(c => c.Text("x"));
            }, stylesheet);

            var margins = PageRuleResolver.SelectApplicableMarginRules(container.PageRules, pageNumber: 1, activeNamedPage: null);
            Assert.Contains(margins, m => m.Selector!.Text!.Contains("top-center", StringComparison.OrdinalIgnoreCase));
        }

        // ─── @property / var() ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task AtProperty_RegisteredInitialValue_ResolvesViaVarWhenNeverSet()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(
                """@property --c { syntax: "<color>"; inherits: false; initial-value: blue; } .box { color: var(--c); }""");
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.Class("box");
                    box = cb.Box;
                });
            }, stylesheet);

            Assert.Equal("rgb(0, 0, 255)", box!.Color);
        }

        // ─── @font-face (smoke only - see CLAUDE.md's FontFactory-adjacent-test caution) ───────────────

        [Fact]
        public async Task AtFontFace_InDocumentStylesheet_DoesNotThrowAndStillLaysOut()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(
                """@font-face { font-family: "DeclarativeTestFace"; src: local("Arial"); }""");

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(container => container.Text("x"));
            }, stylesheet);

            Assert.NotNull(root);
        }

        // ─── @page: Track A (whole-document base geometry) ─────────────────────────────────────────────

        [Fact]
        public async Task AtPageBaseRule_FillsMarginThePageBuilderLeftUnset()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("@page { margin: 50pt; }");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Text("x"));
            }, stylesheet);

            Assert.Equal(50, container.MarginTop, 1);
            Assert.Equal(50, container.MarginLeft, 1);
        }

        [Fact]
        public async Task AtPageBaseRule_DoesNotOverrideAnEdgeThePageBuilderSetExplicitly()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("@page { margin: 50pt; }");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.MarginTop(10);
                page.Content(c => c.Text("x"));
            }, stylesheet);

            Assert.Equal(10, container.MarginTop, 1);
            // The other three edges were left unset by the page builder, so the stylesheet still fills them.
            Assert.Equal(50, container.MarginLeft, 1);
        }

        [Fact]
        public async Task AtPageBaseRule_SizeFillsUnsetPageSize()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("@page { size: 300pt 500pt; }");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Text("x"));
            }, stylesheet);

            Assert.NotNull(container.CssPageSize);
            Assert.Equal(300, container.CssPageSize!.Value.Width, 1);
            Assert.Equal(500, container.CssPageSize.Value.Height, 1);
        }

        // ─── @page: Track B (header/footer margin-box content survives alongside a stylesheet base rule) ─

        [Fact]
        public async Task AtPageBaseRule_DoesNotSilentlyDropHeaderFooterMarginBoxContent()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("@page { margin: 30pt; }");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Header(h => h.Text("HEADER"));
                page.Content(c => c.Text("x"));
            }, stylesheet);

            // The merge (DomParser.BuildDeclarativePageRules) must combine the header's own synthesized
            // base rule (its @top-center margin box) with the stylesheet's own base rule (its margin
            // declaration), rather than one silently discarding the other's declarations.
            var margins = PageRuleResolver.SelectApplicableMarginRules(container.PageRules, pageNumber: 1, activeNamedPage: null);
            Assert.Contains(margins, m => m.Selector!.Text!.Contains("top-center", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(30, container.MarginTop, 1);
        }

        // ─── PageName ───────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task PageName_SetsTheCssPagePropertyOnTheBox()
        {
            CssBox? box = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    var cb = (ContainerBuilder)container.PageName("chapter");
                    box = cb.Box;
                    cb.Text("x");
                });
            });

            Assert.Equal("chapter", box!.PageName);
        }

        [Fact]
        public async Task PageName_CombinedWithNamedAtPageRule_IsSelectableByPageRuleResolver()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("@page chapter { size: 400pt 600pt; }");

            var (_, container) = await BuildAndLayoutPage(page =>
            {
                page.Content(c => c.Text("x"));
            }, stylesheet);

            var rule = PageRuleResolver.SelectPageRule(container.PageRules, pageNumber: 1, activeNamedPage: "chapter");
            Assert.NotNull(rule);
            Assert.Equal("400pt 600pt", rule!.Style.Size);
        }

        // ─── ReadOnlyMemory<char> CSS parsing ──────────────────────────────────────────────────────────

        [Fact]
        public async Task PeachPdfCssContent_FromReadOnlyMemory_MatchesStringOverload()
        {
            const string css = ".x { color: rgb(10, 20, 30); }";
            var generator = new PdfGenerator();

            var fromString = await generator.ParseStyleSheet(css);
            var fromMemory = await generator.ParseStyleSheet(css.AsMemory());

            CssBox? boxFromString = null;
            CssBox? boxFromMemory = null;

            await BuildAndLayoutPage(page => page.Content(c => { var cb = (ContainerBuilder)c.Class("x"); boxFromString = cb.Box; }), fromString);
            await BuildAndLayoutPage(page => page.Content(c => { var cb = (ContainerBuilder)c.Class("x"); boxFromMemory = cb.Box; }), fromMemory);

            Assert.Equal(boxFromString!.ActualColor.R, boxFromMemory!.ActualColor.R);
            Assert.Equal(boxFromString.ActualColor.G, boxFromMemory.ActualColor.G);
            Assert.Equal(boxFromString.ActualColor.B, boxFromMemory.ActualColor.B);
            Assert.Equal(10, boxFromMemory.ActualColor.R);
        }

        [Fact]
        public async Task PeachPdfCssContent_AddStyleSheetReadOnlyMemory_MergesIntoExistingContent()
        {
            var generator = new PdfGenerator();
            var content = await generator.ParseStyleSheet(".a { color: rgb(1, 1, 1); }");
            await content.AddStyleSheet(".b { color: rgb(2, 2, 2); }".AsMemory());

            CssBox? aBox = null;
            CssBox? bBox = null;

            await BuildAndLayoutPage(page => page.Content(c => c.Column(col =>
            {
                var a = (ContainerBuilder)col.Item().Class("a");
                a.Text("a");
                aBox = a.Box;
                var b = (ContainerBuilder)col.Item().Class("b");
                b.Text("b");
                bBox = b.Box;
            })), content);

            Assert.Equal("rgb(1, 1, 1)", aBox!.Color);
            Assert.Equal("rgb(2, 2, 2)", bBox!.Color);
        }

        [Fact]
        public async Task CssData_ParseReadOnlyMemory_ProducesEquivalentDataToStringOverload()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            const string css = ".y { color: red; }";

            var fromString = await CssData.Parse(adapter, css);
            var fromMemory = await CssData.Parse(adapter, css.AsMemory());

            Assert.Equal(fromString.Stylesheets.Count, fromMemory.Stylesheets.Count);
        }

        // ─── IContainer.Html(...) fragment insertion ───────────────────────────────────────────────────

        [Fact]
        public async Task Html_MultipleTopLevelSiblings_AllBecomeChildrenOfOneWrapper()
        {
            ContainerBuilder? cb = null;

            var (root, _) = await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<div>First</div><span>Second</span>");
                });
            });

            var wrapper = Assert.Single(cb!.Box.Boxes);
            Assert.Equal(DisplayMode.Block, wrapper.Display.Value);
            Assert.Equal(2, wrapper.Boxes.Count);
            Assert.True(LayoutHarnessContains(root, wrapper));
        }

        [Fact]
        public async Task Html_TableFragment_GetsUaDefaultTableDisplayAndAnonymousTableCorrection()
        {
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<table><tr><td>A</td><td>B</td></tr></table>");
                });
            });

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var table = Assert.Single(wrapper.Boxes);
            Assert.Equal(DisplayMode.Table, table.Display.Value);
        }

        [Fact]
        public async Task Html_OwnStyleTag_CascadesAgainstTheFragment()
        {
            ContainerBuilder? cb = null;
            CssBox? divBox = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<style>.x { color: rgb(7, 8, 9); }</style><div class=\"x\">hi</div>");
                });
            });

            var wrapper = Assert.Single(cb!.Box.Boxes);
            divBox = wrapper.Boxes.Single(b => b.HtmlTag?.Name.Equals("div", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Equal("rgb(7, 8, 9)", divBox.Color);
        }

        [Fact]
        public async Task Html_WithCallerSuppliedStylesheet_CascadesAgainstIt()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet(".y { color: rgb(11, 12, 13); }");
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<div class=\"y\">hi</div>", stylesheet);
                });
            });

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            Assert.Equal("rgb(11, 12, 13)", div.Color);
        }

        [Fact]
        public async Task Html_FragmentBoxes_AreFlaggedIsFragmentStyled_AndSkippedByTheWholeTreeStylesheetPass()
        {
            // A document-level stylesheet rule that would otherwise match the fragment's own div must NOT
            // override what the fragment's own (higher-specificity) rule already resolved - proves
            // IsFragmentStyled actually protects fragment content from the whole-tree pass.
            var documentStylesheet = await new PdfGenerator().ParseStyleSheet("div { color: rgb(255, 0, 0); }");
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<style>div { color: rgb(0, 255, 0); }</style><div>hi</div>");
                });
            }, documentStylesheet);

            var wrapper = Assert.Single(cb!.Box.Boxes);
            // The wrapper itself is the declarative Html() call's own anonymous box, built directly - only
            // the fragment's own parsed content (its descendants) are flagged.
            Assert.False(wrapper.IsFragmentStyled);
            var div = wrapper.Boxes.Single(b => b.HtmlTag?.Name.Equals("div", StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(div.IsFragmentStyled);
            Assert.Equal("rgb(0, 255, 0)", div.Color);
        }

        [Fact]
        public async Task Html_Bytes_And_Stream_OverloadsProduceTheSameStructureAsString()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);
            const string html = "<div>x</div>";

            var byBytes = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Html(System.Text.Encoding.UTF8.GetBytes(html))), properties);
            var byStream = DocumentBuilder.BuildPage(
                page => page.Content(c => c.Html(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)))), properties);

            Assert.Single(Assert.Single(byBytes.RootBox.Boxes).Boxes);
            Assert.Single(Assert.Single(byStream.RootBox.Boxes).Boxes);
        }

        [Fact]
        public void Html_IsTerminal_SecondCallThrows()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new CssPropertyFactory(adapter);

            DocumentBuilder.BuildPage(page =>
                page.Content(c =>
                {
                    c.Html("<div>first</div>");
                    Assert.Throws<InvalidOperationException>(() => c.Html("<div>second</div>"));
                }), properties);
        }

        // ─── Slot callbacks ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Slot_FilledByCallback_ReplacesFallbackContent()
        {
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<div><slot name=\"greeting\">Fallback</slot></div>", onSlot: (slot, slotContainer) =>
                    {
                        Assert.Equal("greeting", slot.Name);
                        Assert.Equal("greeting", slot.Attributes["name"]);
                        slotContainer.Text("Filled!");
                    });
                });
            });

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            var replacement = Assert.Single(div.Boxes);
            var textBox = Assert.Single(replacement.Boxes);
            Assert.Equal("Filled!", textBox.Text);
        }

        [Fact]
        public async Task Slot_NoCallbackProvided_KeepsFallbackContent()
        {
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<div><slot>Fallback text</slot></div>");
                });
            });

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            var slotBox = Assert.Single(div.Boxes);
            Assert.Equal("slot", slotBox.HtmlTag!.Name, ignoreCase: true);
            Assert.Equal("Fallback text", slotBox.Boxes.Single().Text);
        }

        [Fact]
        public async Task Slot_CallbackDeclinesToAct_KeepsFallbackContent()
        {
            ContainerBuilder? cb = null;
            var callbackInvoked = false;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<div><slot name=\"x\">Fallback text</slot></div>", onSlot: (_, _) =>
                    {
                        callbackInvoked = true;
                        // Deliberately places no content.
                    });
                });
            });

            Assert.True(callbackInvoked);
            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            var slotBox = Assert.Single(div.Boxes);
            Assert.Equal("Fallback text", slotBox.Boxes.Single().Text);
        }

        [Fact]
        public async Task Slot_MultipleSlots_EachInvokedIndependently()
        {
            ContainerBuilder? cb = null;
            var seenNames = new System.Collections.Generic.List<string>();

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html(
                        "<div><slot name=\"one\">1</slot><slot name=\"two\">2</slot></div>",
                        onSlot: (slot, slotContainer) =>
                        {
                            seenNames.Add(slot.Name);
                            slotContainer.Text($"filled-{slot.Name}");
                        });
                });
            });

            Assert.Equal(new[] { "one", "two" }, seenNames);

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            Assert.Equal(2, div.Boxes.Count);
            Assert.Equal("filled-one", div.Boxes[0].Boxes.Single().Text);
            Assert.Equal("filled-two", div.Boxes[1].Boxes.Single().Text);
        }

        [Fact]
        public async Task Slot_ReplacementContent_ParticipatesNormallyInTheWholeTreeStylesheetPass()
        {
            // Confirms the IsFragmentStyled per-box-not-subtree-skip design: slot-replacement content is
            // built via ordinary ContainerBuilder calls, so it should be reachable and stylable by the
            // document-level stylesheet exactly like any other declarative content.
            var stylesheet = await new PdfGenerator().ParseStyleSheet(".filled { color: rgb(20, 30, 40); }");
            ContainerBuilder? cb = null;

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html("<div><slot>fallback</slot></div>", onSlot: (_, slotContainer) =>
                    {
                        var slotBuilder = (ContainerBuilder)slotContainer.Class("filled");
                        slotBuilder.Text("filled");
                    });
                });
            }, stylesheet);

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            var replacement = Assert.Single(div.Boxes);
            Assert.False(replacement.IsFragmentStyled);
            Assert.Equal("rgb(20, 30, 40)", replacement.Color);
        }

        [Fact]
        public async Task Slot_NestedInsideAnAncestorSlotsFallback_CallbackNotInvokedOnceTheAncestorIsFilled()
        {
            // Regression: slots are all collected up front, in one pass, before any of them are filled - so
            // a <slot> nested inside an earlier sibling slot's own fallback content is still in that
            // collected list even after the ancestor slot's own fill detaches the whole fallback subtree
            // (including the nested slot) from the live tree. Its callback must NOT fire in that case - the
            // content it would place can never reach the final tree either way, so invoking it would just be
            // a spurious side effect.
            ContainerBuilder? cb = null;
            var invokedNames = new System.Collections.Generic.List<string>();

            await BuildAndLayoutPage(page =>
            {
                page.Content(container =>
                {
                    cb = (ContainerBuilder)container;
                    cb.Html(
                        "<div><slot name=\"outer\"><slot name=\"inner\">inner fallback</slot></slot></div>",
                        onSlot: (slot, slotContainer) =>
                        {
                            invokedNames.Add(slot.Name);
                            if (slot.Name == "outer")
                            {
                                slotContainer.Text("outer filled");
                            }
                        });
                });
            });

            // The inner slot lived only inside the outer slot's own fallback content, which was discarded
            // the moment the outer slot was filled - its callback must never have run.
            Assert.Equal(new[] { "outer" }, invokedNames);

            var wrapper = Assert.Single(cb!.Box.Boxes);
            var div = Assert.Single(wrapper.Boxes);
            var replacement = Assert.Single(div.Boxes);
            Assert.Equal("outer filled", replacement.Boxes.Single().Text);
        }

        // ─── Test harness ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and lays out exactly one declarative page, mirroring <c>PdfGenerator.AddDeclarativePage</c>
        /// up to (but not including) PDF rendering - including its stylesheet handling (<see cref="HtmlContainer.SetDeclarativeRoot"/>
        /// with a stylesheet, <see cref="DomParser.BuildDeclarativePageRules"/>, <see cref="DomParser.CascadeApplyPageStyles"/>)
        /// - so these tests exercise the same logic <c>AddDeclarativePage</c> (a private method) does,
        /// the same way <c>DeclarativeApiIntegrationTests.BuildAndLayoutPage</c> does for the pre-existing
        /// surface.
        /// </summary>
        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayoutPage(
            Action<IPageDescriptor> pageHandler, PeachPdfCssContent? stylesheet = null, PdfGenerateConfig? config = null)
        {
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
            container.HtmlContainerInt.PageRules = DomParser.BuildDeclarativePageRules(
                pageDescriptor.PageRules, stylesheet?.CssData,
                marginLeftIsExplicit: pageDescriptor.MarginLeftOverride is not null,
                marginTopIsExplicit: pageDescriptor.MarginTopOverride is not null,
                marginRightIsExplicit: pageDescriptor.MarginRightOverride is not null,
                marginBottomIsExplicit: pageDescriptor.MarginBottomOverride is not null,
                sizeIsExplicit: pageDescriptor.PageSizeOverride is not null);

            await container.SetDeclarativeRoot(pageDescriptor.RootBox, config.DefaultLanguage, stylesheet);

            if (stylesheet is not null)
            {
                DomParser.CascadeApplyPageStyles(container.HtmlContainerInt, pageDescriptor.RootBox, stylesheet.CssData,
                    allowMarginLeft: pageDescriptor.MarginLeftOverride is null,
                    allowMarginTop: pageDescriptor.MarginTopOverride is null,
                    allowMarginRight: pageDescriptor.MarginRightOverride is null,
                    allowMarginBottom: pageDescriptor.MarginBottomOverride is null,
                    allowSize: pageDescriptor.PageSizeOverride is null);

                if (container.CssPageSize.HasValue)
                {
                    orgPageSize = container.CssPageSize.Value;
                }
            }

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
    }
}
