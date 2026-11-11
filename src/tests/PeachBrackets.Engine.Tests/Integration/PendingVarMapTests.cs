using PeachBrackets.Html.Parse;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary><see cref="PendingVarMap"/>: deferred <c>var()</c> declarations keep the order they were written in, against plain ones.</summary>
    public class PendingVarMapTests
    {
        [Fact]
        public void ADeferredDeclaration_IsReadBack_AndOrderedByWhenItWasWritten()
        {
            var map = new PendingVarMap
            {
                ["background"] = "linear-gradient(red, var(--x))",
                ["color"] = "var(--c)"
            };

            Assert.Equal("var(--c)", map["color"]);
            Assert.True(map.OrderOf("background") < map.OrderOf("color"));
            Assert.Equal(0, map.OrderOf("margin"));

            // Written again, it moves to where it is written now.
            map["background"] = "var(--y)";
            Assert.True(map.OrderOf("background") > map.OrderOf("color"));
        }

        [Fact]
        public void APlainDeclarationAfterADeferredOne_ReplacesIt_AndWinsOverALaterlyExpandedShorthand()
        {
            var map = new PendingVarMap
            {
                ["background"] = "linear-gradient(red, var(--x))"
            };
            var shorthandAt = map.OrderOf("background");

            map.NotePlain("background-clip");
            Assert.True(map.IsSetPlainAfter("background-clip", shorthandAt));
            Assert.False(map.IsSetPlainAfter("background-image", shorthandAt));

            map.NotePlain("background");
            Assert.False(map.ContainsKey("background"));
            Assert.Equal(0, map.OrderOf("background"));
        }
    }
}
