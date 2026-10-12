using PeachPDF.PDF.Drawing;

namespace PeachPDF.Tests.PdfEngineTests.Drawing;

public class PdfStringFormatsTests
{
    public static TheoryData<string, string, string> Layouts() => new()
    {
        { "BaseLineLeft", "Near", "BaseLine" },
        { "TopLeft", "Near", "Near" },
        { "CenterLeft", "Near", "Center" },
        { "BottomLeft", "Near", "Far" },
        { "TopCenter", "Center", "Near" },
        { "Center", "Center", "Center" },
        { "BottomCenter", "Center", "Far" },
        { "TopRight", "Far", "Near" },
        { "CenterRight", "Far", "Center" },
        { "BottomRight", "Far", "Far" },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void EveryPredefinedLayout_HasItsAlignments(string name, string horizontal, string vertical)
    {
        var property = typeof(PdfStringFormats).GetProperty(name)!;

        var format = (PdfStringFormat)property.GetValue(null)!;

        Assert.Equal(horizontal, format.Alignment.ToString());
        Assert.Equal(vertical, format.LineAlignment.ToString());
    }

    [Fact]
    public void Default_IsTheLeftBaseLineLayout_AndEveryAccessGivesANewObject()
    {
        var first = PdfStringFormats.Default;
        var second = PdfStringFormats.Default;

        Assert.Equal(PdfStringAlignment.Near, first.Alignment);
        Assert.Equal(PdfLineAlignment.BaseLine, first.LineAlignment);
        Assert.NotSame(first, second);
    }
}
