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

        [Theory]
        [InlineData("Normal", 2.5, 0.5)]            // the end state of 2.5 iterations is half way through the third
        [InlineData("Alternate", 2.5, 0.5)]         // third iteration (index 2) runs forward
        [InlineData("Alternate", 1.5, 0.5)]         // second iteration runs backward: 1 - 0.5
        [InlineData("Alternate", 2, 0.0)]           // second iteration, whole: ends at its 0% (backward)
        public void Timeline_ZeroDurationFillingForwards_EndsWhereTheFinalIterationEnds(string direction, double count, double expected) =>
            Assert.Equal(expected, AnimationTimeline.DirectedProgress(0.5, 0, count, Enum.Parse<AnimationDirectionKind>(direction), true)!.Value, 9);

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
        public void Easing_LinearWithStops_IsReadAsLinear()
        {
            Assert.True(EasingFunction.TryParse("linear(0, 0.25 40%, 1)", out var function));
            Assert.Equal(0.3, function.Evaluate(0.3), 9);
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
        [InlineData("filter", "none", "brightness(150%)", 0.5, "brightness(125%)")]    // the identity of a percentage is 100%, not 1%
        [InlineData("filter", "opacity(50%)", "none", 0.5, "opacity(75%)")]
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

        [Theory]
        [InlineData("transform", "rotate(100grad)", "rotate(0.25turn)", 0.5, "rotate(90deg)")]   // grad and turn meet in degrees
        [InlineData("transform", "rotate(0rad)", "rotate(180deg)", 0.5, "rotate(90deg)")]
        [InlineData("opacity", "1e0", "5e-1", 0.5, "0.75")]                                       // exponents are part of the number
        [InlineData("width", "calc((10px))", "calc((20px))", 0.5, "calc((15px))")]                // a bare parenthesis keeps its shape
        [InlineData("transform", "none", "translateX(10px) scale(2)", 0.5, "translateX(5px) scale(1.5)")]
        [InlineData("filter", "hue-rotate(90deg)", "none", 0.5, "hue-rotate(45deg)")]
        public void Interpolate_UnitsExponentsAndIdentities(string property, string from, string to, double t, string expected) =>
            Assert.Equal(expected, Mix(property, from, to, t));

        [Theory]
        [InlineData("background-image", "url(a.png)", "url(b.png)", 0.4, "url(a.png)")]          // a url() is opaque text
        [InlineData("background-image", "url(a.png)", "url(b.png)", 0.6, "url(b.png)")]
        [InlineData("content", "\"a\"", "\"b\"", 0.6, "\"b\"")]                               // so is a string
        [InlineData("transform", "none", "matrix(1, 0, 0, 1, 0, 0)", 0.4, "none")]                // no identity known for matrix()
        [InlineData("transform", "none", "translateX(calc(10px))", 0.6, "translateX(calc(10px))")] // nested function: not mixed
        [InlineData("filter", "none", "drop-shadow(1px 1px 1px red)", 0.4, "none")]
        [InlineData("margin-left", "10px", "2", 0.4, "10px")]                                     // a unitless non-zero number is not a length
        [InlineData("width", "10px", "1.5deg", 0.6, "1.5deg")]                                    // nor is an angle
        public void Interpolate_WhatCannotBeMixed_FlipsHalfWay(string property, string from, string to, double t, string expected) =>
            Assert.Equal(expected, Mix(property, from, to, t));

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

        [Theory]
        [InlineData("animation: k 2000ms linear", 0.5, 0.5)]                    // milliseconds
        [InlineData("animation: k 1s linear reverse", 0.25, 0.75)]
        [InlineData("animation: k 1s linear alternate-reverse", 0.25, 0.75)]
        [InlineData("animation: k 1s linear 2 alternate", 0.75, 0.5)]           // second iteration, running backward
        [InlineData("animation: k 1s linear 0.5", 1.0, 0.5)]                    // half an iteration is all there is
        [InlineData("animation: k 0s linear forwards", 0.5, 1.0)]               // no run: only the final state is left
        [InlineData("animation: k 0s linear", 0.5, 1.0)]                        // ...unless it does not fill forwards: base value
        public async Task AnimationTimingProperties_DecideTheSampledOpacity(string declaration, double progress, double expected)
        {
            var root = await BuildAsync($@"
                <style>@keyframes k {{ from {{ opacity: 0 }} to {{ opacity: 1 }} }}
                #a {{ {declaration} }}</style><div id=""a"">x</div>", progress);

            // The last case has no run and no fill, so the animation leaves the base opacity (1) alone.
            Assert.Equal(expected, Opacity(Find(root, "a")), 3);
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

        // ─── Underlying value, var() and global keywords ─────────────────────────

        [Fact]
        public async Task ImplicitKeyframe_TakesAnAuthorVarDeclarationAsTheUnderlyingValue()
        {
            // width is still a deferred var() declaration when the animation step runs; the implicit 0% keyframe
            // must be its resolved value (200pt), not the UA default (auto, which cannot be mixed).
            var root = await BuildAsync("""
                <style>@keyframes k { to { width: 400pt } }
                #a { --w: 200pt; width: var(--w); height: 10pt; animation: k 1s linear; }</style><div id="a"></div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal(300.0, a.ActualRight - a.Location.X, 1);
        }

        [Fact]
        public async Task ImplicitKeyframe_WithAVarOpacity_MixesFromTheResolvedValue()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { to { opacity: 0 } }
                #a { --o: 0.8; opacity: var(--o); animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            Assert.Equal(0.4, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task ShorthandWithVarInAKeyframe_IsExpandedAndAnimated()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { margin: var(--m) } to { margin: 40pt } }
                #a { --m: 20pt; margin: var(--m); animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal("30pt", a.MarginLeft.ToString());
            Assert.Equal("30pt", a.MarginTop.ToString());
        }

        [Fact]
        public async Task PendingVarShorthand_IsNotLostWhenOnlyALonghandIsAnimated()
        {
            // The animation owns margin-left; the rest of the margin: var() declaration still resolves.
            var root = await BuildAsync("""
                <style>@keyframes k { from { margin-left: 0pt } to { margin-left: 100pt } }
                #a { --m: 20pt; margin: var(--m); animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal("50pt", a.MarginLeft.ToString());
            Assert.Equal("20pt", a.MarginTop.ToString());
        }

        [Fact]
        public async Task UnrelatedVarDeclaration_StillResolvesAfterTheAnimationStep()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 1 } }
                #a { --w: 90pt; width: var(--w); height: 10pt; animation: k 1s linear; }</style><div id="a"></div>
                """, 0.5);

            var a = Find(root, "a");
            Assert.Equal(90.0, a.ActualRight - a.Location.X, 1);
            Assert.Equal(0.5, Opacity(a), 3);
        }

        [Fact]
        public async Task KeyframeInherit_ResolvesAgainstTheParent()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: inherit } to { opacity: 1 } }
                #p { opacity: 0.2; } #c { animation: k 1s linear; }</style><div id="p"><div id="c">x</div></div>
                """, 0.0);

            Assert.Equal(0.2, Opacity(Find(root, "c")), 3);
        }

        [Fact]
        public async Task KeyframeInitial_ResolvesToTheInitialValue()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: initial } }
                #a { opacity: 0.5; animation: k 1s linear; }</style><div id="a">x</div>
                """, 1.0);

            Assert.Equal(1.0, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task KeyframeUnset_ResolvesLikeInheritOrInitial()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { color: unset; opacity: unset } to { color: unset; opacity: unset } }
                #p { color: rgb(0, 0, 255); opacity: 0.3 } #c { color: rgb(255, 0, 0); opacity: 0.5; animation: k 1s; }</style>
                <div id="p"><div id="c">x</div></div>
                """, 0.5);

            var c = Find(root, "c");
            Assert.Equal("rgb(0, 0, 255)", c.Color);   // color is inherited: unset = inherit
            Assert.Equal(1.0, Opacity(c), 3);          // opacity is not: unset = initial
        }

        [Fact]
        public async Task KeyframeRevert_RollsBackToTheUserAgentValue()
        {
            // The author's 0.5 is below the animation origin's reach: revert goes to the UA level, where opacity is 1.
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: revert } to { opacity: 0 } }
                #a { opacity: 0.5; animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.0);

            Assert.Equal(1.0, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task KeyframeRevert_NamingAUserAgentStyledProperty_UsesTheUaRule()
        {
            // The UA sheet gives a <p> its margins; the author sheet overrides one, and revert undoes the override
            // - so it matches an untouched <p>, which an ignored keyword would not.
            var root = await BuildAsync("""
                <style>@keyframes k { from { margin-top: revert } to { margin-top: revert } }
                #a { margin-top: 3pt; animation: k 1s; }</style><p id="a">x</p><p id="b">y</p>
                """, 0.5);

            var animated = Find(root, "a").MarginTop.ToString();
            Assert.NotEqual("3pt", animated);
            Assert.Equal(Find(root, "b").MarginTop.ToString(), animated);
        }

        [Fact]
        public async Task KeyframeRevertLayer_RollsBackToTheAuthorValue()
        {
            // The animation origin is a layer of its own, so the layer below is the author's 0.8 - at the middle
            // keyframe, not just where an implicit keyframe would have filled it in.
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } 50% { opacity: revert-layer } to { opacity: 0 } }
                #a { opacity: 0.8; animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            Assert.Equal(0.8, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task AnimationProperties_GivenThroughVar_AreRead()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 1 } }
                #a { --n: k; --d: 2s; animation-name: var(--n); animation-duration: var(--d); animation-timing-function: linear; }
                #b { --all: k 1s linear; animation: var(--all); }</style><div id="a">x</div><div id="b">y</div>
                """, 0.5);

            Assert.Equal(0.5, Opacity(Find(root, "a")), 3);
            Assert.Equal(0.5, Opacity(Find(root, "b")), 3);
        }

        [Fact]
        public async Task ImportantAnimationNone_SwitchesAnAnimationOff()
        {
            // The print-stylesheet reset: !important on animation-* decides which animations exist, even though the
            // animation origin itself sits below the important phase.
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 0 } }
                * { animation: none !important; }
                #a { animation: k 1s; }</style><div id="a">x</div>
                """, 0.5);

            Assert.Equal(1.0, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task ImportantAnimation_AppliesAnAnimationTheNormalCascadeDoesNotName()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 1 } }
                #a { animation: k 1s linear !important; }
                #b { animation-name: k; animation-duration: 1s; animation-timing-function: linear; }
                #b { animation-duration: 2s !important; }</style><div id="a">x</div><div id="b">y</div>
                """, 0.5);

            Assert.Equal(0.5, Opacity(Find(root, "a")), 3);
            Assert.Equal(0.5, Opacity(Find(root, "b")), 3);    // the important 2s run is the one sampled; half of it is still 0.5
        }

        [Fact]
        public async Task ImportantInlineAnimationNone_BeatsAStylesheetAnimation()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 0 } }
                #a { animation: k 1s; }</style><div id="a" style="animation: none !important">x</div>
                """, 0.5);

            Assert.Equal(1.0, Opacity(Find(root, "a")), 3);
        }

        [Fact]
        public async Task KeyframeInheritOnAShorthand_ExpandsToTheParentsLonghands()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { margin: inherit } to { margin: 0pt } }
                #p { margin-left: 30pt; } #c { animation: k 1s linear; }</style><div id="p"><div id="c">x</div></div>
                """, 0.0);

            Assert.Equal("30pt", Find(root, "c").MarginLeft.ToString());
        }

        [Fact]
        public async Task KeyframeSet_UsesRevertOnlyForADeclarationThatSurvives()
        {
            var adapter = new PdfSharpAdapter();
            var overridden = RegisteredKeyframes.BuildRegistry(await CssData.Parse(adapter, "@keyframes k { to { opacity: revert; opacity: 1 } }"));
            var used = RegisteredKeyframes.BuildRegistry(await CssData.Parse(adapter, "@keyframes k { to { opacity: revert } }"));

            Assert.False(overridden["k"].UsesRevert);
            Assert.True(used["k"].UsesRevert);
        }

        [Fact]
        public async Task KeyframeRevert_CostsNothingWhenNoKeyframeUsesIt()
        {
            var root = await BuildAsync("""
                <style>@keyframes k { from { opacity: 0 } to { opacity: 1 } }
                #a { opacity: 0.8; animation: k 1s linear; }</style><div id="a">x</div>
                """, 0.5);

            Assert.False(root.HtmlContainer!.KeyframesUseRevert);
            Assert.Equal(0.5, Opacity(Find(root, "a")), 3);
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
