using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    // Adapted from upstream PDFsharp's Pdf.Objects/PdfDateTests.cs. Upstream constructs
    // PdfDate from DateTimeOffset values with several different explicit UTC offsets and
    // asserts the exact offset suffix for each. This fork's PdfDate only takes a plain
    // DateTime (no embedded offset, no parameterless constructor), and its ToString()
    // formats the offset using the *local machine's* current UTC offset for that instant
    // -- so a single fixed offset can't be asserted portably. Instead this test computes
    // the expected offset independently via TimeZoneInfo, so it passes regardless of
    // which time zone the machine running it is in.
    public class PdfDateTests
    {
        [Fact]
        public void Test_PdfDate()
        {
            var value = new DateTime(2024, 6, 15, 10, 30, 0);
            var date = new PdfDate(value);

            Assert.Equal(value, date.Value);

            var offset = TimeZoneInfo.Local.GetUtcOffset(value);
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var absOffset = offset.Duration();
            var expectedSuffix = $"{sign}{absOffset.Hours:00}'{absOffset.Minutes:00}'";

            Assert.Equal($"D:20240615103000{expectedSuffix}", date.ToString());
        }

        [Theory]
        [InlineData("th-TH")] // Buddhist calendar: 2024 would print as 2567
        [InlineData("ar-SA")] // Hijri calendar
        [InlineData("fa-IR")] // Persian calendar
        public void ToString_IsNotLocalizedByTheCurrentCulture(string cultureName)
        {
            var value = new DateTime(2024, 6, 15, 10, 30, 0);
            var expected = new PdfDate(value).ToString();

            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(cultureName);

                Assert.Equal(expected, new PdfDate(value).ToString());
                Assert.StartsWith("D:20240615103000", new PdfDate(value).ToString());
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }
    }
}
