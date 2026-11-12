using PeachBrackets.Html.Dom;
using System.Linq;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>A table cell that is itself a multi-column container lays its content out in columns (css-multicol-1 §2).</summary>
    public class MulticolTableCellTests
    {
        private const string Table = "<table style='border-spacing:0;width:300pt'><tr><td id='c' style='padding:0;columns:2;column-gap:10pt'>{0}</td></tr></table>";

        [Fact]
        public async Task ABlockChildrenCell_PutsTheBlocksSideBySide()
        {
            var body = string.Format(Table, "<div id='a' style='width:100pt;height:10pt'></div><div id='b' style='width:100pt;height:10pt'></div>");
            var (root, _) = await LayoutAsync(Wrap(body));

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var cell = FindById(root, "c")!;

            Assert.Equal(cell.Location.X, a.Location.X, 0.01);
            Assert.True(b.Location.X > a.Location.X + 100, $"a={a.Location} b={b.Location} cell={cell.Location}");
            Assert.Equal(a.Location.Y, b.Location.Y, 0.01);
            Assert.Equal(10, a.ActualBottom - a.Location.Y, 0.01);
        }

        [Fact]
        public async Task ATextCell_LaysItsWordsInTwoColumns()
        {
            var body = string.Format(Table, "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima");
            var (root, _) = await LayoutAsync(Wrap(body));

            var cell = FindById(root, "c")!;
            var words = Descendants(cell).SelectMany(b => b.Words).ToList();

            Assert.Equal(12, words.Count);
            Assert.True(words.Select(w => w.Left).Distinct().Count() > 2);
        }

        private static System.Collections.Generic.IEnumerable<CssBox> Descendants(CssBox box)
        {
            yield return box;
            foreach (var child in box.Boxes)
                foreach (var d in Descendants(child)) yield return d;
        }
    }
}
