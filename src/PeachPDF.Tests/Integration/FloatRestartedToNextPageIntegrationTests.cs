using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A float with no room left on its page is restarted at the top of the next one, and the in-flow
    /// content after it carries on where it was: every line of it still has to fit the page it is on.
    /// </summary>
    /// <remarks>
    /// Restarting the float steps the pass's fragmentainer cursor to the next page, and nothing stepped it
    /// back, so every line-straddle question for the paragraph after the float was asked of the next
    /// page's band. A line crossing the foot of the page the paragraph really sits on was then never
    /// broken: it was painted there and cut in two by the page clip, its words lost to text extraction.
    /// The fixtures measure in the bundled Liberation Sans (Arial's metrics) on a 300×240pt page with 20pt
    /// margins, so page 1's content band is [20, 220).
    /// </remarks>
    public partial class FloatRestartedToNextPageIntegrationTests
    {
        private const string Font = "FloatRestartSans";
        private const double PageHeight = 240;
        private const double Margin = 20;

        private static readonly string Paragraph = string.Join(" ", Enumerable.Range(10, 34).Select(i => $"w1_{i}"));

        // Three lines, 36pt: splitting it 2+1 would break widows, so a float this tall that crosses the foot moves whole.
        private const string FloatContent = "<div>w1_90 w1_91<br>w1_92<br>w1_93 w1_94</div>";

        private const string LaterFloatContent = "<div>w1_70 w1_71<br>w1_72<br>w1_73 w1_74</div>";

        private static async Task<(CssBox Root, HtmlContainerInt Container, string Html)> LayoutAsync(
            string floatStyle, string spacerHeight, string between = "")
        {
            var html = "<!DOCTYPE html><html><head></head>" +
                       $"<body style='margin:0;font-family:\"{Font}\";font-size:10pt;line-height:12pt'>" +
                       $"<div style='height:{spacerHeight}pt'>w1_1</div>" +
                       $"<div id='f' style='{floatStyle}'>{FloatContent}</div>" +
                       between +
                       $"<p id='p' style='margin:0'>{Paragraph}</p></body></html>";

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: PageHeight, margin: Margin,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, Font));

            return (root, container, html);
        }

        private static void AssertEveryWordFitsItsPage_AndNoneIsLost(HtmlContainerInt container, string html)
        {
            var pageFoot = container.PageBoxRect.Bottom;
            var pages = container.FragmentTree!.Fragmentainers;
            Assert.True(pages.Count >= 2);

            foreach (var page in pages)
            {
                foreach (var word in Flatten(page.Root).SelectMany(fragment => fragment.Words))
                {
                    Assert.True(word.Rect.Bottom <= pageFoot + 0.001,
                        $"'{word.Word.Text}' reaches {word.Rect.Bottom:F2}, past the page foot at {pageFoot:F2}");
                }
            }

            var drawn = pages
                .SelectMany(page => Flatten(page.Root).SelectMany(fragment => fragment.Words))
                .Select(w => w.Word.Text)
                .Where(w => w is not null && w.StartsWith("w1_"))
                .Order();
            var expected = WordPattern().Matches(html).Select(m => m.Value).Order();

            Assert.Equal(expected, drawn);
        }

        // Spacer heights put the paragraph's first line at 187, 188, 189 - the third line of it then ends at
        // 223 to 225 against a foot of 220.
        [Theory]
        [InlineData("float:right;width:125pt", "167")]
        [InlineData("float:right;width:125pt", "168")]
        [InlineData("float:left;width:125pt", "169")]
        public async Task ParagraphAfterARestartedFloat_BreaksBetweenItsLinesAtThePageFoot(string floatStyle, string spacerHeight)
        {
            var (root, container, html) = await LayoutAsync(floatStyle, spacerHeight);

            var f = LayoutHarness.FindById(root, "f")!;
            var p = LayoutHarness.FindById(root, "p")!;

            // The float did not fit and moved to the top of page 2; the paragraph did not move with it.
            Assert.Equal(container.PageTopOf(1), f.Location.Y, 3);
            Assert.True(p.Location.Y < container.PageBottomOf(0), "the paragraph starts on page 1");

            AssertEveryWordFitsItsPage_AndNoneIsLost(container, html);
        }

        // What follows the float can itself be placed past it: a second float beside or below the first on the
        // next page, or a paragraph that clears it. The cursor goes back only for the paragraph that stays on
        // page 1; these are re-stepped onto page 2 by the ordinary in-flow step, and must not lose a line.
        [Theory]
        [InlineData("<div style='float:right;width:125pt'>" + LaterFloatContent + "</div>")]
        [InlineData("<div style='float:left;width:60pt'>" + LaterFloatContent + "</div>")]
        [InlineData("<div style='float:right;width:125pt;clear:right'>" + LaterFloatContent + "</div>")]
        [InlineData("<p style='margin:0;clear:both'>w1_60 w1_61 w1_62 w1_63 w1_64 w1_65 w1_66 w1_67 w1_68 w1_69</p>")]
        [InlineData("<p style='margin:0;clear:right'>w1_60 w1_61 w1_62 w1_63 w1_64 w1_65 w1_66 w1_67 w1_68 w1_69</p>")]
        public async Task ContentPlacedPastTheRestartedFloat_DoesNotLoseALine(string between)
        {
            var (_, container, html) = await LayoutAsync("float:right;width:125pt", "167", between);

            AssertEveryWordFitsItsPage_AndNoneIsLost(container, html);
        }

        // The control: a float that fits keeps its own place and leaves the paragraph's lines where they were.
        [Fact]
        public async Task ParagraphAfterAFloatThatFits_IsUnchanged()
        {
            var (root, container, _) = await LayoutAsync("float:right;width:125pt", "100");

            var f = LayoutHarness.FindById(root, "f")!;

            Assert.Equal(Margin + 100, f.Location.Y, 3);

            foreach (var page in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in Flatten(page.Root).SelectMany(fragment => fragment.Words))
                {
                    Assert.True(word.Rect.Bottom <= container.PageBoxRect.Bottom + 0.001);
                }
            }
        }

        [GeneratedRegex(@"w1_\d+")]
        private static partial Regex WordPattern();

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
