using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Svg;
using System.Xml.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The <c>all</c> shorthand (CSS Cascade 4 §3.2): every property except <c>direction</c>, <c>unicode-bidi</c>
    /// and custom properties takes one CSS-wide keyword. The regression guards at the top make sure a property
    /// added later is covered automatically and cannot silently opt out.
    /// </summary>
    public class AllShorthandTests
    {
        private static readonly string[] Keywords = ["initial", "inherit", "unset", "revert", "revert-layer"];

        // The reviewed exclusion list: anything not named here must be a longhand of `all`.
        private static bool IsReviewedExclusion(string name) =>
            name is "direction" or "unicode-bidi"
            || VendorPropertyAliases.Canonicalize(name) != name // an alias spelling; its standard name is the longhand
            || name.StartsWith("-webkit-box-", StringComparison.Ordinal)
            || name.StartsWith("-moz-box-", StringComparison.Ordinal);

        private static IEnumerable<string> AllLonghandNames() =>
            PropertyNamesFromFactory().Where(n => !PropertyFactory.Instance.IsShorthand(n));

        // Every longhand PropertyFactory knows, found by probing the property-name table.
        private static IEnumerable<string> PropertyNamesFromFactory() =>
            typeof(PropertyNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Select(f => (string)f.GetValue(null)!)
                .Concat(CssPropertyRegistry.InitialValues.Keys)
                .Distinct()
                .Where(n => PropertyFactory.Instance.CreateLonghand(n) is not null);

        // ── regression guards ──────────────────────────────────────────────────

        [Fact]
        public void EveryLonghand_IsCoveredByAll_UnlessReviewedExclusion()
        {
            var covered = PropertyFactory.Instance.GetLonghands("all").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = AllLonghandNames().Where(n => !covered.Contains(n) && !IsReviewedExclusion(n)).ToList();

            Assert.True(missing.Count == 0, "longhands missing from `all`: " + string.Join(", ", missing));
            Assert.DoesNotContain("direction", covered);
            Assert.DoesNotContain("unicode-bidi", covered);
            Assert.All(covered, n => Assert.False(IsReviewedExclusion(n), n));
        }

        [Fact]
        public void EveryRegistryProperty_IsCoveredByAll()
        {
            var covered = PropertyFactory.Instance.GetLonghands("all").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = CssPropertyRegistry.InitialValues.Keys.Where(n => !covered.Contains(n) && !IsReviewedExclusion(n)).ToList();

            Assert.True(missing.Count == 0, "css-properties.json properties missing from `all`: " + string.Join(", ", missing));
        }

        [Fact]
        public void EverySvgRegistryProperty_IsCoveredByAll()
        {
            var covered = PropertyFactory.Instance.GetLonghands("all").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = SvgPropertyRegistry.InitialValues.Keys.Where(n => !covered.Contains(n)).ToList();

            Assert.True(missing.Count == 0, "SVG properties missing from `all`: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryCoveredLonghand_AcceptsEveryCssWideKeyword()
        {
            var failures = new List<string>();
            foreach (var name in PropertyFactory.Instance.GetLonghands("all"))
                foreach (var keyword in Keywords)
                {
                    var property = PropertyFactory.Instance.CreateLonghand(name)!;
                    if (!property.TrySetValue(TokenValue.FromString(keyword)))
                        failures.Add($"{name}: {keyword}");
                }

            Assert.True(failures.Count == 0, "longhands rejecting a CSS-wide keyword: " + string.Join(", ", failures));
        }

        [Fact]
        public async Task AllInitial_MatchesDeclaringEveryRegistryPropertyInitial()
        {
            var names = CssPropertyRegistry.InitialValues.Keys.Where(n => !IsReviewedExclusion(n)).OrderBy(n => n).ToList();
            var each = string.Join(";", names.Select(n => $"{n}: initial"));
            var html = $$"""
                <!DOCTYPE html><html><head><style>#parent { color: red; margin-top: 9px; }</style></head><body>
                <div id="parent"><div id="viaAll" style="all: initial"></div><div id="viaEach" style="{{each}}"></div></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var viaAll = FindById(root, "viaAll")!;
            var viaEach = FindById(root, "viaEach")!;

            var differing = names.Where(n => CssUtils.GetPropertyValue(viaAll, n) != CssUtils.GetPropertyValue(viaEach, n)).ToList();
            Assert.True(differing.Count == 0, "all: initial differs from per-property initial for: " + string.Join(", ", differing));
        }

        // ── CSS-OM ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("initial")]
        [InlineData("inherit")]
        [InlineData("unset")]
        [InlineData("revert")]
        [InlineData("revert-layer")]
        public void All_ExpandsToLonghands_ExceptDirectionUnicodeBidiAndCustomProperties(string keyword)
        {
            var sheet = new StylesheetParser().Parse($"p {{ --x: 1; all: {keyword}; }}");
            var style = ((IStyleRule)sheet.Rules[0]).Style;
            var names = style.Select(p => p.Name).ToList();

            Assert.Contains("color", names);
            Assert.Contains("margin-top", names);
            Assert.Contains("fill", names);
            Assert.DoesNotContain("all", names);
            Assert.DoesNotContain("direction", names);
            Assert.DoesNotContain("unicode-bidi", names);
            Assert.Contains("--x", names); // untouched, and no keyword leaked onto it
            Assert.Equal("1", style.First(p => p.Name == "--x").Value);
            Assert.All(style.Where(p => p.Name != "--x"), p => Assert.Equal(keyword, p.Value));
        }

        [Theory]
        [InlineData("red")]
        [InlineData("none")]
        [InlineData("var(--x)")]
        [InlineData("initial initial")]
        public void All_RejectsAnythingButAKeyword(string value)
        {
            var sheet = new StylesheetParser().Parse($"p {{ all: {value}; }}");
            var style = ((IStyleRule)sheet.Rules[0]).Style;

            Assert.Empty(style);
        }

        [Fact]
        public void All_IsNeverReconstructedFromLonghandsWhenSerializing()
        {
            var sheet = new StylesheetParser().Parse("p { all: initial; }");
            var text = ((IStyleRule)sheet.Rules[0]).Style.CssText;

            Assert.DoesNotContain("all:", text);
            Assert.Contains("color: initial", text);
        }

        [Fact]
        public void All_IsImportantOnEveryLonghand()
        {
            var sheet = new StylesheetParser().Parse("p { all: initial !important; }");

            Assert.All(((IStyleRule)sheet.Rules[0]).Style, p => Assert.True(p.IsImportant, p.Name));
        }

        [Fact]
        public async Task All_Supports_AcceptsKeywordsOnly()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  @supports (all: initial) { #e { color: red; } }
                  @supports (all: red) { #e { margin-top: 40px; } }
                </style></head><body><div id="e">x</div></body></html>
                """;

            var e = FindById(await BuildBoxTree(html), "e")!;

            Assert.Equal("rgb(255, 0, 0)", e.Color);
            Assert.Equal("0", e.MarginTop.ToString());
        }

        // ── cascade: box properties ───────────────────────────────────────────

        [Fact]
        public async Task AllInitial_ResetsInheritedAndNonInheritedProperties()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="p" style="color: blue; font-size: 30px; margin-top: 12px; background-color: red">
                  <div id="c" style="all: initial">x</div>
                </div></body></html>
                """;

            var c = FindById(await BuildBoxTree(html), "c")!;

            Assert.Equal("black", c.Color);
            Assert.Equal("medium", c.FontSize.ToString());
            Assert.Equal("0", c.MarginTop.ToString());
            Assert.Equal("inline", c.Display.ToString()); // initial display, not the UA block of <div>
        }

        [Fact]
        public async Task AllInherit_TakesParentNonInheritedValues()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="p" style="margin-top: 30px; background-color: red">
                  <div id="c" style="all: inherit">x</div>
                </div></body></html>
                """;

            var c = FindById(await BuildBoxTree(html), "c")!;

            Assert.Equal("30px", c.MarginTop.ToString());
            Assert.Equal("rgb(255, 0, 0)", c.BackgroundColor);
        }

        [Fact]
        public async Task AllUnset_InheritsOnlyInheritedProperties()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="p" style="color: blue; margin-top: 30px">
                  <div id="c" style="all: unset">x</div>
                </div></body></html>
                """;

            var c = FindById(await BuildBoxTree(html), "c")!;

            Assert.Equal("rgb(0, 0, 255)", c.Color);
            Assert.Equal("0", c.MarginTop.ToString());
        }

        [Fact]
        public async Task All_LeavesDirectionAlone()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="p" dir="rtl"><div id="c" style="direction: rtl; all: initial">x</div></div>
                </body></html>
                """;

            var c = FindById(await BuildBoxTree(html), "c")!;

            Assert.Equal("rtl", c.Direction.ToString());
        }

        [Fact]
        public async Task All_FollowsDeclarationOrder()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="a" style="color: red; all: initial">x</div>
                <div id="b" style="all: initial; color: red">x</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);

            Assert.Equal("black", FindById(root, "a")!.Color);
            Assert.Equal("rgb(255, 0, 0)", FindById(root, "b")!.Color);
        }

        [Fact]
        public async Task All_Important_BeatsLaterNormalLonghand()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  #e { all: initial !important; }
                  #e { color: red; }
                </style></head><body><div id="e">x</div></body></html>
                """;

            Assert.Equal("black", FindById(await BuildBoxTree(html), "e")!.Color);
        }

        [Fact]
        public async Task AllRevert_RollsBackToUserAgentStyle()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  #e { display: inline; margin-top: 77px; }
                  #e { all: revert; }
                </style></head><body><div id="e">x</div></body></html>
                """;

            var e = FindById(await BuildBoxTree(html), "e")!;

            Assert.Equal("block", e.Display.ToString()); // the UA sheet's display for <div>, not the author's inline
            Assert.Equal("0", e.MarginTop.ToString());
        }

        [Fact]
        public async Task AllRevertLayer_RollsBackToLowerLayer()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  @layer a { #e { color: red; margin-top: 11px; } }
                  @layer b { #e { color: blue; margin-top: 22px; all: revert-layer; } }
                </style></head><body><div id="e">x</div></body></html>
                """;

            var e = FindById(await BuildBoxTree(html), "e")!;

            Assert.Equal("rgb(255, 0, 0)", e.Color);
            Assert.Equal("11px", e.MarginTop.ToString());
        }

        [Fact]
        public async Task All_DoesNotTouchCustomProperties()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="e" style="--x: 5px; all: initial; margin-top: var(--x)">x</div>
                </body></html>
                """;

            Assert.Equal("5px", FindById(await BuildBoxTree(html), "e")!.MarginTop.ToString());
        }

        [Fact]
        public async Task All_AppliesToMathMlElements()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="color: green"><math id="m" style="color: red; all: initial"><mi>x</mi></math></div>
                </body></html>
                """;

            var m = FindById(await BuildBoxTree(html), "m")!;

            Assert.Equal("black", m.Color);
        }

        // ── cascade: SVG ──────────────────────────────────────────────────────

        private static IReadOnlyDictionary<string, string?> SvgMatched(string rule)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg"><style>{rule}</style><rect class="s"/></svg>""";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            var rect = root.Descendants().Single(e => e.Name.LocalName == "rect");

            return SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(rect, root), cssData, "print")!;
        }

        [Fact]
        public void Svg_AllInitial_ResetsPaintToSvgInitialValues()
        {
            var matched = SvgMatched(".s { fill: red; stroke: blue; stroke-width: 9; all: initial; }");

            Assert.Equal(SvgPropertyRegistry.GetInitialValue("fill"), matched["fill"]);
            Assert.Equal(SvgPropertyRegistry.GetInitialValue("stroke"), matched["stroke"]);
            Assert.Equal(SvgPropertyRegistry.GetInitialValue("stroke-width"), matched["stroke-width"]);
        }

        [Fact]
        public void Svg_AllInherit_PassesInheritThrough()
        {
            var matched = SvgMatched(".s { fill: red; all: inherit; }");

            Assert.Equal("inherit", matched["fill"]);
        }

        [Fact]
        public void Svg_AllUnsetAndRevert_SignalInheritedOrInitial()
        {
            Assert.Null(SvgMatched(".s { fill: red; all: unset; }")["fill"]);
            Assert.Null(SvgMatched(".s { fill: red; all: revert; }")["fill"]);
        }

        [Fact]
        public async Task Svg_InlineSvgBox_AllInitialResetsHtmlCascade()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="color: green"><svg id="s" xmlns="http://www.w3.org/2000/svg" style="color: red; all: initial"><rect width="1" height="1"/></svg></div>
                </body></html>
                """;

            Assert.Equal("black", FindById(await BuildBoxTree(html), "s")!.Color);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
