using PeachBrackets.WinUI;
using PeachDrawing.Core;

namespace PeachBrackets.WinUI.Tests;

public class AnimationLogicTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void MotionIsAllowedOnlyWhenTheSystemPlaysAnimationEffects(bool system, bool allowed) =>
        Assert.Equal(allowed, HtmlViewLogic.SystemAllowsMotion(system));

    [Fact]
    public void ASurfaceThatCutOneRectangleIntoBands_IsPaintedFromOneCluster()
    {
        // One 100 x 60 rectangle as the surface delivers it after other invalidations: three bands.
        var bands = new[] { new Rect(0, 0, 100, 10), new Rect(0, 10, 100, 40), new Rect(0, 50, 100, 10) };

        var clusters = HtmlViewLogic.ClusterRegions(bands);

        var cluster = Assert.Single(clusters);
        Assert.Equal<int>(new[] { 0, 1, 2 }, cluster.Members);
        Assert.Equal(new Rect(0, 0, 100, 60), cluster.Bounds);
    }

    [Fact]
    public void RegionsFarApart_AreNotDraggedIntoOnePaint()
    {
        var far = new[] { new Rect(0, 0, 60, 60), new Rect(2000, 1500, 60, 60) };

        var clusters = HtmlViewLogic.ClusterRegions(far);

        Assert.Equal(2, clusters.Count);
        Assert.Equal<int>(new[] { 0 }, clusters[0].Members);
        Assert.Equal<int>(new[] { 1 }, clusters[1].Members);
    }

    [Fact]
    public void NoRegions_MakeNoClusters() => Assert.Empty(HtmlViewLogic.ClusterRegions([]));

    [Theory]
    [InlineData(100f, 0f, 512f)]
    [InlineData(600f, 0f, 1024f)]
    [InlineData(600f, 2048f, 2048f)]
    [InlineData(1025f, 1024f, 1536f)]
    public void ARenderTargetGrowsInSteps_AndNeverShrinksWhileInUse(float needed, float kept, float expected) =>
        Assert.Equal(expected, HtmlViewLogic.TargetExtent(needed, kept));
}
