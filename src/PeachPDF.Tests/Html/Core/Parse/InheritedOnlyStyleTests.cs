using PeachPDF;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// A text or anonymous box has no element, so no rule or attribute reaches it and its computed style depends only on the style it
    /// had going into the cascade and its parent's; the cascade keeps the result and hands it to the next such box under a parent of
    /// that style. These check the shared style is the one the cascade would have made (the check mode runs the cascade on every hit
    /// and compares), and that it is shared.
    /// </summary>
    public class InheritedOnlyStyleTests
    {
        private static async Task<(int Verified, CssBox Root)> LayoutVerifiedAsync(string body)
        {
            var check = new DomParser.SharedStyleCheck();
            DomParser.VerifySharedStyles.Value = check;
            try
            {
                var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
                return (check.Verified, root);
            }
            finally
            {
                DomParser.VerifySharedStyles.Value = null;
            }
        }

        public static TheoryData<string, string> Documents => new()
        {
            { "cells", "<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>" },
            { "list", "<ul><li>one</li><li>two</li><li>three</li></ul><ol><li>x</li><li>y</li></ol>" },
            { "inline runs", "<p>one <b>two</b> three <i>four</i> five <b>six</b></p><p>seven <b>eight</b> nine</p>" },
            { "inherited properties", "<div style='color: red; font-size: 14pt; letter-spacing: 1pt'><p>a</p><p>b <span>c</span></p></div>" },
            { "custom properties", "<div style='--c: red; color: var(--c)'><p>a</p><p>b</p></div><p>c</p><p>d</p>" },
            { "flex", "<div style='display: flex'>text<span>a</span>more<span>b</span></div>" },
            { "grid", "<div style='display: grid'>text<span>a</span>more<span>b</span></div>" },
            { "float and position", "<div style='float: left'>a</div><div style='float: left'>b</div><div style='position: absolute'>c</div><div style='position: absolute'>d</div>" },
            { "pseudo-elements", "<style>p::before { content: 'x' } p::after { content: 'y' } p::first-letter { color: red }</style><p>one</p><p>two</p>" },
            { "display contents", "<div style='display: contents'>a<span>b</span></div><div style='display: contents'>c</div>" },
            { "writing modes", "<div style='writing-mode: vertical-rl'><p>a</p><p>b</p></div><p>c</p>" },
            { "columns", "<div style='columns: 2'><p>a</p><p>b</p><p>c</p></div>" },
            { "pre and whitespace", "<pre>a\n b</pre><pre>c\n d</pre><p style='white-space: nowrap'>e f</p><p style='white-space: nowrap'>g h</p>" },
            { "text transform", "<div style='text-transform: uppercase'><p>a</p><p>b</p></div>" },
            { "quotes and counters", "<style>q { quotes: '<' '>' } li { counter-increment: n }</style><p><q>a</q> <q>b</q></p><ul><li>c</li><li>d</li></ul>" },
            { "nested tables", "<table><tr><td><table><tr><td>a</td><td>b</td></tr></table></td><td>c</td></tr></table>" },
            { "forms", "<form><label>a</label><input value='b'><label>c</label><button>d</button></form>" },
            { "svg and math", "<p>t<svg width='10' height='10'><text>a</text></svg><math><mi>x</mi></math>u</p>" },
            { "bidi", "<p dir='rtl'>אב <span>c</span> ג</p><p dir='ltr'>a <span>א</span> b</p>" },
        };

        [Theory]
        [MemberData(nameof(Documents))]
        public async Task ASharedStyle_IsTheOneTheCascadeMakes(string name, string body)
        {
            var (verified, root) = await LayoutVerifiedAsync(body);

            // The run itself is the assertion (a mismatch throws). A document with no text boxes under parents of one style has
            // nothing to compare, which these are written not to be; the ones that cannot share (custom properties) are asserted below.
            Assert.True(verified > 0 || name is "custom properties" or "pseudo-elements" or "display contents", $"'{name}' compared no cached style");
            Assert.NotNull(root);
        }

        [Fact]
        public async Task TextBoxesUnderIdenticalCells_ShareOneComputedStyle()
        {
            var (_, root) = await LayoutVerifiedAsync(
                "<table id='t'><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>");

            var texts = Descendants(LayoutHarness.FindById(root, "t")!).Where(b => b.HtmlTag is null && b.Text is not null).ToList();

            Assert.Equal(4, texts.Count);
            Assert.All(texts.Skip(1), text => Assert.Same(texts[0].ComputedStyle, text.ComputedStyle));
        }

        [Fact]
        public async Task TextBoxesUnderParentsOfDifferentStyle_DoNotShare()
        {
            var (_, root) = await LayoutVerifiedAsync(
                "<div id='d'><p style='color: red'>a</p><p style='color: blue'>b</p><p>c</p></div>");

            var texts = Descendants(LayoutHarness.FindById(root, "d")!).Where(b => b.HtmlTag is null && b.Text is not null).ToList();

            Assert.Equal(3, texts.Count);
            Assert.Equal(3, texts.Select(t => t.ComputedStyle).Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Equal(["rgb(255, 0, 0)", "rgb(0, 0, 255)"], texts.Take(2).Select(t => t.Color));
        }

        [Fact]
        public async Task AParentWithCustomProperties_IsLeftOutOfTheCache()
        {
            var (verified, root) = await LayoutVerifiedAsync(
                "<div id='d' style='--c: red'><p>a</p><p>b</p></div>");

            var texts = Descendants(LayoutHarness.FindById(root, "d")!).Where(b => b.HtmlTag is null && b.Text is not null).ToList();

            Assert.Equal(0, verified);
            Assert.Equal(2, texts.Count);
            Assert.Equal("red", texts[0].CustomProperties!["--c"]);
            Assert.NotSame(texts[0].CustomProperties, texts[1].CustomProperties);
        }

        [Fact]
        public async Task AnimatedDocuments_AreLeftOutOfTheCache()
        {
            var check = new DomParser.SharedStyleCheck();
            DomParser.VerifySharedStyles.Value = check;
            try
            {
                await new PdfGenerator().GeneratePdf(
                    "<style>@keyframes k { from { color: red } to { color: blue } } p { animation: k 1s linear }</style><p>a</p><p>b</p>",
                    new PdfGenerateConfig { AnimationProgress = 0.5 });
            }
            finally
            {
                DomParser.VerifySharedStyles.Value = null;
            }

            Assert.Equal(0, check.Verified);
        }

        private static IEnumerable<CssBox> Descendants(CssBox box) =>
            box.Boxes.SelectMany(child => new[] { child }.Concat(Descendants(child)));
    }
}
