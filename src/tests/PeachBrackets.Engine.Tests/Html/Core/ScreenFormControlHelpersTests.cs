using PeachBrackets.Html.Paint.Content;
using PeachDrawing.Core;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    public class ScreenFormControlHelpersTests
    {
        [Theory]
        [InlineData("#ff8000", 255, 128, 0)]
        [InlineData("#FF8000", 255, 128, 0)]
        [InlineData("  #0000ff ", 0, 0, 255)]
        [InlineData("#abc", 170, 187, 204)]
        [InlineData("#fff", 255, 255, 255)]
        [InlineData("00ff00", 0, 255, 0)]
        [InlineData("", 0, 0, 0)]
        [InlineData("red", 0, 0, 0)]
        [InlineData("#12345", 0, 0, 0)]
        [InlineData("#gggggg", 0, 0, 0)]
        public void AColorInputHoldsAHexColorAndBlackForAnythingElse(string value, int r, int g, int b) =>
            Assert.Equal(PaintColor.FromArgb(255, r, g, b), ScreenFormControlPainter.ColorOf(value));

        [Theory]
        [InlineData("date", "mm/dd/yyyy")]
        [InlineData("time", "--:-- --")]
        [InlineData("datetime-local", "mm/dd/yyyy, --:-- --")]
        [InlineData("month", "------ ----")]
        [InlineData("week", "Week --, ----")]
        public void AnEmptyDateOrTimeFieldShowsThePatternOfItsValue(string type, string hint) =>
            Assert.Equal(hint, ScreenFormControlPainter.DateTimeHint(type));
    }
}
