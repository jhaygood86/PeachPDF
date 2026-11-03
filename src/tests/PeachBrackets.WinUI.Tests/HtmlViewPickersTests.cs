using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class HtmlViewPickersTests
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
        Assert.Equal(kind, HtmlViewPickers.KindOf(type));

    [Fact]
    public void ADateAndATimeParseAndFormatBackToTheSameText()
    {
        Assert.True(HtmlViewPickers.TryParseDate("2026-11-02", out var date));
        Assert.Equal("2026-11-02", HtmlViewPickers.FormatDate(date));
        Assert.True(HtmlViewPickers.TryParseDate("2026-11-02T14:30", out var fromLocal));
        Assert.Equal(date, fromLocal);
        Assert.False(HtmlViewPickers.TryParseDate("nope", out _));

        Assert.True(HtmlViewPickers.TryParseTime("14:30", out var time));
        Assert.Equal("14:30", HtmlViewPickers.FormatTime(time));
        Assert.True(HtmlViewPickers.TryParseTime("2026-11-02T09:05", out var fromLocalTime));
        Assert.Equal("09:05", HtmlViewPickers.FormatTime(fromLocalTime));
        Assert.True(HtmlViewPickers.TryParseTime("08:15:30", out var seconds));
        Assert.Equal("08:15", HtmlViewPickers.FormatTime(seconds));
        Assert.False(HtmlViewPickers.TryParseTime("later", out var none));
        Assert.Equal(TimeSpan.Zero, none);
    }

    [Fact]
    public void ADateTimeKeepsTheHalfThatWasNotChanged()
    {
        Assert.Equal("2027-01-05T14:30", HtmlViewPickers.FormatDateTime("2026-11-02T14:30", new DateTime(2027, 1, 5), null));
        Assert.Equal("2026-11-02T08:00", HtmlViewPickers.FormatDateTime("2026-11-02T14:30", null, new TimeSpan(8, 0, 0)));
        Assert.Equal("2027-01-05T00:00", HtmlViewPickers.FormatDateTime(string.Empty, new DateTime(2027, 1, 5), null));
        Assert.EndsWith("T09:00", HtmlViewPickers.FormatDateTime(string.Empty, null, new TimeSpan(9, 0, 0)));
    }

    [Fact]
    public void AColourFormatsAsHexAndParsesBackWithBlackForAnythingElse()
    {
        Assert.Equal("#ff8000", HtmlViewPickers.FormatColor(255, 128, 0));
        Assert.Equal((255, 128, 0), HtmlViewPickers.ParseColor("#FF8000"));
        Assert.Equal((0, 0, 0), HtmlViewPickers.ParseColor("red"));
        Assert.Equal((0, 0, 0), HtmlViewPickers.ParseColor("#12345"));
        Assert.Equal((0, 0, 0), HtmlViewPickers.ParseColor("#gggggg"));
    }

    [Theory]
    [InlineData("image/*", ".png")]
    [InlineData("audio/*", ".mp3")]
    [InlineData("video/*", ".mp4")]
    [InlineData("text/*", ".txt")]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpeg")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("application/json", ".json")]
    [InlineData("text/plain", ".txt")]
    [InlineData("text/csv", ".csv")]
    [InlineData("text/html", ".htm")]
    public void EachCommonMediaTypeListsItsExtensions(string accept, string extension) =>
        Assert.Contains(extension, HtmlViewPickers.ExtensionsFor(accept));

    [Fact]
    public void AnAcceptAttributeBecomesTheExtensionsAPickerListsOrAnyFile()
    {
        Assert.Equal(new[] { ".png", ".pdf" }, HtmlViewPickers.ExtensionsFor(".PNG, .pdf"));
        Assert.Contains(".jpg", HtmlViewPickers.ExtensionsFor("image/*"));
        Assert.Equal(new[] { ".pdf", ".png" }, HtmlViewPickers.ExtensionsFor("application/pdf,.png,.pdf"));
        Assert.Equal(new[] { "*" }, HtmlViewPickers.ExtensionsFor(string.Empty));
        Assert.Equal(new[] { "*" }, HtmlViewPickers.ExtensionsFor("application/x-unknown"));
        Assert.Equal(new[] { "*" }, HtmlViewPickers.ExtensionsFor("."));
    }
}
