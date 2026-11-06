using PeachBrackets.Apple.Logic;

namespace PeachBrackets.Apple.Tests;

public class PickerValuesTests
{
    [Theory]
    [InlineData("date", "date")]
    [InlineData("time", "time")]
    [InlineData("datetime-local", "datetime")]
    [InlineData("color", "color")]
    [InlineData("select", "select")]
    [InlineData("text", null)]
    [InlineData("file", "file")]
    [InlineData("number", null)]
    public void EachKindOfControlOpensItsOwnPickerOrNone(string type, string? kind) =>
        Assert.Equal(kind, PickerValues.KindOf(type));

    [Fact]
    public void ADateAndATimeParseAndFormatBackToTheSameText()
    {
        Assert.True(PickerValues.TryParseDate("2026-11-02", out var date));
        Assert.Equal("2026-11-02", PickerValues.FormatDate(date));
        Assert.True(PickerValues.TryParseDate("2026-11-02T14:30", out var fromLocal));
        Assert.Equal(date, fromLocal);
        Assert.False(PickerValues.TryParseDate("nope", out _));

        Assert.True(PickerValues.TryParseTime("14:30", out var time));
        Assert.Equal("14:30", PickerValues.FormatTime(time));
        Assert.True(PickerValues.TryParseTime("2026-11-02T09:05", out var fromLocalTime));
        Assert.Equal("09:05", PickerValues.FormatTime(fromLocalTime));
        Assert.True(PickerValues.TryParseTime("08:15:30", out var seconds));
        Assert.Equal("08:15", PickerValues.FormatTime(seconds));
        Assert.False(PickerValues.TryParseTime("later", out var none));
        Assert.Equal(TimeSpan.Zero, none);
    }

    [Fact]
    public void ADateTimeKeepsTheHalfThatWasNotChanged()
    {
        Assert.Equal("2027-01-05T14:30", PickerValues.FormatDateTime("2026-11-02T14:30", new DateTime(2027, 1, 5), null));
        Assert.Equal("2026-11-02T08:00", PickerValues.FormatDateTime("2026-11-02T14:30", null, new TimeSpan(8, 0, 0)));
        Assert.Equal("2027-01-05T00:00", PickerValues.FormatDateTime(string.Empty, new DateTime(2027, 1, 5), null));
        Assert.EndsWith("T09:00", PickerValues.FormatDateTime(string.Empty, null, new TimeSpan(9, 0, 0)));
    }

    [Fact]
    public void AColourFormatsAsHexAndParsesBackWithBlackForAnythingElse()
    {
        Assert.Equal("#ff8000", PickerValues.FormatColor(255, 128, 0));
        Assert.Equal((255, 128, 0), PickerValues.ParseColor("#FF8000"));
        Assert.Equal((0, 0, 0), PickerValues.ParseColor("red"));
        Assert.Equal((0, 0, 0), PickerValues.ParseColor("#12345"));
        Assert.Equal((0, 0, 0), PickerValues.ParseColor("#gggggg"));
    }

    [Fact]
    public void AnAcceptAttributeSplitsIntoMediaTypesAndExtensions()
    {
        var (mimes, extensions) = PickerValues.AcceptTokens("image/*, .PNG,application/pdf,.,bogus");

        Assert.Equal(["image/*", "application/pdf"], mimes);
        Assert.Equal(["png"], extensions);
        var (none, noExtensions) = PickerValues.AcceptTokens(string.Empty);
        Assert.Empty(none);
        Assert.Empty(noExtensions);
    }
}
