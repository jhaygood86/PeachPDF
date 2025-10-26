namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssTransitionPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssTransitionPropertyNoneLegal()
        {
            var snippet = "transition-property : none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-property", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionPropertyProperty>(property);
            var concrete = (TransitionPropertyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssTransitionPropertyAllLegal()
        {
            var snippet = "transition-property : ALL";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-property", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionPropertyProperty>(property);
            var concrete = (TransitionPropertyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("all", concrete.Value);
        }

        [Fact]
        public void CssTransitionPropertyWidthHeightLegal()
        {
            var snippet = "transition-property : width   , height";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-property", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionPropertyProperty>(property);
            var concrete = (TransitionPropertyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("width, height", concrete.Value);
        }

        [Fact]
        public void CssTransitionPropertyDashSpecificIllegal()
        {
            var snippet = "transition-property : -specific";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-property", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionPropertyProperty>(property);
            var concrete = (TransitionPropertyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssTransitionPropertySlidingVerticallyIllegal()
        {
            var snippet = "transition-property : sliding-vertically";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-property", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionPropertyProperty>(property);
            var concrete = (TransitionPropertyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssTransitionPropertyTest05Illegal()
        {
            var snippet = "transition-property : test_05";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-property", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionPropertyProperty>(property);
            var concrete = (TransitionPropertyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssTransitionTimingFunctionEaseLegal()
        {
            var snippet = "transition-timing-function : ease";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("ease", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionEaseInLegal()
        {
            var snippet = "transition-timing-function : ease-IN";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("ease-in", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionStepStartLegal()
        {
            var snippet = "transition-timing-function : step-start";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("step-start", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionStepStartStepEndLegal()
        {
            var snippet = "transition-timing-function : step-start  , step-end";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("step-start, step-end", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionStepStartStepEndLinearEaseInOutLegal()
        {
            var snippet = "transition-timing-function : step-start  , step-end,linear,ease-IN-OUT";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("step-start, step-end, linear, ease-in-out", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionCubicBezierLegal()
        {
            var snippet = "transition-timing-function : cubic-bezier(0, 1, 0.5, 1)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("cubic-bezier(0, 1, 0.5, 1)", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionStepsStartLegal()
        {
            var snippet = "transition-timing-function : steps(10, start)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("steps(10, start)", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionStepsEndLegal()
        {
            var snippet = "transition-timing-function : steps(25, end)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("steps(25, end)", concrete.Value);
        }

        [Fact]
        public void CssTransitionTimingFunctionStepsLinearCubicBezierLegal()
        {
            var snippet = "transition-timing-function : steps(25), linear, cubic-bezier(0.25, 1, 0.5, 1)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-timing-function", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionTimingFunctionProperty>(property);
            var concrete = (TransitionTimingFunctionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("steps(25), linear, cubic-bezier(0.25, 1, 0.5, 1)", concrete.Value);
        }

        [Fact]
        public void CssTransitionDurationSecondsLegal()
        {
            var snippet = "transition-duration : 6s";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-duration", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionDurationProperty>(property);
            var concrete = (TransitionDurationProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("6s", concrete.Value);
        }

        [Fact]
        public void CssTransitionDurationMillisecondsLegal()
        {
            var snippet = "transition-duration : 60ms";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-duration", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionDurationProperty>(property);
            var concrete = (TransitionDurationProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("60ms", concrete.Value);
        }

        [Fact]
        public void CssTransitionDurationMillisecondsSecondsSecondsLegal()
        {
            var snippet = "transition-duration : 60ms, 1s, 2s";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-duration", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionDurationProperty>(property);
            var concrete = (TransitionDurationProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("60ms, 1s, 2s", concrete.Value);
        }

        [Fact]
        public void CssTransitionDelayMillisecondsLegal()
        {
            var snippet = "transition-delay : 60ms";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-delay", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionDelayProperty>(property);
            var concrete = (TransitionDelayProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("60ms", concrete.Value);
        }

        [Fact]
        public void CssTransitionDelayMillisecondsSecondsSecondsLegal()
        {
            var snippet = "transition-delay : 60ms, 1s, 2s";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition-delay", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionDelayProperty>(property);
            var concrete = (TransitionDelayProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("60ms, 1s, 2s", concrete.Value);
        }

        [Fact]
        public void CssTransitionMillisecondsSecondsSecondsLegal()
        {
            var snippet = "transition : 60ms, 1s, 2s";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("60ms, 1s, 2s", concrete.Value);
        }

        [Fact]
        public void CssTransitionStepsLinearCubicBezierLegal()
        {
            var snippet = "transition : steps(25), linear, cubic-bezier(0.25, 1, 0.5, 1)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("steps(25), linear, cubic-bezier(0.25, 1, 0.5, 1)", concrete.Value);
        }

        [Fact]
        public void CssTransitionWidthHeightLegal()
        {
            var snippet = "transition : width   , height";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("width, height", concrete.Value);
        }

        [Fact]
        public void CssTransitionEaseLegal()
        {
            var snippet = "transition : ease";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("ease", concrete.Value);
        }

        [Fact]
        public void CssTransitionSecondsEaseAllLegal()
        {
            var snippet = "transition : all 1s ease";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("all 1s ease", concrete.Value);
        }

        [Fact]
        public void CssTransitionSecondsEaseAllHeightMsStepsLegal()
        {
            var snippet = "transition : all 1s ease, height steps(5) 50ms";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("all 1s ease, height 50ms steps(5)", concrete.Value);
        }

        [Fact]
        public void CssTransitionSecondsEaseAllHeightMsStepsWidthCubicBezierLegal()
        {
            var snippet = "transition : all 1s ease, height step-start 50ms,width,cubic-bezier(0.2,0.5 , 1  ,  1)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transition", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransitionProperty>(property);
            var concrete = (TransitionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("all 1s ease, height 50ms step-start, width, cubic-bezier(0.2, 0.5, 1, 1)", concrete.Value);
        }
    }
}








