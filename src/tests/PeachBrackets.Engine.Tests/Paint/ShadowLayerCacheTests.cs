using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint;
using PeachDrawing.Core;

namespace PeachBrackets.Engine.Tests.Paint;

public class ShadowLayerCacheTests
{
    private sealed class FakeLayer(bool canDraw = true) : RetainedLayer(new Rect(0, 0, 1, 1))
    {
        public bool Disposed { get; private set; }
        public override Canvas Recorder => throw new NotSupportedException();
        public override bool IsSealed => true;
        public override void Seal() { }
        public override bool CanDraw(Canvas target) => canDraw && !Disposed;
        public override void Draw(Canvas target, double opacity = 1.0) { }
        public override void Dispose() => Disposed = true;
    }

    private static ShadowKey Key(double width) => new(width, 10, 8, 0, 0, width, 10, PaintColor.Empty, default, false, default, 1, 1);

    private static Canvas AnyCanvas() => null!;

    [Fact]
    public void ALayerMadeForAShape_IsFoundAgainForTheSameShape()
    {
        using var cache = new ShadowLayerCache();
        var layer = new FakeLayer();
        cache.Add(Key(40), layer);

        Assert.True(cache.TryGet(Key(40), AnyCanvas(), out var found));
        Assert.Same(layer, found);
        Assert.False(cache.TryGet(Key(41), AnyCanvas(), out _));
    }

    [Fact]
    public void ALayerTheCanvasCannotDraw_IsDroppedAndDisposed()
    {
        using var cache = new ShadowLayerCache();
        var layer = new FakeLayer(canDraw: false);
        cache.Add(Key(40), layer);

        Assert.False(cache.TryGet(Key(40), AnyCanvas(), out _));
        Assert.True(layer.Disposed);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ThePastCapacity_OldestShadowsGo()
    {
        using var cache = new ShadowLayerCache();
        var first = new FakeLayer();
        cache.Add(Key(1), first);
        for (var i = 2; i <= ShadowLayerCache.Capacity + 1; i++) cache.Add(Key(i), new FakeLayer());

        Assert.Equal(ShadowLayerCache.Capacity, cache.Count);
        Assert.True(first.Disposed);
        Assert.False(cache.TryGet(Key(1), AnyCanvas(), out _));
    }

    [Fact]
    public void ReplacingAShape_DisposesTheOldLayer_AndDisposingTheCacheDisposesAll()
    {
        var cache = new ShadowLayerCache();
        var old = new FakeLayer();
        var current = new FakeLayer();
        cache.Add(Key(5), old);
        cache.Add(Key(5), current);
        Assert.True(old.Disposed);

        cache.Dispose();

        Assert.True(current.Disposed);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void RadiiThatAreTheSame_AreTheSameKey()
    {
        var a = new BorderRadii(4, 4, 4, 4, 4, 4, 4, 4);
        var b = new BorderRadii(4, 4, 4, 4, 4, 4, 4, 4);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new BorderRadii(4, 4, 4, 4, 4, 4, 4, 5));
    }

    [Fact]
    public void AShapeDroppedAndMadeAgain_IsNotEvictedBeforeOlderOnes()
    {
        using var cache = new ShadowLayerCache();
        cache.Add(Key(1), new FakeLayer(canDraw: false));
        Assert.False(cache.TryGet(Key(1), AnyCanvas(), out _));

        var second = new FakeLayer();
        cache.Add(Key(2), second);
        for (var i = 3; i <= ShadowLayerCache.Capacity; i++) cache.Add(Key(i), new FakeLayer());

        var again = new FakeLayer();
        cache.Add(Key(1), again);
        cache.Add(Key(ShadowLayerCache.Capacity + 1), new FakeLayer());

        // The oldest live shadow goes; the one that was just made again stays (its first queue entry is stale).
        Assert.True(second.Disposed);
        Assert.False(again.Disposed);
        Assert.True(cache.TryGet(Key(1), AnyCanvas(), out _));
    }
}
