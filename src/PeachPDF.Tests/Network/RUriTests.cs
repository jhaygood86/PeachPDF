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

        // ─── The base-relative constructors (the path the reported bug actually took) ───────────
        //
        // An <img src="data:..."> reaches RUri through CommonUtils.ResolveAgainstDocumentBase, which
        // calls new RUri(baseUri, src) — not the single-string constructor the fixtures above cover.
        // Those two overloads had no data: handling at all, so a large inline image was dropped
        // before .NET 10 however well the string constructors behaved.

        [Fact]
        public void DataUri_ThroughBaseRelativeStringConstructor_RoundTripsExactly()
        {
            var bytes = new byte[100_000];
            new Random(42).NextBytes(bytes);
            var input = $"data:application/octet-stream;base64,{Convert.ToBase64String(bytes)}";
            var baseUri = new RUri("https://example.com/docs/page.html");

            var uri = new RUri(baseUri, input);

            // RFC 3986 §5.2.2: a reference carrying its own scheme is already absolute, so the base
            // is not consulted and the result is the data: URI unchanged.
            Assert.Equal(input, uri.AbsoluteUri);
            Assert.Equal("data", uri.Scheme);
            Assert.True(uri.IsAbsoluteUri);
        }

        [Fact]
        public void DataUri_ThroughBaseRelativeRUriConstructor_RoundTripsExactly()
        {
            var bytes = new byte[100_000];
            new Random(42).NextBytes(bytes);
            var input = $"data:application/octet-stream;base64,{Convert.ToBase64String(bytes)}";
            var baseUri = new RUri("https://example.com/docs/page.html");

            var uri = new RUri(baseUri, new RUri(input));

            Assert.Equal(input, uri.AbsoluteUri);
            Assert.Equal("data", uri.Scheme);
        }

        [Fact]
        public void RelativeReference_ThroughBaseRelativeConstructor_StillResolvesAgainstTheBase()
        {
            // The contrast case: the data: short-circuit must not swallow an ordinary relative
            // reference, which is the whole job of these two constructors.
            var baseUri = new RUri("https://example.com/docs/page.html");

            var uri = new RUri(baseUri, "../img/logo.png");

            Assert.Equal("https://example.com/img/logo.png", uri.AbsoluteUri);
        }

        [Fact]
        public void DataUriScheme_IsMatchedCaseInsensitively()
        {
            // RFC 3986 §3.1: a scheme is case-insensitive. A document writing DATA: must take the
            // same path as one writing data:, or it silently loses the same large payloads.
            var bytes = new byte[100_000];
            new Random(42).NextBytes(bytes);
            var payload = $"application/octet-stream;base64,{Convert.ToBase64String(bytes)}";
            var input = $"DATA:{payload}";

            var uri = new RUri(input);

            // What matters on every target: the payload survives intact and the scheme is data.
            Assert.Equal("data", uri.Scheme);
            Assert.EndsWith(payload, uri.AbsoluteUri, StringComparison.Ordinal);

            // The scheme's own casing is a genuine per-target difference, and only because the
            // bypass is gated. RFC 3986 §3.1 makes lower case the canonical form and System.Uri
            // normalizes to it, so on .NET 10 the authored DATA: comes back as data:; before .NET 10
            // the string is held verbatim and the authored casing survives. Measured, not assumed:
            // new Uri("DATA:text/plain;base64,SGVsbG8=").AbsoluteUri is "data:text/plain;base64,SGVsbG8=".
#if NET10_0_OR_GREATER
            Assert.Equal($"data:{payload}", uri.AbsoluteUri);
#else
            Assert.Equal(input, uri.AbsoluteUri);
#endif
        }

        [Fact]
        public void DataUri_NonBase64Payload_EscapesOnNet10AndIsVerbatimBefore()
        {
            // The deliberate per-target-framework difference, pinned so it cannot change unnoticed.
            // .NET 10's System.Uri percent-escapes a literal-text data: payload; holding the raw
            // string (the pre-.NET-10 path) does not. Keeping the #if NET10_0_OR_GREATER gate in
            // RUri is what preserves the better .NET 10 behavior instead of flattening both targets
            // onto the older one.
            var uri = new RUri("data:text/plain,Hello World!");

#if NET10_0_OR_GREATER
            Assert.Equal("data:text/plain,Hello%20World!", uri.AbsoluteUri);
#else
            Assert.Equal("data:text/plain,Hello World!", uri.AbsoluteUri);
#endif
        }

        [Fact]
        public void FileUri_ReportsFileSchemeAndIsFile()
        {
            var path = OperatingSystem.IsWindows() ? @"C:\dir\page.html" : "/dir/page.html";

            var uri = new RUri(new Uri(path));

            Assert.Equal("file", uri.Scheme);
            Assert.True(uri.IsFile);
            Assert.True(uri.IsAbsoluteUri);
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
            await response!.ResourceStream!.CopyToAsync(memoryStream, TestContext.Current.CancellationToken);
            Assert.Equal(bytes, memoryStream.ToArray());
        }
    }
}
