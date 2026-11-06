using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.Svg;
using PeachBrackets.Svg.Smil;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>The SMIL animation model: clock values and begin lists, the timing of intervals, and what each kind of animation gives at a time (SMIL Animation 3).</summary>
    public class SmilAnimationTests
    {
        [Theory]
        [InlineData("5s", 5)]
        [InlineData("100ms", 0.1)]
        [InlineData("1.5min", 90)]
        [InlineData("2h", 7200)]
        [InlineData("02:30", 150)]
        [InlineData("01:02:30.5", 3750.5)]
        [InlineData("3", 3)]
        [InlineData("-2s", -2)]
        [InlineData("+ 4s", 4)]
        public void AClockValue_IsReadInSeconds(string text, double expected)
        {
            Assert.True(SmilAnimation.TryParseClock(text, out var seconds));
            Assert.Equal(expected, seconds, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("soon")]
        [InlineData("1:2:3:4")]
        [InlineData("s")]
        public void AMalformedClockValue_IsRejected(string text) => Assert.False(SmilAnimation.TryParseClock(text, out _));

        [Fact]
        public void ABeginList_KeepsOffsetsAndSyncBases_AndLeavesOutWhatNeverHappensHere()
        {
            var times = SmilAnimation.ParseTimes("0s; 2s; other.end+1s; first.begin; click; indefinite; wat.nope");

            Assert.Equal(4, times.Count);
            Assert.Equal(new SmilTime(0), times[0]);
            Assert.Equal(new SmilTime(2), times[1]);
            Assert.Equal(new SmilTime(1, "other", true), times[2]);
            Assert.Equal(new SmilTime(0, "first", false), times[3]);
        }

        [Fact]
        public void TheActiveDuration_FollowsRepeatCountRepeatDurationMinAndMax()
        {
            SmilAnimation Make(double? dur, double count = double.NaN, double repeatDur = double.NaN, double min = double.NaN, double max = double.NaN) =>
                new() { Kind = SmilKind.Animate, Element = null!, Target = null!, Duration = dur, RepeatCount = count, RepeatDuration = repeatDur, Min = min, Max = max };

            Assert.Equal(2, Make(2).ActiveDuration());
            Assert.Equal(6, Make(2, count: 3).ActiveDuration());
            Assert.Equal(5, Make(2, repeatDur: 5).ActiveDuration());
            Assert.Equal(4, Make(2, count: 2, repeatDur: 10).ActiveDuration());
            Assert.Equal(1, Make(2, max: 1).ActiveDuration());
            Assert.Equal(3, Make(2, min: 3).ActiveDuration());
            Assert.True(double.IsPositiveInfinity(Make(null).ActiveDuration()));
            Assert.True(double.IsPositiveInfinity(Make(2, count: double.PositiveInfinity).ActiveDuration()));
        }

        [Fact]
        public void NumbersWithTheSameTextBetweenThem_AreMixedOneByOne()
        {
            Assert.Equal("15", SmilValues.Mix(null, "x", "10", "20", 0.5, null));
            Assert.Equal("M 5 5 L 15 5", SmilValues.Mix(null, "d", "M 0 0 L 10 0", "M 10 10 L 20 10", 0.5, null));
            Assert.Equal("5px", SmilValues.Mix(null, "r", "0px", "10px", 0.5, null));
            Assert.Equal("0 0 150 75", SmilValues.Mix(null, "viewBox", "0 0 100 50", "0 0 200 100", 0.5, null));
        }

        [Fact]
        public void ValuesThatDoNotLineUp_SwitchAtTheHalfwayPoint()
        {
            Assert.Equal("a", SmilValues.Mix(null, "visibility", "a", "b", 0.4, null));
            Assert.Equal("b", SmilValues.Mix(null, "visibility", "a", "b", 0.6, null));
        }

        [Fact]
        public void ValuesAreAddedAndScaledNumberByNumber()
        {
            Assert.Equal("30", SmilValues.Add("10", "20"));
            Assert.Equal("1 2 3 4", SmilValues.Add("0 1 2 3", "1 1 1 1"));
            Assert.Null(SmilValues.Add("10", "20px"));
            Assert.Equal("30", SmilValues.Scale("10", 3));
            Assert.Null(SmilValues.Scale("none", 3));
            Assert.Equal(5, SmilValues.Distance("0 0", "3 4"));
            Assert.Equal(0, SmilValues.Distance("0", "a"));
        }

        [Fact]
        public void ASplineWithTheDiagonalControlPoints_IsLinear_AndAnEaseInStartsSlow()
        {
            Assert.Equal(0.3, SmilValues.Spline("0.3 0.3 0.7 0.7", 0.3), 4);
            Assert.True(SmilValues.Spline("0.5 0 1 1", 0.25) < 0.25);
            Assert.True(SmilValues.Spline("0 0 0.5 1", 0.25) > 0.25);
            Assert.Equal(0.4, SmilValues.Spline("bad", 0.4));
        }

        private static async Task<(SmilSet Set, CssBoxSvg Svg)> ReadAsync(string svgBody)
        {
            var html = $"<!DOCTYPE html><html><body><svg width=\"200\" height=\"100\">{svgBody}</svg></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationsEnabled = true };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var svg = Assert.IsType<CssBoxSvg>(DomUtils.GetBoxByTagName(container.Root!, "svg"));
            return (Assert.IsType<SmilSet>(svg.SmilAnimations), svg);
        }

        private static string? SampleAt(SmilSet set, CssBoxSvg svg, string attribute, double time, string? underlying = null) =>
            set.Sample(set.Animations[0].Target, attribute, time, () => underlying, svg.SmilParser);

        [Fact]
        public async Task ADiscreteAnimation_StepsThroughItsValuesAtTheKeyTimes()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"visibility\" values=\"a;b;c\" calcMode=\"discrete\" dur=\"3s\" fill=\"freeze\"/></rect>");

            Assert.Equal("a", SampleAt(set, svg, "visibility", 0.5));
            Assert.Equal("b", SampleAt(set, svg, "visibility", 1.5));
            Assert.Equal("c", SampleAt(set, svg, "visibility", 2.5));
            Assert.Equal("c", SampleAt(set, svg, "visibility", 9));
        }

        [Fact]
        public async Task KeyTimes_PlaceTheValuesAlongTheDuration()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" values=\"0;100;100\" keyTimes=\"0;0.25;1\" dur=\"4s\" fill=\"freeze\"/></rect>");

            Assert.Equal("50", SampleAt(set, svg, "x", 0.5));
            Assert.Equal("100", SampleAt(set, svg, "x", 2));
        }

        [Fact]
        public async Task APacedAnimation_MovesAtASteadyRateWhateverTheValueSpacing()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" values=\"0;10;100\" calcMode=\"paced\" dur=\"10s\" fill=\"freeze\"/></rect>");

            // 100 units over 10 seconds: 10 per second, so the second value is reached after one second.
            Assert.Equal("10", SampleAt(set, svg, "x", 1));
            Assert.Equal("55", SampleAt(set, svg, "x", 5.5));
        }

        [Fact]
        public async Task ASplineAnimation_ShapesEachSegmentWithItsKeySpline()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"100\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.5 0 1 1\" dur=\"2s\" fill=\"freeze\"/></rect>");

            Assert.True(double.Parse(SampleAt(set, svg, "x", 0.5)!, System.Globalization.CultureInfo.InvariantCulture) < 25);
        }

        [Fact]
        public async Task AccumulateSum_StartsEachRepeatWhereTheLastEnded()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"10\" dur=\"1s\" repeatCount=\"3\" accumulate=\"sum\" fill=\"freeze\"/></rect>");

            Assert.Equal("5", SampleAt(set, svg, "x", 0.5));
            Assert.Equal("15", SampleAt(set, svg, "x", 1.5));
            Assert.Equal("25", SampleAt(set, svg, "x", 2.5));
            Assert.Equal("30", SampleAt(set, svg, "x", 4));
        }

        [Fact]
        public async Task AdditiveSum_AddsToTheUnderlyingValue_AndByAloneIsAddition()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"10\" dur=\"2s\" additive=\"sum\" fill=\"freeze\"/></rect>");
            Assert.Equal("105", SampleAt(set, svg, "x", 1, underlying: "100"));

            var (byOnly, byOnlySvg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" by=\"10\" dur=\"2s\" fill=\"freeze\"/></rect>");
            Assert.Equal("105", SampleAt(byOnly, byOnlySvg, "x", 1, underlying: "100"));
            Assert.Equal("110", SampleAt(byOnly, byOnlySvg, "x", 5, underlying: "100"));
        }

        [Fact]
        public async Task AToAnimation_StartsFromTheUnderlyingValue()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" to=\"100\" dur=\"2s\" fill=\"freeze\"/></rect>");

            Assert.Equal("60", SampleAt(set, svg, "x", 1, underlying: "20"));
        }

        [Fact]
        public async Task TwoAnimationsOfOneAttribute_ApplyInTheOrderTheyBegan_TheLaterOverTheEarlier()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\">" +
                "<set attributeName=\"x\" to=\"10\" begin=\"0s\"/>" +
                "<set attributeName=\"x\" to=\"20\" begin=\"2s\"/>" +
                "</rect>");

            Assert.Equal("10", SampleAt(set, svg, "x", 1));
            Assert.Equal("20", SampleAt(set, svg, "x", 3));
        }

        [Fact]
        public async Task RestartNever_AllowsOneInterval_AndWhenNotActiveIgnoresABeginInsideOne()
        {
            var (never, neverSvg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"10\" begin=\"0s;5s\" dur=\"1s\" restart=\"never\"/></rect>");
            Assert.Equal("5", SampleAt(never, neverSvg, "x", 0.5));
            Assert.Null(SampleAt(never, neverSvg, "x", 5.5));

            var (always, alwaysSvg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"10\" begin=\"0s;5s\" dur=\"1s\"/></rect>");
            Assert.Equal("5", SampleAt(always, alwaysSvg, "x", 5.5));

            var (whenNotActive, whenNotActiveSvg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"10\" begin=\"0s;0.5s\" dur=\"1s\" restart=\"whenNotActive\"/></rect>");
            Assert.Equal("7", SampleAt(whenNotActive, whenNotActiveSvg, "x", 0.7));
        }

        [Fact]
        public async Task AnEndTime_CutsTheActiveDurationShort()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"10\" dur=\"4s\" end=\"1s\"/></rect>");

            Assert.NotNull(SampleAt(set, svg, "x", 0.9));
            Assert.Null(SampleAt(set, svg, "x", 1.1));
        }

        [Fact]
        public async Task AnimateTransform_GivesTheTypeAndItsValues_AndAdditiveKeepsTheUnderlyingList()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animateTransform attributeName=\"transform\" type=\"rotate\" from=\"0 5 5\" to=\"90 5 5\" dur=\"2s\" additive=\"sum\" fill=\"freeze\"/></rect>");

            Assert.Equal("translate(10 20) rotate(45 5 5)", SampleAt(set, svg, "transform", 1, underlying: "translate(10 20)"));
            Assert.Equal("rotate(90 5 5)", SampleAt(set, svg, "transform", 3, underlying: ""));
        }

        [Fact]
        public async Task AnimateMotion_PlacesTheElementOnThePath_AndAutoRotateFollowsTheTangent()
        {
            var (set, svg) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animateMotion path=\"M0,0 L100,0\" rotate=\"auto\" dur=\"2s\" fill=\"freeze\"/></rect>");

            Assert.Equal("translate(50 0)", SampleAt(set, svg, "transform", 1));
            Assert.Equal("translate(100 0)", SampleAt(set, svg, "transform", 5));
        }

        [Fact]
        public async Task AnMPath_TakesItsPathFromTheElementItNames()
        {
            var (set, svg) = await ReadAsync(
                "<path id=\"track\" d=\"M0,0 L0,100\"/><rect width=\"5\" height=\"5\"><animateMotion dur=\"2s\" fill=\"freeze\"><mpath href=\"#track\"/></animateMotion></rect>");

            var rect = set.Animations[0].Target;
            Assert.Equal("translate(0 50)", set.Sample(rect, "transform", 1, () => null, svg.SmilParser));
        }

        [Fact]
        public async Task AnAnimationWithATarget_NamedByHref_AnimatesThatElement()
        {
            var (set, _) = await ReadAsync(
                "<rect id=\"r\" width=\"5\" height=\"5\"/><set href=\"#r\" attributeName=\"x\" to=\"9\"/>");

            Assert.Equal("r", set.Animations[0].Target.GetAttribute("id", null));
        }

        [Fact]
        public async Task TheStillFrameLength_IsTheEndOfTheLongestAnimation()
        {
            var (set, _) = await ReadAsync(
                "<rect width=\"5\" height=\"5\"><animate attributeName=\"x\" from=\"0\" to=\"1\" begin=\"1s\" dur=\"2s\"/><animate attributeName=\"y\" from=\"0\" to=\"1\" dur=\"6s\" repeatCount=\"indefinite\"/></rect>");

            Assert.Equal(6, set.StillFrameLength, 6);
            Assert.True(set.ChangesAfter(100));
        }
    }
}
