using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Animation;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS animations rendered as a single frame: <see cref="PdfGenerateConfig.AnimationProgress"/> picks the
    /// position in each animation's run, <c>@keyframes</c> is interpolated there, and the result is written onto the
    /// box as ordinary declarations during the cascade.
    /// </summary>
    public class CssAnimationSnapshotTests
    {
        // ─── Timeline ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.25, 0.25)]
        [InlineData(1.0, 1.0)]
        public void Timeline_SingleIteration_IsTheFraction(double fraction, double expected) =>
            Assert.Equal(expected, AnimationTimeline.DirectedProgress(fraction, 4, 1, AnimationDirectionKind.Normal, false)!.Value, 9);

        [Theory]
        [InlineData(0.0, 0.0)]     // start of the first iteration
        [InlineData(0.25, 0.75)]   // 0.75 of 3 iterations = 2.25 -> a quarter of the third
        [InlineData(0.5, 0.5)]     // 1.5 iterations -> half of the second
        [InlineData(1.0, 1.0)]     // the end of the last whole iteration is its full progress, not 0 of a fourth
        public void Timeline_FiniteIterations_FractionSpansAllOfThem(double fraction, double expected) =>
            Assert.Equal(expected, AnimationTimeline.DirectedProgress(fraction, 2, 3, AnimationDirectionKind.Normal, false)!.Value, 9);

        [Fact]
        public void Timeline_FractionalIterationCount_EndsPartWayThroughTheLastIteration() =>
            Assert.Equal(0.5, AnimationTimeline.DirectedProgress(1, 2, 2.5, AnimationDirectionKind.Normal, false)!.Value, 9);

        [Fact]
        public void Timeline_Infinite_SpansOneIteration()
        {
            Assert.Equal(0.5, AnimationTimeline.DirectedProgress(0.5, 10, double.PositiveInfinity, AnimationDirectionKind.Normal, false)!.Value, 9);
            Assert.Equal(1.0, AnimationTimeline.DirectedProgress(1, 10, double.PositiveInfinity, AnimationDirectionKind.Normal, false)!.Value, 9);
        }

        [Theory]
        [InlineData("Normal", 0.3, 0.3)]
        [InlineData("Reverse", 0.3, 0.7)]
        [InlineData("Alternate", 0.3, 0.3)]              // first iteration runs forward
        [InlineData("AlternateReverse", 0.3, 0.7)]       // first iteration runs backward
        public void Timeline_Direction_AppliesToTheFirstIteration(string direction, double fraction, double expected) =>
            Assert.Equal(expected, AnimationTimeline.DirectedProgress(fraction, 1, double.PositiveInfinity, Enum.Parse<AnimationDirectionKind>(direction), false)!.Value, 9);

        [Fact]
        public void Timeline_Alternate_AtTheEndOfTheFirstCycleShowsTheLastKeyframe() =>
            Assert.Equal(1.0, AnimationTimeline.DirectedProgress(1, 10, double.PositiveInfinity, AnimationDirectionKind.Alternate, false)!.Value, 9);

        [Fact]
        public void Timeline_Alternate_SecondIterationRunsBackward() =>
            // Fraction 0.75 of 2 iterations = iteration 1 (odd), a quarter in: runs backward.
            Assert.Equal(0.5, AnimationTimeline.DirectedProgress(0.75, 2, 2, AnimationDirectionKind.Alternate, false)!.Value, 9);

        [Fact]
        public void Timeline_ZeroDuration_HasNoEffectUnlessItFillsForwards()
        {
            Assert.Null(AnimationTimeline.DirectedProgress(0.5, 0, 1, AnimationDirectionKind.Normal, false));
            Assert.Equal(1.0, AnimationTimeline.DirectedProgress(0.5, 0, 1, AnimationDirectionKind.Normal, true)!.Value, 9);
            Assert.Equal(0.0, AnimationTimeline.DirectedProgress(0.5, 0, 1, AnimationDirectionKind.Reverse, true)!.Value, 9);
        }

        [Fact]
        public void Timeline_ZeroIterations_HasNoEffectUnlessItFillsForwards() =>
            Assert.Null(AnimationTimeline.DirectedProgress(0.5, 1, 0, AnimationDirectionKind.Normal, false));

        // ─── Easing ──────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("linear", 0.3, 0.3)]
        [InlineData("ease-in-out", 0.5, 0.5)]
        [InlineData("ease", 0.0, 0.0)]
        [InlineData("ease", 1.0, 1.0)]
        [InlineData("step-start", 0.01, 1.0)]
        [InlineData("step-end", 0.99, 0.0)]
        [InlineData("steps(4, end)", 0.5, 0.5)]
        [InlineData("steps(4, jump-start)", 0.1, 0.25)]
        [InlineData("steps(3, jump-none)", 0.5, 0.5)]
        [InlineData("steps(2, jump-both)", 0.1, 1.0 / 3)]
        [InlineData("cubic-bezier(0, 0, 1, 1)", 0.4, 0.4)]
        public void Easing_Evaluates(string easing, double input, double expected)
        {
            Assert.True(EasingFunction.TryParse(easing, out var function));
            Assert.Equal(expected, function.Evaluate(input), 4);
        }

        [Fact]
        public void Easing_EaseIn_StartsSlowAndEaseOut_EndsSlow()
        {
            Assert.True(EasingFunction.TryParse("ease-in", out var easeIn));
            Assert.True(EasingFunction.TryParse("ease-out", out var easeOut));
            Assert.True(easeIn.Evaluate(0.25) < 0.25);
            Assert.True(easeOut.Evaluate(0.25) > 0.25);
        }

        [Theory]
        [InlineData("wobble")]
        [InlineData("cubic-bezier(1, 2)")]
        [InlineData("steps(0)")]
        [InlineData("steps(1, jump-none)")]
        [InlineData("steps(2, sideways)")]
        public void Easing_RejectsWhatIsNotOne(string text) => Assert.False(EasingFunction.TryParse(text, out _));

        // ─── Interpolation ───────────────────────────────────────────────────────

        private static string Mix(string property, string from, string to, double t) =>
            CssValueInterpolator.Interpolate(new CssValueParser(new PdfSharpAdapter()), property, from, to, t);

        [Theory]
        [InlineData("opacity", "0", "1", 0.25, "0.25")]
        [InlineData("opacity", "1", "0", 0.5, "0.5")]
        [InlineData("width", "10px", "30px", 0.5, "20px")]
        [InlineData("margin-left", "0", "20px", 0.5, "10px")]          // a bare 0 takes the other side's unit
        [InlineData("margin-left", "20px", "0", 0.25, "15px")]
        [InlineData("width", "0%", "100%", 0.3, "30%")]
        [InlineData("transform", "rotate(0deg)", "rotate(90deg)", 0.5, "rotate(45deg)")]
        [InlineData("transform", "rotate(0deg)", "rotate(0.5turn)", 0.5, "rotate(90deg)")]  // angles meet in degrees
        [InlineData("transform", "translateX(0) scale(1)", "translateX(10px) scale(2)", 0.5, "translateX(5px) scale(1.5)")]
        [InlineData("filter", "blur(0px)", "blur(8px)", 0.5, "blur(4px)")]
        [InlineData("z-index", "0", "5", 0.5, "3")]                     // integers stay integers
        public void Interpolate_MixesNumbersLengthsAndFunctionArguments(string property, string from, string to, double t, string expected) =>
            Assert.Equal(expected, Mix(property, from, to, t));

        [Fact]
        public void Interpolate_DifferentLengthUnits_AreLeftToCalc() =>
            Assert.Equal("calc(5px + 25%)", Mix("width", "10px", "50%", 0.5));

        [Theory]
        [InlineData("#000000", "#ffffff", 0.5, "rgb(128, 128, 128)")]
        [InlineData("red", "blue", 0.5, "rgb(128, 0, 128)")]
        [InlineData("rgb(255, 0, 0)", "rgb(0, 0, 255)", 0.25, "rgb(191, 0, 64)")]
        [InlineData("rgba(255, 0, 0, 1)", "rgba(255, 0, 0, 0)", 0.5, "rgba(255, 0, 0, 0.5)")]
        public void Interpolate_Colors_MixPremultiplied(string from, string to, double t, string expected) =>
            Assert.Equal(expected, Mix("color", from, to, t));

        [Fact]
        public void Interpolate_FromTransparent_KeepsTheOtherColorsHue() =>
            // Premultiplied: transparent contributes nothing, so a half-transparent red stays red, not pink-grey.
            Assert.Equal("rgba(255, 0, 0, 0.5)", Mix("background-color", "transparent", "red", 0.5));

        [Fact]
        public void Interpolate_ColorsInsideAShadow_AreMixedAmongTheLengths() =>
            Assert.Equal("0px 4px 10px rgb(128, 0, 128)", Mix("box-shadow", "0px 0px 10px red", "0px 8px 10px blue", 0.5));

        [Theory]
        [InlineData("transform", "none", "translateX(10px)", 0.5, "translateX(5px)")]
        [InlineData("transform", "scale(3)", "none", 0.5, "scale(2)")]
        [InlineData("filter", "none", "grayscale(1)", 0.5, "grayscale(0.5)")]
        [InlineData("filter", "none", "brightness(0)", 0.5, "brightness(0.5)")]
        public void Interpolate_NoneAgainstAList_MeetsItsIdentity(string property, string from, string to, double t, string expected) =>
            Assert.Equal(expected, Mix(property, from, to, t));

        [Theory]
        [InlineData("display", "block", "none", 0.49, "block")]
        [InlineData("display", "block", "none", 0.5, "none")]
        [InlineData("transform", "rotate(10deg)", "translateX(10px)", 0.4, "rotate(10deg)")]   // different functions: discrete
        [InlineData("width", "auto", "100px", 0.9, "100px")]
        public void Interpolate_WhatDoesNotMatch_FlipsHalfWay(string property, string from, string to, double t, string expected) =>
            Assert.Equal(expected, Mix(property, from, to, t));

        [Theory]
        [InlineData(0.5, "visible")]
        [InlineData(0.0, "hidden")]
        [InlineData(1.0, "visible")]
        public void Interpolate_Visibility_IsVisibleThroughoutAnyTransitionToVisible(double t, string expected) =>
            Assert.Equal(expected, Mix("visibility", "hidden", "visible", t));

        [Fact]
        public void Interpolate_Opacity_IsClampedWhenAnEasingOvershoots()
        {
            Assert.Equal("1", Mix("opacity", "0", "1", 1.4));
            Assert.Equal("0", Mix("opacity", "0", "1", -0.4));
        }

        // ─── Cascade ─────────────────────────────────────────────────────────────

        private const string FadingLogos = """
            <!DOCTYPE html><html><head><style>
            @keyframes fadeOut { 0% { opacity: 1; } 45% { opacity: 1; } 55% { opacity: 0; } 100% { opacity: 0; } }
            @keyframes fadeIn  { 0% { opacity: 0; } 45% { opacity: 0; } 55% { opacity: 1; } 100% { opacity: 1; } }
            img { position: absolute; top: 0; left: 0; width: 10px; height: 10px; }
            #top    { animation-name: fadeOut; animation-timing-function: ease-in-out; animation-iteration-count: infinite; animation-duration: 10s; animation-direction: alternate; }
            #bottom { animation: fadeIn 10s ease-in-out infinite alternate; }
            </style></head><body>
            <img id="top"    src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
            <img id="bottom" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
            </body></html>
            """;

        [Theory]
        [InlineData(0.0, 1.0, 0.0)]
        [InlineData(0.2, 1.0, 0.0)]    // both logos sit on their first plateau
        [InlineData(0.5, 0.5, 0.5)]    // the cross-fade is symmetric: half way through the ramp both are half visible
        [InlineData(0.8, 0.0, 1.0)]
        [InlineData(1.0, 0.0, 1.0)]    // alternate: the first cycle still runs forward, so the end is the 100% keyframe
        public async Task TwoLogosCrossFade_AtTheChosenPointOfTheRun(double progress, double top, double bottom)
        {
            var root = await BuildAsync(FadingLogos, progress);

            Assert.Equal(top, Opacity(Find(root, "top")), 3);
            Assert.Equal(bottom, Opacity(Find(root, "bottom")), 3);
        }

        [Fact]
        public async Task WithoutAnimationProgress_AnimationsAreIgnored()
        {
            var root = await BuildAsync(FadingLogos, progress: null);

            Assert.Equal(1.0, Opacity(Find(root, "top")));
            Assert.Equal(1.0, Opacity(Find(root, "bottom")));
        }

        [Fact]
        public void Config_DefaultsToOff() => Assert.Null(new PdfGenerateConfig().AnimationProgress);

        [Fact]
        public async Task MissingFromAndTo_AreFilledWithTheUnderlyingValue()
        {
            var root = await BuildAsync("""
                <style>@keyframes pulse { 50% { opacity: 0; } }
                #a { opacity: 0.8; animation: pulse 2s linear; }</style><div id="a">x</div>
                """, 0.25);

            // Half way from the underlying 0.8 (implicit 0%) to 0 (the 50% keyframe).
            Assert.Equal(0.4, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task ImportantDeclaration_BeatsAnAnimation()
        {
            var root = await BuildAsync("""
                <style>@keyframes hide { from { opacity: 0 } to { opacity: 0 } }
                #a { opacity: 0.6 !important; animation: hide 1s; }
                #b { opacity: 0.6; animation: hide 1s; }</style><div id="a">x</div><div id="b">y</div>
                """, 0.5);

            Assert.Equal(0.6, Opacity(Find(root, "a")), 3);   // an !important declaration outranks the animation origin
            Assert.Equal(0.0, Opacity(Find(root, "b")), 3);   // a normal one does not
        }

        [Fact]
        public async Task InlineStyleAnimation_AndLaterAnimationWinsPerProperty()
        {
            var root = await BuildAsync("""
                <style>
                @keyframes a { from { opacity: 0 } to { opacity: 0 } }
                @keyframes b { from { opacity: 1 } to { opacity: 1 } }
                </style><div id="x" style="animation: a 1s, b 1s">x</div>
                """, 0.5);

            Assert.Equal(1.0, Opacity(Find(root, "x")), 3);
        }

        [Fact]
        public async Task EachAnimationInAList_UsesItsOwnTimingParameters()
        {
            // Lists repeat to the length of animation-name, so the second animation reuses the only duration.
            var root = await BuildAsync("""
                <style>
                @keyframes fade { from { opacity: 0 } to { opacity: 1 } }
                @keyframes grow { from { width: 0pt } to { width: 100pt } }
                #x { animation-name: fade, grow; animation-duration: 4s; animation-timing-function: linear, steps(2, end); }
                </style><div id="x">x</div>
                """, 0.75);

            var x = Find(root, "x");
            Assert.Equal(0.75, Opacity(x), 3);
            // steps(2, end) at 0.75 -> the second step, half way across: 50pt.
            Assert.Equal(50.0, x.ActualRight - x.Location.X, 1);
        }

        [Fact]
        public async Task AnimatedLength_ChangesLayout_AsADeclaredOneWould()
        {
            var root = await BuildAsync("""
                <style>@keyframes grow { from { width: 40pt } to { width: 120pt } }
                #a { height: 10pt; animation: grow 1s linear; }</style><div id="a"></div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal(80.0, a.ActualRight - a.Location.X, 1);
        }

        [Fact]
        public async Task KeyframeLengthsOfDifferentUnits_ResolveAtLayout()
        {
            var root = await BuildAsync("""
                <style>@keyframes grow { from { width: 40pt } to { width: 50% } }
                #wrap { width: 200pt }
                #a { height: 10pt; animation: grow 1s linear; }</style><div id="wrap"><div id="a"></div></div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal(70.0, a.ActualRight - a.Location.X, 1);   // 40pt/2 + 50% of 200pt / 2
        }

        [Fact]
        public async Task KeyframeValue_ResolvesVarAgainstTheBox()
        {
            var root = await BuildAsync("""
                <style>@keyframes slide { from { margin-left: 0pt } to { margin-left: var(--far) } }
                #a { --far: 100pt; animation: slide 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            Assert.Equal("50pt", Find(root, "a").MarginLeft.ToString());
        }

        [Fact]
        public async Task Keyframes_InsideAMediaRule_AreFound()
        {
            var root = await BuildAsync("""
                <style>@media print { @keyframes k { from { opacity: 0 } to { opacity: 1 } } }
                #a { animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            Assert.Equal(0.5, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task LaterKeyframesRule_ReplacesAnEarlierOfTheSameName()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 0 } }
                @keyframes k { from { opacity: 1 } to { opacity: 1 } }
                #a { animation: k 1s; }</style><div id="a">x</div>
                """, 0.5);

            Assert.Equal(1.0, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task UnknownAnimationName_AndNone_DoNothing()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 0 } }
                #a { animation: nope 1s; } #b { animation: none; }</style><div id="a">x</div><div id="b">y</div>
                """, 0.5);

            Assert.Equal(1.0, Opacity(Find(root, "a")));
            Assert.Equal(1.0, Opacity(Find(root, "b")));
        }

        [Fact]
        public async Task KeyframeDeclarations_ThatAreImportant_OrAnimationProperties_AreIgnored()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 !important; animation-duration: 99s; color: red } to { opacity: 0 !important; color: red } }
                #a { animation: k 1s; }</style><div id="a">x</div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal(1.0, Opacity(a));                       // the !important keyframe declaration is not a declaration
            Assert.Equal("rgb(255, 0, 0)", a.Color);             // the rest of the keyframe still applies
        }

        [Fact]
        public async Task AKeyframeTimingFunction_EasesItsOwnInterval()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0; animation-timing-function: step-end } to { opacity: 1 } }
                #a { animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.75);

            Assert.Equal(0.0, Opacity(Find(root, "a")), 3);   // step-end holds the 0% value until the interval ends
        }

        [Fact]
        public async Task Pseudoelement_CanBeAnimated()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 1 } }
                #a::before { content: "x"; animation: k 1s linear; }</style><div id="a"></div>
                """, 0.5);

            var before = Find(root, "a").Boxes[0];
            Assert.Equal(0.5, Opacity(before), 3);
        }

        [Fact]
        public async Task Animation_IsNotInherited()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 0 } }
                #p { animation: k 1s; }</style><div id="p"><span id="c">x</span></div>
                """, 0.5);

            Assert.Equal(0.0, Opacity(Find(root, "p")));
            Assert.Equal(1.0, Opacity(Find(root, "c")));
        }

        [Fact]
        public async Task OutOfRangeProgress_Throws()
        {
            var generator = new PdfGenerator();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await generator.GeneratePdf("<p>x</p>", new PdfGenerateConfig { AnimationProgress = 1.5 }));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await generator.GeneratePdf("<p>x</p>", new PdfGenerateConfig { AnimationProgress = double.NaN }));
        }

        [Fact]
        public async Task GeneratesAPdf_ThroughThePublicApi()
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(FadingLogos, new PdfGenerateConfig { PageSize = PageSize.A4, AnimationProgress = 0.5 });

            Assert.NotNull(document);
        }

        [Fact]
        public async Task PagesAppendedWithDifferentProgress_ShareOneDocument()
        {
            // The animation showcase is one PDF of three pages, each the same HTML at another progress: AddPdfPages
            // takes a config per call, so the setting belongs to the page being added, not to the generator.
            var generator = new PdfGenerator();
            PdfGenerateConfig At(double progress) => new() { PageSize = PageSize.A4, AnimationProgress = progress };

            var document = await generator.GeneratePdf(FadingLogos, At(0));
            await generator.AddPdfPages(document, FadingLogos, At(0.5));
            await generator.AddPdfPages(document, FadingLogos, At(1));

            Assert.Equal(3, document.PageCount);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildAsync(string html, double? progress)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter) { AnimationProgress = progress };
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

        private static double Opacity(CssBox box) => double.Parse(box.Opacity, CultureInfo.InvariantCulture);

        private static CssBox Find(CssBox box, string id) => FindOrNull(box, id) ?? throw new InvalidOperationException($"No #{id}");

        private static CssBox? FindOrNull(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindOrNull(child, id);
                if (found is not null) return found;
            }

            return null;
        }
    }
}
