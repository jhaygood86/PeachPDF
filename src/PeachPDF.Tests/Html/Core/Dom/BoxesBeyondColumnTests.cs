using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="BoxesBeyondColumn"/> stands in for the set of every box after a column's break - the rest of a long
    /// document - without building it. These pin that it answers exactly what that set would have.
    /// </summary>
    public class BoxesBeyondColumnTests
    {
        private static async Task<(CssBox Parent, List<CssBox> Items, CssBox Elsewhere)> Tree()
        {
            var items = string.Concat(Enumerable.Range(0, 12).Select(i => $"<div id='i{i}'><span>t{i}</span></div>"));
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<div id='parent'>{items}</div><div id='other'>x</div>"));

            var parent = LayoutHarness.FindById(root, "parent")!;
            var elsewhere = LayoutHarness.FindById(root, "other")!;

            return (parent, parent.Boxes, elsewhere);
        }

        [Fact]
        public async Task None_ContainsNothing()
        {
            var (_, items, _) = await Tree();

            Assert.True(BoxesBeyondColumn.None.IsEmpty);
            Assert.All(items, box => Assert.False(BoxesBeyondColumn.None.Contains(box)));
        }

        [Fact]
        public async Task ARange_ContainsTheChildrenFromItsStartOn_AndNoneBefore()
        {
            var (_, items, elsewhere) = await Tree();
            var beyond = new BoxesBeyondColumn();
            beyond.AddFrom(items, 5);

            for (var i = 0; i < items.Count; i++)
                Assert.Equal(i >= 5, beyond.Contains(items[i]));

            Assert.False(beyond.Contains(elsewhere));
            Assert.False(beyond.IsEmpty);
        }

        [Fact]
        public async Task ARangeStartingPastTheEnd_AddsNothing()
        {
            var (_, items, _) = await Tree();
            var beyond = new BoxesBeyondColumn();

            beyond.AddFrom(items, items.Count);

            Assert.True(beyond.IsEmpty);
        }

        [Fact]
        public async Task AnAddedBox_IsContainedWhereverItSits()
        {
            var (_, items, elsewhere) = await Tree();
            var beyond = new BoxesBeyondColumn();
            beyond.Add(elsewhere);
            beyond.Add(items[1]);

            Assert.True(beyond.Contains(elsewhere));
            Assert.True(beyond.Contains(items[1]));
            Assert.False(beyond.Contains(items[0]));
            Assert.False(beyond.Contains(items[2]));
        }

        [Fact]
        public async Task RangesAndAddedBoxesCombine_AndEnumerateIsExactlyWhatContainsAnswers()
        {
            var (parent, items, elsewhere) = await Tree();
            var inner = items[3].Boxes;
            var beyond = new BoxesBeyondColumn();
            beyond.AddFrom(items, 9);
            beyond.AddFrom(inner, 0);
            beyond.Add(items[0]);

            var all = new List<CssBox>();
            Collect(parent, all);
            all.Add(elsewhere);

            var enumerated = beyond.Enumerate().ToHashSet();
            foreach (var box in all)
                Assert.Equal(enumerated.Contains(box), beyond.Contains(box));

            Assert.Contains(items[9], enumerated);
            Assert.Contains(items[0], enumerated);
            Assert.Contains(inner[0], enumerated);
            Assert.DoesNotContain(items[5], enumerated);
        }

        [Fact]
        public async Task AListThatChanges_IsAnsweredFromItsNewShape()
        {
            var (_, items, _) = await Tree();
            var moved = items[11];
            var beyond = new BoxesBeyondColumn();
            beyond.AddFrom(items, 8);

            Assert.True(beyond.Contains(items[8]));

            // The map was built at 12 children; the list is another shape now, and the questions must follow it.
            items.Insert(0, items[11]);
            items.RemoveAt(items.Count - 1);
            items.Add(moved);

            var expected = beyond.Enumerate().ToHashSet();
            foreach (var box in items)
                Assert.Equal(expected.Contains(box), beyond.Contains(box));
        }

        [Fact]
        public async Task ABoxAppearingTwiceInTheList_IsBeyondIfAnyOccurrenceIs()
        {
            var (_, items, _) = await Tree();
            var twice = items[2];
            items.Add(twice);
            var beyond = new BoxesBeyondColumn();
            beyond.AddFrom(items, 10);

            Assert.True(beyond.Contains(twice));
        }

        private static void Collect(CssBox box, List<CssBox> into)
        {
            into.Add(box);
            foreach (var child in box.Boxes) Collect(child, into);
        }
    }
}
