using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The HTML Standard's "Hidden elements" rendering rules (15.3.1): the <c>hidden</c> attribute makes
    /// an element <c>display: none</c>, and so do a fixed list of elements, <c>input type=hidden</c> and -
    /// only where a script context exists, which a PDF has not - <c>noscript</c>. The default style sheet
    /// carries the spec's own selectors, including the <c>i</c> case-sensitivity modifier they use.
    /// </summary>
    public class HiddenAttributeTests
    {
        private static async Task<List<string>> DrawnTextsAsync(string html)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(html);
            var g = new TestRecordingGraphics();

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                FragmentPaintHarness.PaintPage(container, g, page);
            }

            return g.DrawStringCalls.Select(c => c.Text.Trim()).ToList();
        }

        private static async Task<CssBox> BoxAsync(string body, string id = "x")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            return LayoutHarness.FindById(root, id)!;
        }

        [Theory]
        [InlineData("<div id='x' hidden>SECRET</div>")]
        [InlineData("<div id='x' hidden=''>SECRET</div>")]
        [InlineData("<div id='x' hidden='hidden'>SECRET</div>")]
        [InlineData("<div id='x' HIDDEN>SECRET</div>")]
        [InlineData("<span id='x' hidden>SECRET</span>")]
        [InlineData("<p id='x' hidden='anything else'>SECRET</p>")]
        public async Task HiddenAttribute_MakesTheElementDisplayNone(string markup)
        {
            var box = await BoxAsync(markup);

            Assert.Equal(DisplayMode.None, box.Display.Value);
        }

        [Fact]
        public async Task HiddenBlock_TakesNoSpace_AndItsTextIsNeverDrawn()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:20pt'>A</div><div id='h' hidden style='height:50pt'>BLOCKSECRET</div><div id='b' style='height:20pt'>B</div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.ActualBottom, b.Location.Y, 3);

            var drawn = await DrawnTextsAsync(LayoutHarness.Wrap(
                "<div>A</div><div hidden>BLOCKSECRET</div><div>B</div>"));

            Assert.Contains("A", drawn);
            Assert.Contains("B", drawn);
            Assert.DoesNotContain("BLOCKSECRET", drawn);
        }

        [Fact]
        public async Task HiddenInline_LeavesTheSurroundingTextFlowing_TheIssueRepro()
        {
            var drawn = await DrawnTextsAsync(LayoutHarness.Wrap(
                "<p>before <span hidden>SECRET</span> after</p><div hidden>BLOCKSECRET</div>"));

            Assert.Contains("before", drawn);
            Assert.Contains("after", drawn);
            Assert.DoesNotContain("SECRET", drawn);
            Assert.DoesNotContain("BLOCKSECRET", drawn);
        }

        [Fact]
        public async Task HiddenAttribute_IsOverriddenByAnAuthorDisplayDeclaration()
        {
            // The rule lives in the UA origin, so any author `display` beats it - which is how Chrome
            // behaves too (an author who restyles a hidden element is asking for it to show).
            var box = await BoxAsync("<style>#x{display:block}</style><div id='x' hidden>shown</div>");

            Assert.Equal(DisplayMode.Block, box.Display.Value);

            var inline = await BoxAsync("<div id='x' hidden style='display:block'>shown</div>");

            Assert.Equal(DisplayMode.Block, inline.Display.Value);
        }

        [Theory]
        [InlineData("until-found")]
        [InlineData("UNTIL-FOUND")]
        public async Task HiddenUntilFound_IsNotHiddenByTheDisplayRule(string value)
        {
            // [hidden=until-found i] is excluded from the display: none rule (it gets content-visibility:
            // hidden instead, which PeachPDF has no property for, so the content stays visible).
            var box = await BoxAsync($"<div id='x' hidden='{value}'>found</div>");

            Assert.Equal(DisplayMode.Block, box.Display.Value);
        }

        [Fact]
        public async Task HiddenEmbed_IsNotDisplayNone_ButIsZeroSized()
        {
            var box = await BoxAsync("<p>a<embed id='x' hidden width='100' height='100'>b</p>");

            Assert.NotEqual(DisplayMode.None, box.Display.Value);
            Assert.Equal(DisplayMode.Inline, box.Display.Value);
        }

        [Theory]
        [InlineData("area")]
        [InlineData("base")]
        [InlineData("basefont")]
        [InlineData("datalist")]
        [InlineData("link")]
        [InlineData("meta")]
        [InlineData("noembed")]
        [InlineData("noframes")]
        [InlineData("param")]
        [InlineData("rp")]
        [InlineData("script")]
        [InlineData("style")]
        [InlineData("template")]
        [InlineData("title")]
        public async Task TheSpecsHiddenElements_AreDisplayNone(string tag)
        {
            var box = await BoxAsync($"<div>a<{tag} id='x'>inner</{tag}>b</div>");

            Assert.Equal(DisplayMode.None, box.Display.Value);
        }

        [Fact]
        public async Task TextInsideAHiddenElementOfTheList_IsNeverDrawn()
        {
            var drawn = await DrawnTextsAsync(LayoutHarness.Wrap(
                "<p>keep</p><noframes>NOFRAMES</noframes><template><p>TEMPLATE</p></template><datalist><option>DATALIST</option></datalist>"));

            Assert.Contains("keep", drawn);
            Assert.DoesNotContain("NOFRAMES", drawn);
            Assert.DoesNotContain("TEMPLATE", drawn);
            Assert.DoesNotContain("DATALIST", drawn);
        }

        [Theory]
        [InlineData("hidden")]
        [InlineData("HIDDEN")]
        [InlineData("Hidden")]
        public async Task InputTypeHidden_IsDisplayNone_WhateverTheCaseOfItsType(string type)
        {
            var box = await BoxAsync($"<form><input id='x' type='{type}' value='v'></form>");

            Assert.Equal(DisplayMode.None, box.Display.Value);
        }

        [Fact]
        public async Task InputTypeHidden_StaysHiddenAgainstAnAuthorDisplayDeclaration()
        {
            // The spec's rule is `display: none !important`, so unlike [hidden] an author cannot show it.
            var box = await BoxAsync("<style>input{display:block}</style><input id='x' type='hidden'>");

            Assert.Equal(DisplayMode.None, box.Display.Value);
        }

        [Fact]
        public async Task Noscript_StillRenders_BecauseAPdfHasNoScriptingContext()
        {
            // The spec hides <noscript> only inside @media (scripting), and `scripting` reports `none`
            // for a document with no script context, so the block never matches.
            var box = await BoxAsync("<noscript id='x'><p>fallback</p></noscript>");

            Assert.NotEqual(DisplayMode.None, box.Display.Value);

            var drawn = await DrawnTextsAsync(LayoutHarness.Wrap("<noscript><p>fallback</p></noscript>"));

            Assert.Contains("fallback", drawn);
        }

        [Fact]
        public async Task ScriptingMediaFeature_BooleanFormIsFalse_AndNoneFormIsTrue()
        {
            var off = await BoxAsync("<style>@media (scripting) { #x { display: none } }</style><div id='x'>shown</div>");
            var on = await BoxAsync("<style>@media (scripting: none) { #x { display: none } }</style><div id='x'>hidden</div>");

            Assert.Equal(DisplayMode.Block, off.Display.Value);
            Assert.Equal(DisplayMode.None, on.Display.Value);
        }
    }
}
