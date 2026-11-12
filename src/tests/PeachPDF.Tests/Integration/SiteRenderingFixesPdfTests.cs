using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What it took to render a real site (a server-rendered page with a reset style sheet, nested rules, `var()` spacing and a sticky footer) the
    /// way a browser does, read back from the fills and images a saved PDF paints. The layout rules are asserted box by box in
    /// <c>PeachBrackets.Engine.Tests</c>; these exercise the same engine through the whole pipeline.
    /// </summary>
    public class SiteRenderingFixesPdfTests
    {
        private const string Small = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAEklEQVR4nGM8wYAdMOEQH6QSAKKAANhVIlE9AAAAAElFTkSuQmCC";
        private const string Large = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAIAAABvFaqvAAAALElEQVR4nGNkYDjBQA3ARBVTGEYNIgaMBjZhMBpGhMFoGBEGo2FEGAy+MAIAAdoA+K0BaKkAAAAASUVORK5CYII=";

        private sealed record Fill(string Color, double X, double Y, double Width, double Height);

        private static async Task<(List<Fill> Fills, string Content)> RenderAsync(string css, string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 10px Arial }} {css}</style></head><body>{body}</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            var content = file.ContentText(file.Pages[0]);

            var fills = new List<Fill>();
            var pattern = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in pattern.Matches(content))
            {
                double N(string group) => double.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture) / 0.75;
                fills.Add(new Fill(m.Groups["c"].Value, N("x"), N("y"), N("w"), N("h")));
            }

            return (fills, content);
        }

        private const string Red = "1 0.8 0.8";
        private const string Green = "0.8 1 0.8";
        private const string Blue = "0.8 0.8 1";

        [Fact]
        public async Task EachSelectorOfANestedList_IsRelativeToTheParent()
        {
            // `& p, div` is `& p, & div`; the commas inside `:is()`, a string and an escape do not split the list.
            var (fills, _) = await RenderAsync(
                ".pg { & p, div { background: #fcc } } .box { &:is(.a, .b), [data-x=\"p,q\"], .c\\,d { background: #cfc } }",
                "<div style='height: 5px'>out</div><div class='pg'><p>in</p><div>deep</div></div>"
                + "<div class='box a'>one</div><div class='box'><i data-x='p,q'>two</i></div><div class='box'><div class='c,d'>three</div></div><div class='box'>four</div>");

            // The two inside .pg, not the `div` outside it; `.box.a`, and inside a `.box` the `[data-x]` element and the escaped class.
            Assert.Equal(2, fills.Count(f => f.Color == Red));
            Assert.True(fills.Count(f => f.Color == Green) >= 3, "the three matching boxes should be painted");
        }

        [Fact]
        public async Task AGrowingItemFillsTheMinHeightOfAnAutoHeightColumn()
        {
            // The sticky-footer page: a column of auto height whose `min-height` is taller than its content hands the room to the `flex: 1` item.
            var (fills, _) = await RenderAsync(
                "#c { display: flex; flex-direction: column; min-height: 400px }",
                "<div id='c'><div style='height: 20px; background: #fcc'></div><div style='flex: 1; background: #cfc'><div style='height: 50px'></div></div>"
                + "<div style='height: 30px; background: #ccf'></div></div>");

            Assert.Equal(350, fills.Single(f => f.Color == Green).Height, 0.5);
            Assert.Equal(30, fills.Single(f => f.Color == Blue).Height, 0.5);
        }

        [Theory]
        [InlineData("flex: 1 1 0px")]
        [InlineData("flex: 1")]
        [InlineData("flex-grow: 1")]
        public async Task AGrowingItemOfAnAutoHeightColumnIsAsTallAsItsContent(string flex)
        {
            var (fills, _) = await RenderAsync(
                "#c { display: flex; flex-direction: column }",
                $"<div id='c'><div style='height: 20px; background: #fcc'></div><div style='{flex}; background: #cfc'><div style='height: 100px'></div></div>"
                + "<div style='height: 30px; background: #ccf'></div></div>");

            var green = fills.Single(f => f.Color == Green);
            var blue = fills.Single(f => f.Color == Blue);
            Assert.Equal(100, green.Height, 0.5);

            // PDF y runs up the page: the footer sits entirely below the item.
            Assert.True(blue.Y + blue.Height <= green.Y + 0.5, "the footer must not be drawn over the item's content");
        }

        [Fact]
        public async Task AWrappingFlexRowThatIsAnItemStaysOnOneLine()
        {
            // The footer: a wrapping row of a paragraph and a link, inside a row that spaces its items apart.
            var (fills, _) = await RenderAsync(
                "#o { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 12px; width: 600px } "
                + "#l { display: flex; flex-wrap: wrap; gap: 4px 16px } #l p { margin: 0; background: #fcc } #l a { background: #cfc }",
                "<div id='o'><div style='width: 80px; height: 10px; background: #ccf'></div>"
                + "<div id='l'><p>(c) 2026 Ballentine Pointe Homeowners Association, Inc. All Rights Reserved.</p><a>Privacy Policy</a></div></div>");

            var paragraph = fills.Single(f => f.Color == Red);
            var link = fills.Single(f => f.Color == Green);
            Assert.Equal(paragraph.Y, link.Y, 2);
        }

        [Fact]
        public async Task ADeferredVarLogicalMarginKeepsItsPlaceInTheCascade()
        {
            // `p { margin-block: 0 var(--s) }` then the more specific `.f p { margin: 0 }`: no space is left under the paragraph.
            var (fills, _) = await RenderAsync(
                ":root { --s: 12px } p { margin-block: 0 var(--s); background: #fcc } .f p { margin: 0 } .n { height: 5px; background: #cfc }",
                "<div class='f'><p>text</p></div><div class='n'></div>");

            var paragraph = fills.Single(f => f.Color == Red);
            var next = fills.Single(f => f.Color == Green);
            Assert.Equal(paragraph.Y, next.Y + next.Height, 0.5);
        }

        private static async Task<double> ImageWidthAsync(string css, string markup)
        {
            var (_, content) = await RenderAsync(css, markup);
            var draws = Regex.Matches(content, @"(?<w>\S+) 0 0 (?<h>\S+) \S+ \S+ cm\s+/\S+ Do", RegexOptions.CultureInvariant);
            return double.Parse(draws[0].Groups["w"].Value, CultureInfo.InvariantCulture) / 0.75;
        }

        [Theory]
        [InlineData("img { display: block }", "<a href='#'>", "</a>")]
        [InlineData("a { display: inline-block } img { display: block }", "<a href='#'>\n  ", "\n</a>")]
        [InlineData("img { display: block }", "", "")]
        public async Task APictureWithABlockImgStillUsesItsMatchingSource(string css, string open, string close)
        {
            var matching = $"{open}<picture>\n  <source media='(min-width: 10px)' srcset='{Large}' />\n  <img src='{Small}' />\n</picture>{close}";
            Assert.Equal(24, await ImageWidthAsync(css, matching), 0.5);

            var skipped = $"{open}<picture>\n  <source media='(min-width: 99999px)' srcset='{Large}' />\n  <img src='{Small}' />\n</picture>{close}";
            Assert.Equal(8, await ImageWidthAsync(css, skipped), 0.5);
        }

        [Fact]
        public async Task ABlockImageInAnInlineBlockLinkKeepsTheStrutBelowIt()
        {
            var (fills, _) = await RenderAsync(
                "a { display: inline-block } img { display: block; width: 20px; height: 72px } .c { background: #fcc; font: 16px/1.6 Arial }",
                $"<div class='c'><a><img src='{Small}'></a></div>");

            Assert.True(fills.Single(f => f.Color == Red).Height > 76, "the line should be taller than the image by the strut's descent");
        }
    }
}
