using PeachPDF.CSS;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS
{
    /// <summary>
    /// The <c>animation</c> and <c>transition</c> shorthands expand into their longhands. Both go through
    /// <c>TimeBasedShorthandConverter</c>, which used to hand the longhands a duration/delay value that answered
    /// to every property name, so a time anywhere in the shorthand turned the rest of it into <c>initial</c>.
    /// </summary>
    public class AnimationTransitionShorthandTests
    {
        private static string[] Expand(string declaration, params string[] longhands)
        {
            var sheet = StylesheetParser.Default.Parse("#a { " + declaration + "; }");
            var style = sheet.Rules.OfType<IStyleRule>().Single().Style;

            return longhands.Select(name => style.First(p => p.Name == name).Value).ToArray();
        }

        private static readonly string[] AnimationLonghands =
        [
            "animation-name", "animation-duration", "animation-timing-function", "animation-delay",
            "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state"
        ];

        [Fact]
        public void Animation_EveryComponent_LandsInItsOwnLonghand() =>
            Assert.Equal(
                ["fadeIn", "10s", "ease-in-out", "2s", "infinite", "alternate", "forwards", "paused"],
                Expand("animation: fadeIn 10s ease-in-out 2s infinite alternate forwards paused", AnimationLonghands));

        [Theory]
        [InlineData("animation: fadeIn 10s", "fadeIn", "10s", "initial")]
        [InlineData("animation: 10s fadeIn", "fadeIn", "10s", "initial")]
        [InlineData("animation: fadeIn 10s 2s", "fadeIn", "10s", "2s")]
        [InlineData("animation: 2s fadeIn", "fadeIn", "2s", "initial")]
        public void Animation_NameDurationAndDelay_AreTold_Apart(string declaration, string name, string duration, string delay) =>
            Assert.Equal([name, duration, delay], Expand(declaration, "animation-name", "animation-duration", "animation-delay"));

        [Fact]
        public void Animation_WithoutATime_StillExpands() =>
            Assert.Equal("fadeIn", Expand("animation: fadeIn", "animation-name")[0]);

        [Fact]
        public void Animation_List_KeepsOneEntryPerAnimation() =>
            Assert.Equal(["a, b", "1s, 2s"], Expand("animation: a 1s, b 2s", "animation-name", "animation-duration"));

        [Fact]
        public void Transition_EveryComponent_LandsInItsOwnLonghand() =>
            Assert.Equal(
                ["opacity", "300ms", "ease-in", "1s"],
                Expand("transition: opacity 300ms ease-in 1s", "transition-property", "transition-duration", "transition-timing-function", "transition-delay"));

        [Fact]
        public void Transition_PropertyAndDuration_AreNotLostToTheTime() =>
            Assert.Equal(["opacity", "300ms"], Expand("transition: opacity 300ms", "transition-property", "transition-duration"));
    }
}
