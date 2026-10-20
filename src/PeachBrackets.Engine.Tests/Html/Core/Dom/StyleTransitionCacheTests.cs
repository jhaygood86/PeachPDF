using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="StyleTransitionCache"/> hands the same instance to every box that makes the same change to the same style.
    /// </summary>
    public class StyleTransitionCacheTests
    {
        private sealed record Leaf(string? Name = null, int Size = 0);

        private sealed record Holder(Leaf Leaf);

        private static readonly Leaf LeafDefault = StyleTransitionCache.Root(new Leaf());
        private static readonly Holder HolderDefault = StyleTransitionCache.Root(new Holder(LeafDefault));

        private static readonly Func<Leaf, string?, Leaf> WithName = static (l, v) => l with { Name = v };
        private static readonly Func<Leaf, int, Leaf> WithSize = static (l, v) => l with { Size = v };
        private static readonly Func<Holder, Leaf, Holder> WithLeaf = static (h, v) => h with { Leaf = v };

        [Fact]
        public void SameChangeToSameStyle_GivesTheSameInstance()
        {
            var cache = new StyleTransitionCache();

            var first = cache.Set(LeafDefault, LeafDefault.Name, "a", WithName);
            var second = cache.Set(LeafDefault, LeafDefault.Name, "a", WithName);

            Assert.Same(first, second);
            Assert.Equal("a", first.Name);
            Assert.Equal(1, cache.Transitions);
        }

        [Fact]
        public void EqualValueInADifferentInstance_StillSharesTheResult()
        {
            var cache = new StyleTransitionCache();

            var first = cache.Set(LeafDefault, LeafDefault.Name, new string("abc".AsSpan()), WithName);
            var second = cache.Set(LeafDefault, LeafDefault.Name, new string("abc".AsSpan()), WithName);

            Assert.Same(first, second);
        }

        [Fact]
        public void DifferentValues_GiveDifferentInstances()
        {
            var cache = new StyleTransitionCache();

            var a = cache.Set(LeafDefault, LeafDefault.Name, "a", WithName);
            var b = cache.Set(LeafDefault, LeafDefault.Name, "b", WithName);

            Assert.NotSame(a, b);
            Assert.Equal("a", a.Name);
            Assert.Equal("b", b.Name);
        }

        [Fact]
        public void DifferentSetters_DoNotShareATransition()
        {
            var cache = new StyleTransitionCache();

            var named = cache.Set(LeafDefault, LeafDefault.Name, "x", WithName);
            var sized = cache.Set(LeafDefault, LeafDefault.Size, 7, WithSize);

            Assert.NotSame(named, sized);
            Assert.Equal(7, sized.Size);
            Assert.Null(sized.Name);
        }

        [Fact]
        public void ChainsFollowFromWhatTheCacheProduced()
        {
            var cache = new StyleTransitionCache();

            var one = cache.Set(cache.Set(LeafDefault, LeafDefault.Name, "a", WithName), 0, 3, WithSize);
            var two = cache.Set(cache.Set(LeafDefault, LeafDefault.Name, "a", WithName), 0, 3, WithSize);

            Assert.Same(one, two);
        }

        [Fact]
        public void UnchangedValue_ReturnsTheSameStyleAndRemembersNothing()
        {
            var cache = new StyleTransitionCache();

            Assert.Same(LeafDefault, cache.Set(LeafDefault, LeafDefault.Name, (string?)null, WithName));
            Assert.Same(LeafDefault, cache.Set(LeafDefault, LeafDefault.Size, 0, WithSize));
            Assert.Equal(0, cache.Transitions);
        }

        [Fact]
        public void NullValue_IsClonedNotRemembered()
        {
            var cache = new StyleTransitionCache();
            var named = cache.Set(LeafDefault, LeafDefault.Name, "a", WithName);

            var first = cache.Set(named, "a", (string?)null, WithName);
            var second = cache.Set(named, "a", (string?)null, WithName);

            Assert.NotSame(first, second);
            Assert.Null(first.Name);
            Assert.Equal(1, cache.Transitions);
        }

        [Fact]
        public void StyleFromOutsideTheSharedWorld_IsNeverFollowed()
        {
            var cache = new StyleTransitionCache();
            var outsider = new Leaf("own");

            var first = cache.Set(outsider, outsider.Name, "a", WithName);
            var second = cache.Set(outsider, outsider.Name, "a", WithName);

            Assert.NotSame(first, second);
            Assert.Equal(0, cache.Transitions);
        }

        [Fact]
        public void TheCacheIsPerInstance()
        {
            var one = new StyleTransitionCache();
            var other = new StyleTransitionCache();

            var a = one.Set(LeafDefault, LeafDefault.Name, "a", WithName);
            var b = other.Set(LeafDefault, LeafDefault.Name, "a", WithName);

            Assert.NotSame(a, b);
            Assert.Equal(a, b);
        }

        [Fact]
        public void AdoptingTheSameAreaIntoTheSameStyle_GivesTheSameInstance()
        {
            var cache = new StyleTransitionCache();
            var leaf = cache.Set(LeafDefault, LeafDefault.Name, "a", WithName);

            var first = cache.Adopt(HolderDefault, HolderDefault.Leaf, leaf, WithLeaf);
            var second = cache.Adopt(HolderDefault, HolderDefault.Leaf, leaf, WithLeaf);

            Assert.Same(first, second);
            Assert.Same(leaf, first.Leaf);
        }

        [Fact]
        public void AdoptingAnEqualButSeparateArea_IsADifferentTransition()
        {
            var cache = new StyleTransitionCache();
            var one = new Leaf("a");
            var two = new Leaf("a");

            var first = cache.Adopt(HolderDefault, HolderDefault.Leaf, one, WithLeaf);
            var second = cache.Adopt(HolderDefault, HolderDefault.Leaf, two, WithLeaf);

            Assert.NotSame(first, second);
            Assert.Same(one, first.Leaf);
            Assert.Same(two, second.Leaf);
        }

        [Fact]
        public void AdoptingTheCurrentArea_ReturnsTheStyle()
        {
            var cache = new StyleTransitionCache();

            Assert.Same(HolderDefault, cache.Adopt(HolderDefault, HolderDefault.Leaf, HolderDefault.Leaf, WithLeaf));
            Assert.Equal(0, cache.Transitions);
        }

        [Fact]
        public void PastTheBound_ItClonesInsteadOfRemembering()
        {
            var cache = new StyleTransitionCache();

            for (var i = 0; i < StyleTransitionCache.MaxTransitions; i++)
                cache.Set(LeafDefault, LeafDefault.Size, i + 1, WithSize);

            Assert.Equal(StyleTransitionCache.MaxTransitions, cache.Transitions);

            var first = cache.Set(LeafDefault, LeafDefault.Name, "late", WithName);
            var second = cache.Set(LeafDefault, LeafDefault.Name, "late", WithName);
            var adopted = cache.Adopt(HolderDefault, HolderDefault.Leaf, new Leaf("z"), WithLeaf);

            Assert.NotSame(first, second);
            Assert.Equal("late", first.Name);
            Assert.Equal("z", adopted.Leaf.Name);
            Assert.Equal(StyleTransitionCache.MaxTransitions, cache.Transitions);
        }

        [Fact]
        public async Task BoxesThatMakeTheSameChanges_ShareOneComputedStyle()
        {
            var items = string.Concat(Enumerable.Range(0, 20).Select(i => $"<p id='p{i}'>text {i}</p>"));
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<div>{items}</div>"));

            var first = LayoutHarness.FindById(root, "p0")!;

            for (var i = 1; i < 20; i++)
            {
                var other = LayoutHarness.FindById(root, $"p{i}")!;
                Assert.Same(first.ComputedStyle, other.ComputedStyle);
                Assert.Same(first.ComputedStyle.Font, other.ComputedStyle.Font);
            }
        }

        [Fact]
        public async Task ABoxWithItsOwnDeclaration_KeepsItWithoutDisturbingItsSiblings()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div><p id='a'>one</p><p id='b' style='font-size: 30pt; color: red'>two</p><p id='c'>three</p></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Same(a.ComputedStyle, c.ComputedStyle);
            Assert.NotSame(a.ComputedStyle, b.ComputedStyle);
            Assert.Equal("rgb(255, 0, 0)", b.Color);
            Assert.NotEqual("rgb(255, 0, 0)", a.Color);
            Assert.NotEqual(a.FontSize, b.FontSize);
        }

        [Fact]
        public async Task CustomProperties_AreNeverSharedBetweenBoxes()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='--x: 1'><p id='a'>one</p><p id='b'>two</p></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.NotNull(a.CustomProperties);
            Assert.NotSame(a.CustomProperties, b.CustomProperties);
        }
    }
}
