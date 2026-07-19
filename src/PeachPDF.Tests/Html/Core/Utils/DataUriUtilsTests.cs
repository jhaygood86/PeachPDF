using PeachPDF.Html.Core.Utils;
using System;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Regression coverage for a real bug found while investigating why the Acid2 test's embedded
    /// images (<c>.forehead</c>'s tiling background, the "eyes" object chain's PNG, etc.) never
    /// rendered: <see cref="DataUriUtils.TryDecodeDataUri"/>'s base64 branch called
    /// <c>Convert.FromBase64String</c> directly on the URI body without percent-decoding it first, so
    /// any base64 payload with percent-escaped reserved characters (<c>%2F</c>, <c>%2B</c>, <c>%3D</c> -
    /// a common, spec-legal way to write a data: URI, and exactly how the real Acid2 fixture's own
    /// embedded PNGs are written) threw <see cref="FormatException"/> internally and silently decoded
    /// to nothing.
    /// </summary>
    public class DataUriUtilsTests
    {
        [Fact]
        public void TryDecodeDataUri_Base64WithPercentEscapedReservedCharacters_DecodesSuccessfully()
        {
            // The real Acid2 fixture's ".forehead" background: a 1x1 yellow-pixel PNG, base64-encoded
            // with its "/" characters written as the percent-escape "%2F" (verbatim from acid2.html).
            const string uri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4%2F58BAAT%2FAf9jgNErAAAAAElFTkSuQmCC";

            var result = DataUriUtils.TryDecodeDataUri(uri, out var mimeType, out var bytes);

            Assert.True(result);
            Assert.Equal("image/png", mimeType);
            Assert.NotEmpty(bytes);
            // PNG magic bytes.
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }

        [Fact]
        public void TryDecodeDataUri_Base64WithoutPercentEscapes_StillDecodesSuccessfully()
        {
            // Sanity check that the fix didn't disturb the ordinary (already-working) case: the same
            // 1x1 PNG payload, written without any percent-escaping at all.
            const string uri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

            var result = DataUriUtils.TryDecodeDataUri(uri, out _, out var bytes);

            Assert.True(result);
            Assert.NotEmpty(bytes);
        }

        [Fact]
        public void TryDecodeDataUri_NonBase64_StillPercentDecodesAsBefore()
        {
            var result = DataUriUtils.TryDecodeDataUri("data:text/plain,Hello%20World", out var mimeType, out var bytes);

            Assert.True(result);
            Assert.Equal("text/plain", mimeType);
            Assert.Equal("Hello World", System.Text.Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void TryDecodeDataUri_MalformedBase64_ReturnsFalse()
        {
            var result = DataUriUtils.TryDecodeDataUri("data:image/png;base64,not valid base64!!!", out _, out _);

            Assert.False(result);
        }
    }
}
