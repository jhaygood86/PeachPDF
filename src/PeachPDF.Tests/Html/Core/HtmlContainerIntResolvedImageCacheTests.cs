using PeachPDF.Html.Adapters;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Unit tests for <c>HtmlContainerInt</c>'s per-render resolved-image cache (added for issue #1172),
    /// exercised directly against the internal API rather than through a full HTML render, since its
    /// defensive overwrite guard has no reachable trigger through today's single-threaded,
    /// sequential-await image loading (every real caller checks <c>TryGetResolvedImageResource</c> first
    /// and never reaches a second <c>CacheResolvedImageResource</c> call for the same key).
    /// </summary>
    public class HtmlContainerIntResolvedImageCacheTests
    {
        private sealed class DisposeTrackingImage : RImage
        {
            public bool Disposed { get; private set; }
            public override double Width => 1;
            public override double Height => 1;
            public override bool Interpolate { get; set; }
            public override void Dispose() => Disposed = true;
        }

        private sealed class ThrowingImage : RImage
        {
            public override double Width => 1;
            public override double Height => 1;
            public override bool Interpolate { get; set; }
            public override void Dispose() => throw new InvalidOperationException("boom");
        }

        [Fact]
        public async Task CacheResolvedImageResource_TryGetMiss_ThenHit_ReturnsCachedResource()
        {
            var (_, container) = await LayoutHarness.LayoutAsync("<html><body></body></html>");

            Assert.False(container.TryGetResolvedImageResource("https://example.test/a.png", out _));

            var image = new DisposeTrackingImage();
            container.CacheResolvedImageResource("https://example.test/a.png", image, null);

            Assert.True(container.TryGetResolvedImageResource("https://example.test/a.png", out var resource));
            Assert.Same(image, resource.Image);
            Assert.Null(resource.SvgDocument);
        }

        [Fact]
        public async Task CacheResolvedImageResource_OverwritingExistingEntry_DisposesThePreviousImage()
        {
            var (_, container) = await LayoutHarness.LayoutAsync("<html><body></body></html>");

            var first = new DisposeTrackingImage();
            var second = new DisposeTrackingImage();

            container.CacheResolvedImageResource("https://example.test/a.png", first, null);
            container.CacheResolvedImageResource("https://example.test/a.png", second, null);

            Assert.True(first.Disposed);
            Assert.False(second.Disposed);
            Assert.True(container.TryGetResolvedImageResource("https://example.test/a.png", out var resource));
            Assert.Same(second, resource.Image);
        }

        [Fact]
        public async Task Dispose_OneCachedImageThrowsOnDispose_StillClearsTheRestOfTheCache()
        {
            // Dispose(bool)'s cached-image loop swallows a throwing Dispose() (matching the pre-existing
            // swallow-all around Root's own disposal) and always clears the dictionary in its finally, so
            // one broken RImage can't leak every other cached resource for this render.
            var (_, container) = await LayoutHarness.LayoutAsync("<html><body></body></html>");

            container.CacheResolvedImageResource("https://example.test/a.png", new ThrowingImage(), null);
            var second = new DisposeTrackingImage();
            container.CacheResolvedImageResource("https://example.test/b.png", second, null);

            container.Dispose();

            Assert.True(second.Disposed);
            Assert.False(container.TryGetResolvedImageResource("https://example.test/a.png", out _));
            Assert.False(container.TryGetResolvedImageResource("https://example.test/b.png", out _));
        }
    }
}
