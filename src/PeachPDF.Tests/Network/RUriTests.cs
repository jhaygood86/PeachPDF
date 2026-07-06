using System;
using System.IO;
using System.Threading.Tasks;
using PeachPDF.Network;

namespace PeachPDF.Tests.Network
{
    public class RUriTests
    {
        [Fact]
        public void DataUri_SmallPayload_RoundTripsExactly()
        {
            const string input = "data:text/plain;base64,SGVsbG8=";

            var uri = new RUri(input);

            Assert.Equal(input, uri.AbsoluteUri);
            Assert.Equal(input, uri.OriginalString);
            Assert.Equal("data", uri.Scheme);
            Assert.True(uri.IsAbsoluteUri);
            Assert.False(uri.IsFile);
        }

        [Fact]
        public void DataUri_LargePayloadBeyondNet8UriLengthLimit_RoundTripsExactly()
        {
            // System.Uri on net8.0 throws UriFormatException ("The Uri string is too long") for
            // absolute URIs beyond ~65535 characters. This is the concrete behavior RUri's data:
            // special-casing exists to avoid on net8.0 (by never constructing a System.Uri for
            // data: strings). .NET 10 removed this limit entirely, which is what makes it safe to
            // drop the special-casing there. Use a payload comfortably past that historical limit.
            var bytes = new byte[100_000];
            new Random(42).NextBytes(bytes);
            var input = $"data:application/octet-stream;base64,{Convert.ToBase64String(bytes)}";

            var uri = new RUri(input);

            Assert.Equal(input, uri.AbsoluteUri);
            Assert.Equal(input, uri.OriginalString);
            Assert.Equal("data", uri.Scheme);
            Assert.True(uri.IsAbsoluteUri);
        }

        [Fact]
        public void DataUri_SlashHeavyPayload_RoundTripsExactly()
        {
            // '/' is part of the base64 alphabet, so a real base64 payload can contain long runs
            // of slashes. Verify these aren't mistaken for path segments and normalized away.
            var slashes = new string('/', 5000);
            var input = $"data:text/plain;base64,{slashes}==";

            var uri = new RUri(input);

            Assert.Equal(input, uri.AbsoluteUri);
            Assert.Equal(input, uri.OriginalString);
        }

        [Fact]
        public async Task DataUri_LargePayload_DecodesCorrectlyThroughDataUriNetworkLoader()
        {
            var bytes = new byte[100_000];
            new Random(42).NextBytes(bytes);
            var dataUri = new RUri($"data:application/octet-stream;base64,{Convert.ToBase64String(bytes)}");

            var loader = new DataUriNetworkLoader();
            var response = await loader.GetResourceStream(dataUri);

            Assert.NotNull(response);
            using var memoryStream = new MemoryStream();
            await response!.ResourceStream!.CopyToAsync(memoryStream);
            Assert.Equal(bytes, memoryStream.ToArray());
        }
    }
}
