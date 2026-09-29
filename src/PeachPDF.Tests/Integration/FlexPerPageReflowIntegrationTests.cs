using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #196: a block-level flex container that continues across pages of different widths
    /// must follow css-break-3 §5.1 (each fragment sizes to its own fragmentainer) — the container's
    /// frame, and its items' sizes, track the page each fragment lands on. Asserted on the fragment
    /// tree (the layout/paint contract) rather than PDF content-stream substrings.
    /// </summary>
    public class FlexPerPageReflowIntegrationTests(ITestOutputHelper output)
    {
        // 612 x 300 sheet, @page margin 20pt 50pt, :first has no left margin -> page 0 content is
        // 562 wide (left edge 0), later pages 512 wide (left edge 50).
        internal const string Head = """
            <!DOCTYPE html><html><head><style>
            @page { margin: 20pt 50pt; }
            @page :first { margin-left: 0; }
            body { margin: 0; font: 12pt Arial; }
            #f { display: flex; }
            .i { border: 1px solid #333; break-inside: avoid; }
            p { margin: 0; }
            """;

        internal static async Task<HtmlContainerInt> BuildAsync(string html, double sheetH = 300)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new Size(
                612 - container.MarginLeft - container.MarginRight,
                sheetH - container.MarginTop - container.MarginBottom);
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new Size(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);
            return container;
        }

        internal static BoxFragment? FragmentOf(BoxFragment root, CssBox target)
        {
            if (ReferenceEquals(root.Box, target)) return root;
            foreach (var child in root.Children)
                if (FragmentOf(child, target) is { } found) return found;
            return null;
        }

        internal static CssBox? ById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
                if (ById(child, id) is { } found) return found;
            return null;
        }

        internal static string WordExtent(BoxFragment f)
        {
            var all = new List<TextFragment>();
            void Walk(BoxFragment b) { all.AddRange(b.Words); foreach (var c in b.Children) Walk(c); }
            Walk(f);
            if (all.Count == 0) return "-";
            return $"{all.Min(w => w.Rect.Left):F1}..{all.Max(w => w.Rect.Right):F1} y {all.Min(w => w.Rect.Top):F1}..{all.Max(w => w.Rect.Bottom):F1}";
        }

        internal static int CountWords(BoxFragment f) => f.Words.Count + f.Children.Sum(CountWords);

        internal static string Dump(HtmlContainerInt c, params string[] ids)
        {
            var sb = new StringBuilder();
            var tree = c.FragmentTree!;
            for (var p = 0; p < tree.Fragmentainers.Count; p++)
            {
                sb.AppendLine($"page {p}");
                foreach (var id in ids)
                {
                    var box = ById(c.Root!, id);
                    var f = box is null ? null : FragmentOf(tree.Fragmentainers[p].Root, box);
                    if (f is null) continue;
                    var r = f.WholeBoxRect;
                    var own = f.Rect;
                    sb.AppendLine($"  {id}: whole x={r.X:F1} y={r.Y:F1} w={r.Width:F1} h={r.Height:F1} | rect x={own.X:F1} y={own.Y:F1} w={own.Width:F1} h={own.Height:F1} | lines={f.Lines.Count} words={CountWords(f)} wordsX=[{WordExtent(f)}]");
                }
            }
            return sb.ToString();
        }

        [Fact]
        public async Task Probe_StraddlingRow()
        {
            var a = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"a{i}"));
            var b = string.Join(' ', Enumerable.Range(0, 150).Select(i => $"b{i}"));
            var c = await BuildAsync(Head + $$"""
                #f { background: #cde; } .i { flex: 1 1 auto; background: #fdc; }
                </style></head><body><div id="f"><div class="i" id="a">{{a}}</div><div class="i" id="b">{{b}}</div></div><p>after</p></body></html>
                """);
            output.WriteLine(Dump(c, "f", "a", "b"));
        }

        [Fact]
        public async Task Probe_StraddlingGrowRow()
        {
            string Text(int n, string k) => string.Join(' ', Enumerable.Range(0, n).Select(i => $"{k}{i}"));
            var c = await BuildAsync(Head + $$"""
                #f { background: #cde; } .i { flex: 1 1 0; background: #fdc; }
                </style></head><body><div id="f"><div class="i" id="i0">{{Text(300, "a")}}</div><div class="i" id="i1">{{Text(300, "b")}}</div><div class="i" id="i2">{{Text(150, "c")}}</div></div><p id="after">after</p></body></html>
                """);
            output.WriteLine(Dump(c, "f", "i0", "i1", "i2", "after"));
        }

        [Fact]
        public async Task Probe_NestedBlocks()
        {
            string Blocks(string k) => string.Concat(Enumerable.Range(0, 40).Select(n => $"<div class=\"b\" id=\"{k}{n}\"></div>"));
            var c = await BuildAsync(Head + $$"""
                #f { background: #cde; } .i { flex: 1 1 0; background: #fdc; }
                .b { height: 20pt; background: #9c9; margin: 1pt 2pt; }
                </style></head><body><div id="f"><div class="i" id="i0">{{Blocks("x")}}</div><div class="i" id="i1">{{Blocks("y")}}</div><div class="i" id="i2">{{Blocks("z")}}</div></div><p id="after">after</p></body></html>
                """);
            output.WriteLine(Dump(c, "f", "i0", "x0", "x12", "x13", "x14", "x27", "x28", "x39", "after"));
        }

        [Fact]
        public async Task Probe_NestedParagraphs()
        {
            string Text(int n, string k) => string.Join(' ', Enumerable.Range(0, n).Select(i => $"{k}{i}"));
            string Item(int n) => $"<div class=\"i\" id=\"i{n}\"><p id=\"p{n}a\">{Text(200, "a")}</p><p id=\"p{n}b\">{Text(200, "b")}</p></div>";
            var c = await BuildAsync(Head + $$"""
                #f { background: #cde; } .i { flex: 1 1 0; background: #fdc; }
                p { background: #9c9; margin: 2pt; }
                </style></head><body><div id="f">{{Item(0)}}{{Item(1)}}{{Item(2)}}</div><p id="after">after</p></body></html>
                """);
            output.WriteLine(Dump(c, "f", "i0", "p0a", "p0b", "i2", "p2a", "p2b", "after"));
        }

        [Fact]
        public async Task Probe_WrapRowUnstartedLines()
        {
            var items = string.Concat(Enumerable.Range(0, 9).Select(n => $"<div class=\"i\" id=\"g{n}\" style=\"height:150pt;flex:1 0 170pt\">g{n}</div>"));
            var c = await BuildAsync(Head + $$"""
                #f { background: #cde; flex-wrap: wrap; }
                </style></head><body><div id="f">{{items}}</div><p>after</p></body></html>
                """);
            output.WriteLine(Dump(c, "f", "g0", "g1", "g2", "g3", "g4", "g5", "g6", "g7", "g8"));
        }
    }
}
