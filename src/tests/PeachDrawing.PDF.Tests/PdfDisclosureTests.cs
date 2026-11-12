using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>The open-and-close region (<see cref="PdfFormBuilder.AddDisclosure"/>), written the way a consumer uses it.</summary>
    public class PdfDisclosureTests
    {
        private static readonly PaintColor Green = PaintColor.FromArgb(255, 0, 160, 0);

        internal static string Write(Func<PdfComposer, PdfComposerPage, PdfFormBuilder, ValueTask> build, string? copyTo = null)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            var page = composer.AddPage(200, 200);
            build(composer, page, composer.CreateFormBuilder(new PdfRenderContext(), "D")).AsTask().GetAwaiter().GetResult();
            using var stream = new MemoryStream();
            composer.Save(stream);
            if (copyTo is not null)
                File.WriteAllBytes(copyTo, stream.ToArray());
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        private static PdfDisclosureDefinition Definition(string? name = "Details") => new()
        {
            ToggleBounds = new Rect(10, 10, 100, 20),
            ContentBounds = new Rect(10, 10, 100, 80),
            PaintContent = canvas =>
            {
                canvas.DrawRectangle(canvas.GetSolidBrush(Green), 0, 0, 100, 80);
                return ValueTask.CompletedTask;
            },
            AccessibleName = name,
        };

        [Fact]
        public void WritesAHiddenRegionAndAButtonWhoseActionsShowAndHideIt()
        {
            var text = Write((_, page, forms) => forms.AddDisclosureAsync(page, Definition()), Environment.GetEnvironmentVariable("PEACH_DISCLOSURE_OUT"));

            // The region is hidden (flags 2) and printable (4); the button over the heading is shown.
            Assert.Contains("/T (DDisclosure1Content)", text);
            Assert.Matches(new Regex(@"/F 6\b"), text);
            Assert.Contains("/S /Hide", text);
            Assert.Contains("/Next", text);
            Assert.Contains("/T (DDisclosure1Open)", text);
            Assert.Contains("/H true", text);
            Assert.Contains("/H false", text);
            Assert.Contains("/TU", text);
            Assert.Equal(2, Regex.Matches(text, @"/FT /Btn").Count);
        }

        [Fact]
        public void EachDisclosureGetsItsOwnNames()
        {
            var text = Write(async (_, page, forms) =>
            {
                await forms.AddDisclosureAsync(page, Definition(null));
                await forms.AddDisclosureAsync(page, Definition(null));
            });

            Assert.Contains("DDisclosure1Content", text);
            Assert.Contains("DDisclosure2Content", text);
            Assert.DoesNotContain("/TU", text);
        }

        [Fact]
        public void ArgumentsAreChecked()
        {
            Write(async (_, page, forms) =>
            {
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await forms.AddDisclosureAsync(null!, Definition()));
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await forms.AddDisclosureAsync(page, null!));
            });
        }
    }
}
