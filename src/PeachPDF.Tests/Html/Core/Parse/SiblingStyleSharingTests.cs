using System.Text;
using PeachPDF;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// An element takes the computed style of an earlier sibling with the same tag and attributes, skipping the matching and applying
    /// of rules, when no rule that could reach it depends on its place among its siblings or its content (see
    /// <c>CssData.CanShareStyleAmongSiblings</c>). The check mode runs the cascade on every hit and throws if it disagrees, so a run
    /// of these is a comparison of the shared style with the real one; the expectations say where sharing must and must not happen.
    /// </summary>
    public class SiblingStyleSharingTests
    {
        private static readonly string Rows =
            string.Concat(Enumerable.Range(0, 6).Select(i => $"<tr><td>a{i}</td><td>b{i}</td><td class='n'>{i}</td></tr>"));

        public static TheoryData<string, string> Documents => new()
        {
            { "plain rows", $"<table>{Rows}</table>" },
            { "classes", "<style>.a { color: red } .b { color: blue } p { margin: 0 }</style><p class='a'>1</p><p class='b'>2</p><p class='a'>3</p><p>4</p><p class='b'>5</p><p class='a'>6</p>" },
            { "zebra", $"<style>tr:nth-child(even) {{ background: #eee }} td {{ padding: 2pt }}</style><table>{Rows}</table>" },
            { "first and last child", $"<style>td:first-child {{ font-weight: bold }} td:last-child {{ text-align: right }}</style><table>{Rows}</table>" },
            { "only and type", "<style>p:only-child { color: red } span:first-of-type { color: blue } span:last-of-type { color: green }</style><div><p>a</p></div><div><p>b</p><p>c</p></div><p><span>1</span><span>2</span><span>3</span></p>" },
            { "adjacent and general sibling", "<style>h2 + p { color: red } h2 ~ p { margin-left: 3pt }</style><h2>t</h2><p>a</p><p>b</p><p>c</p>" },
            { "empty", "<style>td:empty { background: red } td { padding: 1pt }</style><table><tr><td></td><td>x</td><td></td><td>y</td></tr></table>" },
            { "has", "<style>li:has(b) { color: red } li { margin: 0 }</style><ul><li><b>a</b></li><li>b</li><li><b>c</b></li><li>d</li></ul>" },
            { "not and is", "<style>p:not(.x) { color: red } p:is(.y, .z) { margin-left: 4pt }</style><p class='x'>1</p><p class='y'>2</p><p>3</p><p class='z'>4</p><p class='x'>5</p>" },
            { "attribute selectors", "<style>a[href] { color: red } a[href^='http'] { color: blue } [data-k='1'] { margin-left: 2pt }</style><a href='x'>1</a> <a href='http://y'>2</a> <a>3</a> <a href='x'>4</a> <p data-k='1'>5</p><p data-k='2'>6</p><p data-k='1'>7</p>" },
            { "descendant context", "<style>.o p { color: red } .e p { color: blue } .o > span { margin-left: 3pt }</style><div class='o'><p>1</p><p>2</p><span>s</span><span>t</span></div><div class='e'><p>3</p><p>4</p></div>" },
            { "ancestor structure", "<style>li:first-child a { color: red } li:nth-child(2) a { color: blue }</style><ul><li><a href='#'>1</a><a href='#'>1b</a></li><li><a href='#'>2</a><a href='#'>2b</a></li><li><a href='#'>3</a></li></ul>" },
            { "before and after", "<style>p::before { content: '[' } p::after { content: ']' } li { margin: 0 }</style><p>a</p><p>b</p><p>c</p><ul><li>x</li><li>y</li></ul>" },
            { "before on some", "<style>.m::before { content: '*' }</style><p class='m'>a</p><p>b</p><p class='m'>c</p><p>d</p>" },
            { "first letter", "<style>p::first-letter { font-size: 200%; color: red }</style><p>one</p><p>two</p><p>three</p>" },
            { "marker", "<style>li::marker { color: red }</style><ul><li>a</li><li>b</li><li>c</li></ul><ol><li>1</li><li>2</li></ol>" },
            { "lang", "<style>p:lang(fr) { color: red }</style><div lang='fr'><p>a</p><p>b</p></div><div lang='en'><p>c</p><p>d</p></div>" },
            { "container query", "<style>@container (min-width: 1pt) { p { color: red } }</style><div style='container-type: inline-size'><p>a</p><p>b</p></div>" },
            { "media and layers", "<style>@media print { p { color: red } } @layer a { p { margin-left: 2pt } } @layer b { p.x { margin-left: 5pt } }</style><p>1</p><p class='x'>2</p><p>3</p><p class='x'>4</p>" },
            { "important and revert", "<style>p { color: red !important } p.r { all: revert } p.v { color: var(--c, green) }</style><p>1</p><p class='r'>2</p><p class='v'>3</p><p>4</p>" },
            { "inline styles", "<p style='color: red'>1</p><p style='color: red'>2</p><p style='color: blue'>3</p><p>4</p><p style='color: red'>5</p>" },
            { "custom properties", "<style>:root { --c: red } .u { color: var(--c) }</style><div style='--c: blue'><p class='u'>1</p><p class='u'>2</p></div><p class='u'>3</p><p class='u'>4</p>" },
            { "display contents", "<div><span style='display: contents'>a</span><span style='display: contents'>b</span><span>c</span></div>" },
            { "flex and grid items", "<div style='display: flex'><span>a</span><span>b</span><span>c</span></div><div style='display: grid'><span>d</span><span>e</span></div>" },
            { "floats and positions", "<div style='float: left'>a</div><div style='float: left'>b</div><div style='position: absolute'>c</div><div style='position: absolute'>d</div>" },
            { "forms", "<form><input placeholder='a'><input placeholder='b'><input value='c'><textarea placeholder='d'></textarea><textarea>e</textarea><button>f</button><button>g</button></form>" },
            { "placeholder-shown", "<style>input:placeholder-shown { color: red }</style><input placeholder='a'><input placeholder='b' value='x'><input placeholder='c'>" },
            { "lists", "<ul><li>a<ul><li>b</li><li>c</li></ul></li><li>d</li></ul><ol><li>1</li><li>2</li></ol>" },
            { "headings and paragraphs", "<h1>a</h1><p>b</p><p>c</p><h2>d</h2><p>e</p><p>f</p>" },
            { "tables with spans", "<table><tr><td colspan='2'>a</td><td>b</td></tr><tr><td>c</td><td rowspan='2'>d</td><td>e</td></tr><tr><td>f</td><td>g</td></tr></table>" },
            { "nested tables", $"<table><tr><td><table>{Rows}</table></td><td><table>{Rows}</table></td></tr></table>" },
            { "svg and math", "<p><svg width='10' height='10'><rect width='5' height='5'/></svg><svg width='10' height='10'><rect width='5' height='5'/></svg><math><mi>x</mi></math><math><mi>y</mi></math></p>" },
            { "images", "<img width='10' height='10'><img width='10' height='10'><img width='20' height='10'>" },
            { "dir and bidi", "<p dir='rtl'>א</p><p dir='rtl'>ב</p><p dir='auto'>ג</p><p dir='auto'>x</p><p dir='ltr'>y</p>" },
            { "hidden", "<p hidden>a</p><p hidden>b</p><p>c</p>" },
            { "ids", "<style>#a { color: red } #b { color: blue }</style><p id='a'>1</p><p id='b'>2</p><p id='c'>3</p><p id='a'>4</p>" },
            { "universal rules", "<style>* { margin: 0; box-sizing: border-box } p { padding: 1pt }</style><p>1</p><p>2</p><div><p>3</p><p>4</p></div>" },
            { "unknown selectors", "<style>p:unknown { color: red } p:hover { color: blue } p:focus { color: green } p:target { margin-left: 1pt }</style><p>1</p><p>2</p><p>3</p>" },
        };

        private static async Task<(DomParser.SharedStyleCheck Check, CssBox Root)> LayoutVerifiedAsync(string body)
        {
            var check = new DomParser.SharedStyleCheck();
            DomParser.VerifySharedStyles.Value = check;
            try
            {
                var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
                return (check, root);
            }
            finally
            {
                DomParser.VerifySharedStyles.Value = null;
            }
        }

        [Theory]
        [MemberData(nameof(Documents))]
        public async Task AStyleSharedBetweenSiblings_IsTheOneTheCascadeMakes(string name, string body)
        {
            // The run is the assertion: a shared style that differs from the real one, or a box or mark the real one would have added,
            // throws from the cascade.
            var (check, root) = await LayoutVerifiedAsync(body);

            Assert.NotNull(root);
            Assert.True(check.Verified + check.VerifiedElements >= 0, name);
        }

        [Fact]
        public async Task RowsAndCellsWithNoPositionalRule_ShareTheirStyles()
        {
            var (check, root) = await LayoutVerifiedAsync($"<table id='t'>{Rows}</table>");

            var cells = Elements(LayoutHarness.FindById(root, "t")!, "td").Where(c => c.HtmlTag!.Attributes is null).ToList();

            Assert.True(check.VerifiedElements > 0);
            Assert.Equal(12, cells.Count);
            Assert.All(cells.Skip(1), cell => Assert.Same(cells[0].ComputedStyle, cell.ComputedStyle));
        }

        [Fact]
        public async Task ARuleByPosition_KeepsThoseElementsApart()
        {
            var (_, root) = await LayoutVerifiedAsync(
                "<style>tr:nth-child(even) { background-color: #eeeeee } td:first-child { font-weight: bold }</style>" +
                $"<table id='t'>{Rows}</table>");

            var table = LayoutHarness.FindById(root, "t")!;
            var rows = Elements(table, "tr").ToList();
            var firstCells = rows.Select(r => r.Boxes.First(b => b.HtmlTag?.Name == "td")).ToList();
            var otherCells = rows.Select(r => r.Boxes.Last(b => b.HtmlTag?.Name == "td")).ToList();

            Assert.Equal([false, true, false, true, false, true], rows.Select(r => r.BackgroundColor != "transparent" && r.BackgroundColor != string.Empty && r.BackgroundColor != "rgba(0, 0, 0, 0)"));
            var weight = (CssBox c) => CssUtils.GetPropertyValue(c, "font-weight");
            Assert.All(firstCells, c => Assert.Equal("bold", weight(c)));
            Assert.All(otherCells, c => Assert.NotEqual("bold", weight(c)));
        }

        [Fact]
        public async Task ABoxAddedByARule_IsAddedToEveryElementItMatches()
        {
            var (_, root) = await LayoutVerifiedAsync("<style>p::before { content: '[' } p::after { content: ']' }</style><div id='d'><p>a</p><p>b</p><p>c</p></div>");

            var paragraphs = Elements(LayoutHarness.FindById(root, "d")!, "p").ToList();

            Assert.Equal(3, paragraphs.Count);
            Assert.All(paragraphs, p =>
            {
                Assert.Single(p.Boxes, b => b.IsBeforePseudoElement);
                Assert.Single(p.Boxes, b => b.IsAfterPseudoElement);
            });
        }

        [Fact]
        public async Task ABoxAddedToSomeElementsOnly_IsNotHandedToTheOthers()
        {
            var (_, root) = await LayoutVerifiedAsync("<style>.m::before { content: '*' }</style><div id='d'><p class='m'>a</p><p>b</p><p class='m'>c</p><p>d</p></div>");

            var paragraphs = Elements(LayoutHarness.FindById(root, "d")!, "p").ToList();

            Assert.Equal([true, false, true, false], paragraphs.Select(p => p.Boxes.Any(b => b.IsBeforePseudoElement)));
        }

        [Fact]
        public async Task AFirstLetterRule_MarksEveryElementItMatches()
        {
            var (_, root) = await LayoutVerifiedAsync("<style>p::first-letter { color: red }</style><div id='d'><p>one</p><p>two</p><p>three</p></div>");

            var paragraphs = Elements(LayoutHarness.FindById(root, "d")!, "p").ToList();

            Assert.All(paragraphs, p => Assert.True(p.MatchesFirstLetterSelector));
        }

        [Fact]
        public async Task ElementsThatDifferInAnAttribute_AreNotGivenOneAnothersStyle()
        {
            var (_, root) = await LayoutVerifiedAsync(
                "<style>[data-k='1'] { color: rgb(255, 0, 0) } [data-k='2'] { color: rgb(0, 0, 255) }</style>" +
                "<div id='d'><p data-k='1'>a</p><p data-k='2'>b</p><p data-k='1'>c</p><p>d</p></div>");

            var colours = Elements(LayoutHarness.FindById(root, "d")!, "p").Select(p => p.Color).ToList();

            Assert.Equal(["rgb(255, 0, 0)", "rgb(0, 0, 255)", "rgb(255, 0, 0)"], colours.Take(3));
            Assert.NotEqual("rgb(255, 0, 0)", colours[3]);
        }

        [Fact]
        public async Task EmptyAndFilledCells_AreGivenTheirOwnStyles()
        {
            var (_, root) = await LayoutVerifiedAsync(
                "<style>td:empty { background-color: rgb(255, 0, 0) }</style><table id='t'><tr><td></td><td>x</td><td></td><td>y</td></tr></table>");

            var cells = Elements(LayoutHarness.FindById(root, "t")!, "td").ToList();

            Assert.Equal([true, false, true, false], cells.Select(c => c.BackgroundColor == "rgb(255, 0, 0)"));
        }

        [Fact]
        public async Task AParentWithCustomProperties_IsLeftOutOfTheSharing()
        {
            var (check, _) = await LayoutVerifiedAsync("<div style='--c: red'><p>a</p><p>b</p><p>c</p></div>");

            Assert.Equal(0, check.VerifiedElements);
        }

        // ---- the whole document, with and without sharing

        private static async Task<string> RenderAsync(string body, bool shareSiblings)
        {
            DomParser.DisableSiblingStyleSharing.Value = !shareSiblings;
            try
            {
                var document = await new PdfGenerator().GeneratePdf(
                    $"<!DOCTYPE html><html lang='en'><body style='margin: 0'>{body}</body></html>",
                    new PdfGenerateConfig { PageSize = PageSize.A5, CompressContentStreams = false });

                var content = new StringBuilder();
                for (var i = 0; i < document.Inspect().PageCount; i++)
                {
                    content.Append("\n%% page ").Append(i).Append('\n');

                    content.Append(document.Inspect().ContentText(document.Inspect().Pages[i]));
                }

                return content.ToString();
            }
            finally
            {
                DomParser.DisableSiblingStyleSharing.Value = false;
            }
        }

        [Theory]
        [MemberData(nameof(Documents))]
        public async Task ADocumentWithSharing_IsTheDocumentWithout(string name, string body)
        {
            var shared = await RenderAsync(body, shareSiblings: true);
            var unshared = await RenderAsync(body, shareSiblings: false);

            Assert.True(shared == unshared, $"'{name}' differs when siblings share styles");
        }

        // The math documents bring structures of their own: inline math, a stylesheet that reaches the MathML elements, bidi.
        [Theory]
        [MemberData(nameof(MathML.MathSourceParityTests.Documents), MemberType = typeof(MathML.MathSourceParityTests))]
        public async Task AMathDocumentWithSharing_IsTheDocumentWithout(string name, string body)
        {
            var shared = await RenderAsync(body, shareSiblings: true);
            var unshared = await RenderAsync(body, shareSiblings: false);

            Assert.True(shared == unshared, $"'{name}' differs when siblings share styles");
        }

        private static IEnumerable<CssBox> Elements(CssBox box, string tag) =>
            box.Boxes.SelectMany(child => (child.HtmlTag?.Name == tag ? new[] { child } : []).Concat(Elements(child, tag)));
    }
}
