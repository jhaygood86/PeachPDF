using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Dom
{
    public class CssContentEngineTests
    {
        [Fact]
        public void ApplyContent_WithStringLiteral_SetsText()
        {
            var box = CreateBox();
            box.Content = "\"Hello World\"";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Hello World", box.Text);
        }

        [Fact]
        public void ApplyContent_WithMultipleStringLiterals_Concatenates()
        {
            var box = CreateBox();
            box.Content = "\"Hello\" \" \" \"World\"";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Hello World", box.Text);
        }

        [Fact]
        public void ApplyContent_WithNone_DoesNotSetText()
        {
            var box = CreateBox();
            box.Content = "none";

            CssContentEngine.ApplyContent(box);

            Assert.Null(box.Text);
        }

        [Fact]
        public void ApplyContent_WithNormal_DoesNotSetText()
        {
            var box = CreateBox();
            box.Content = "normal";

            CssContentEngine.ApplyContent(box);

            Assert.Null(box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunction_EvaluatesNamedString()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter)";

            // Register a named string in the container
            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Introduction", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionFirstKeyword_RetrievesFirst()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, first)";

            // Register multiple named strings with same name
            container.RegisterNamedString(new NamedString("chapter", "First Chapter"));
            container.RegisterNamedString(new NamedString("chapter", "Second Chapter"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("First Chapter", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionLastKeyword_RetrievesLast()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, last)";

            // Register multiple named strings with same name
            container.RegisterNamedString(new NamedString("chapter", "First Chapter"));
            container.RegisterNamedString(new NamedString("chapter", "Second Chapter"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Second Chapter", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionNonExistent_ReturnsEmpty()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(nonexistent)";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionAndLiteral_Concatenates()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "\"Chapter: \" string(chapter)";

            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Chapter: Introduction", box.Text);
        }

        [Fact]
        public void ApplyContent_WithMultipleStringFunctions_ConcatenatesAll()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter) \" / \" string(section)";

            container.RegisterNamedString(new NamedString("chapter", "Chapter One"));
            container.RegisterNamedString(new NamedString("section", "Section A"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Chapter One / Section A", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionAndCounter_CombinesCorrectly()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter) \" - Page \" counter(page)";
            box.CounterIncrement = "page";

            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Contains("Introduction - Page", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionStartKeyword_RetrievesFirst()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, start)";

            container.RegisterNamedString(new NamedString("chapter", "First"));
            container.RegisterNamedString(new NamedString("chapter", "Second"));

            CssContentEngine.ApplyContent(box);

            // Start should behave like first for now
            Assert.Equal("First", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionFirstExceptKeyword_ReturnsEmpty()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, first-except)";

            container.RegisterNamedString(new NamedString("chapter", "Chapter"));

            CssContentEngine.ApplyContent(box);

            // First-except not fully implemented, should return empty
            Assert.Equal("", box.Text);
        }

        [Fact]
        public void ApplyContent_WithCounterNoStyle_UsesDecimal()
        {
            var box = CreateBox();
            box.Content = "counter(item)";
            box.CounterReset = "item 7";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("7", box.Text);
        }

        [Fact]
        public void ApplyContent_WithCounterDecimalLeadingZero_PadsToTwoDigits()
        {
            // Issue #128: counter(x, decimal-leading-zero) used to emit nothing at all.
            var box = CreateBox();
            box.Content = "counter(item, decimal-leading-zero)";
            box.CounterReset = "item 1";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("01", box.Text);
        }

        [Fact]
        public void ApplyContent_WithCounterDecimalLeadingZero_DoesNotOverPad()
        {
            var box = CreateBox();
            box.Content = "counter(item, decimal-leading-zero)";
            box.CounterReset = "item 12";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("12", box.Text);
        }

        [Fact]
        public void ApplyContent_WithCounterAlphabeticStyle_FormatsWithStyle()
        {
            var box = CreateBox();
            box.Content = "counter(item, upper-roman)";
            box.CounterReset = "item 4";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("IV", box.Text);
        }

        [Fact]
        public void ApplyContent_WithCounterUnknownStyle_FallsBackToDecimal()
        {
            // CSS Counter Styles Level 3 §2: unknown style must render as decimal, not empty.
            var box = CreateBox();
            box.Content = "counter(item, bogus-style)";
            box.CounterReset = "item 5";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("5", box.Text);
        }

        [Fact]
        public void ApplyContent_WithMalformedCounterNoName_EmitsNothing()
        {
            // Defensive: counter() with no name argument contributes no text (rather than throwing).
            var box = CreateBox();
            box.Content = "\"x\" counter() \"y\"";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("xy", box.Text);
        }

        [Fact]
        public void ApplyContent_WithCounterAndStyleAndLiteral_Concatenates()
        {
            var box = CreateBox();
            box.Content = "counter(item, decimal-leading-zero) \" Item\"";
            box.CounterReset = "item 3";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("03 Item", box.Text);
        }

        [Fact]
        public void ApplyContent_WithAttrFunction_RetrievesAttribute()
        {
            var box = CreateBox();
            box.HtmlTag!.Attributes!["title"] = "Test Title";
            box.Content = "attr(title)";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Test Title", box.Text);
        }

        [Fact]
        public void ApplyContent_ComplexCombination_EvaluatesCorrectly()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "\"Part \" string(part) \" - Chapter \" string(chapter)";

            container.RegisterNamedString(new NamedString("part", "I"));
            container.RegisterNamedString(new NamedString("chapter", "1"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Part I - Chapter 1", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionUnknownKeyword_FallsBackToFirst()
        {
            // Exercises GetNamedStringValueFromDocument's switch default arm (an unrecognized keyword
            // falls back to "first", same as no keyword at all).
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, not-a-real-keyword)";

            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Introduction", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionAndNoContainer_FallsBackToTreeWalk()
        {
            // Exercises CssContentEngine.GetNamedStringValueFromTree: a box with no HtmlContainer (so
            // GetNamedStringValue can't consult the document-level NamedStrings) still resolves
            // string(name) by walking its own NamedStrings, set directly here the way
            // CssNamedStringEngine.ApplyStringSet would populate it during layout.
            var box = CreateBox();
            box.Content = "string(chapter)";
            box.NamedStrings["chapter"] = new NamedString("chapter", "Introduction");

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Introduction", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionLastKeywordAndNoContainer_WalksAncestorChain()
        {
            // The tree-walk fallback's "last" keyword returns the *nearest* ancestor assignment (the
            // parent, since it's found first walking up), not the farthest - opposite of "first".
            var parentTag = new HtmlTag("div", false, new Dictionary<string, string>());
            var parent = new CssBox(null, parentTag);
            parent.NamedStrings["chapter"] = new NamedString("chapter", "Parent Chapter");

            var childTag = new HtmlTag("span", false, new Dictionary<string, string>());
            var child = new CssBox(parent, childTag);
            child.Content = "string(chapter, last)";

            CssContentEngine.ApplyContent(child);

            Assert.Equal("Parent Chapter", child.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionAndNoContainerOrAssignment_ReturnsEmpty()
        {
            var box = CreateBox();
            box.Content = "string(chapter)";

            CssContentEngine.ApplyContent(box);

            Assert.Equal(string.Empty, box.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextUrlTarget_ResolvesTargetsOwnText()
        {
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2))";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal("Chapter Two", tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextAttrTarget_ResolvesViaHrefAttribute()
        {
            var container = await BuildContainer(
                "<div id=\"ch2\">Chapter Two</div><a id=\"link\" href=\"#ch2\"></a>");
            var linkBox = DomUtils.GetBoxById(container.Root, "link")!;
            linkBox.Content = "target-text(attr(href))";

            CssContentEngine.ApplyContent(linkBox);

            Assert.Equal("Chapter Two", linkBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextUnresolvedTarget_ReturnsEmptyWithoutThrowing()
        {
            var container = await BuildContainer("<div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#does-not-exist))";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal(string.Empty, tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextContentMode_ResolvesTargetsOwnText()
        {
            // Explicit "content" mode (as opposed to the default-argument form covered by
            // ApplyContent_TargetTextUrlTarget_ResolvesTargetsOwnText) exercises the same "content" arm
            // of ResolveTargetText's mode switch.
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2), content)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal("Chapter Two", tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextBeforeMode_ResolvesTargetsBeforePseudoElement()
        {
            var container = await BuildContainerWithHead(
                "#ch2::before { content: \"Chapter: \"; }",
                "<div id=\"ch2\">Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2), before)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal("Chapter: ", tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextAfterMode_ResolvesTargetsAfterPseudoElement()
        {
            var container = await BuildContainerWithHead(
                "#ch2::after { content: \" (end)\"; }",
                "<div id=\"ch2\">Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2), after)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal(" (end)", tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextFirstLetterMode_ResolvesTargetsFirstLetter()
        {
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2), first-letter)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal("C", tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextBeforeModeWithNoBeforePseudoElement_ReturnsEmpty()
        {
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2), before)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal(string.Empty, tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetTextUnrecognizedMode_ReturnsEmpty()
        {
            // TargetTextFunctionConverter rejects an unrecognized mode keyword at parse time, but
            // CssBox.Content is a raw string post-cascade, so exercise ResolveTargetText's mode switch
            // default arm directly the same way ApplyContent_WithStringFunctionUnknownKeyword_FallsBackToFirst
            // exercises GetNamedStringValueFromDocument's default arm above.
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-text(url(#ch2), not-a-real-mode)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal(string.Empty, tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetCounterCustomName_ResolvesImmediatelyWithNoPaginationDependency()
        {
            var container = await BuildContainer(
                "<div id=\"ch2\" style=\"counter-reset: chapter 4\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-counter(url(#ch2), chapter)";

            CssContentEngine.ApplyContent(tocBox);

            // A custom counter's value is structural (populated during DOM construction), unlike `page` -
            // resolves correctly with no HtmlContainerInt.TargetPageMap involved at all.
            Assert.Equal("4", tocBox.Text);
            Assert.False(tocBox.HasPendingTargetPageContent);
        }

        [Fact]
        public async Task ApplyContent_TargetCounterPage_BeforePageMapExists_EmitsPlaceholderAndFlagsBox()
        {
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-counter(url(#ch2), page)";

            Assert.Null(container.TargetPageMap);
            CssContentEngine.ApplyContent(tocBox);

            // Same placeholder counter(page) already silently produces outside margin boxes today - real
            // resolution only happens once HtmlContainerInt's target-page convergence loop has run.
            Assert.Equal("1", tocBox.Text);
            Assert.True(tocBox.HasPendingTargetPageContent);
        }

        [Fact]
        public async Task ApplyContent_TargetCounterPageWithStyle_FormatsPlaceholderInRequestedStyle()
        {
            var container = await BuildContainer("<div id=\"ch2\">Chapter Two</div><div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-counter(url(#ch2), page, upper-roman)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal("I", tocBox.Text);
        }

        [Fact]
        public async Task ApplyContent_TargetCounterUnresolvedTarget_ReturnsEmptyAndDoesNotFlagBox()
        {
            var container = await BuildContainer("<div id=\"toc\"></div>");
            var tocBox = DomUtils.GetBoxById(container.Root, "toc")!;
            tocBox.Content = "target-counter(url(#does-not-exist), page)";

            CssContentEngine.ApplyContent(tocBox);

            Assert.Equal(string.Empty, tocBox.Text);
            Assert.False(tocBox.HasPendingTargetPageContent);
        }

        private static async Task<HtmlContainerInt> BuildContainer(string bodyHtml)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body>{bodyHtml}</body></html>", null);
            return container;
        }

        private static async Task<HtmlContainerInt> BuildContainerWithHead(string styleCss, string bodyHtml)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(
                $"<!DOCTYPE html><html><head><style>{styleCss}</style></head><body>{bodyHtml}</body></html>", null);
            return container;
        }

        private CssBox CreateBox()
        {
            var tag = new HtmlTag("div", false, new Dictionary<string, string>());
            return new CssBox(null, tag);
        }

        private CssBox CreateBox(HtmlContainerInt container)
        {
            var tag = new HtmlTag("div", false, new Dictionary<string, string>());
            var box = new CssBox(null, tag);
            box.HtmlContainer = container;
            return box;
        }

        private HtmlContainerInt CreateContainer()
        {
            var adapter = new PdfSharpAdapter();
            return new HtmlContainerInt(adapter);
        }
    }
}
