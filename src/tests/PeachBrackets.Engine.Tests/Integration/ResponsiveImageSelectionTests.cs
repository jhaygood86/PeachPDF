using PeachBrackets.Engine.Tests.TestSupport;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>Device pixel ratio, selection policy, fallback and re-selection of responsive images (srcset/sizes/&lt;picture&gt;).</summary>
    public class ResponsiveImageSelectionTests
    {
        [Theory]
        [InlineData(1, "rgb(255, 0, 0)")]
        [InlineData(1.5, "rgb(255, 0, 0)")]
        [InlineData(2, "rgb(0, 0, 255)")]
        [InlineData(3, "rgb(0, 0, 255)")]
        public async Task ResolutionMediaFeature_ReflectsDevicePixelRatio(double dpr, string expected)
        {
            const string html = "<html><head><style>#el { color: rgb(255, 0, 0); } @media (min-resolution: 2dppx) { #el { color: rgb(0, 0, 255); } }</style></head><body><p id='el'>x</p></body></html>";
            var (root, _) = await LayoutHarness.LayoutAsync(html, configureContainer: c => c.DevicePixelRatio = dpr);

            Assert.Equal(expected, LayoutHarness.FindById(root, "el")!.Color);
        }

        [Theory]
        [InlineData(1, "rgb(255, 0, 0)")]
        [InlineData(2, "rgb(0, 0, 255)")]
        public async Task DevicePixelRatioMediaFeature_ReflectsDevicePixelRatio(double dpr, string expected)
        {
            const string html = "<html><head><style>#el { color: rgb(255, 0, 0); } @media (min-device-pixel-ratio: 2) { #el { color: rgb(0, 0, 255); } }</style></head><body><p id='el'>x</p></body></html>";
            var (root, _) = await LayoutHarness.LayoutAsync(html, configureContainer: c => c.DevicePixelRatio = dpr);

            Assert.Equal(expected, LayoutHarness.FindById(root, "el")!.Color);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public async Task DevicePixelRatio_InvalidValue_IsTreatedAsOne(double dpr)
        {
            var (_, container) = await LayoutHarness.LayoutAsync("<p>x</p>", configureContainer: c => c.DevicePixelRatio = dpr);

            Assert.Equal(1, container.DevicePixelRatio);
        }
    }
}
