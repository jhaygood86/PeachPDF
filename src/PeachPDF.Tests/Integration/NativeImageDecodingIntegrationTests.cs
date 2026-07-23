using PeachPDF.Imaging;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// OS-specific native image codec tests, following the dynamic-skip convention established by
    /// <see cref="PeachPDF.Tests.Network.MimeTypeResolverTests"/> (<c>Assert.Skip</c>, not a silent no-op
    /// return) - each test reports as skipped, not passed, on an OS it doesn't apply to.
    /// </summary>
    public class NativeImageDecodingIntegrationTests
    {
        [Fact]
        public void Windows_Wic_DecodesJpegPngGif()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("Windows-only: exercises the WIC native codec.");
                return;
            }

            var decoder = PlatformImageCodecs.Decoder;
            Assert.NotNull(decoder);

            foreach (var path in new[] { BundledImages.Jpg, BundledImages.Png, BundledImages.Gif })
            {
                Assert.True(decoder!.TryDecode(File.ReadAllBytes(path), out var decoded), $"WIC failed to decode {path}");
                Assert.Equal(16, decoded.Width);
                Assert.Equal(16, decoded.Height);
            }
        }

        [Fact]
        public void Windows_Wic_ReEncodesJpegAndBmp()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("Windows-only: exercises the WIC native encoder.");
                return;
            }

            var decoder = PlatformImageCodecs.Decoder;
            var encoder = PlatformImageCodecs.Encoder;
            Assert.NotNull(decoder);
            Assert.NotNull(encoder);

            Assert.True(decoder!.TryDecode(File.ReadAllBytes(BundledImages.Png), out var decoded));

            Assert.True(encoder!.TryEncodeJpeg(decoded, 90, out var jpeg));
            Assert.True(jpeg.Length > 2);
            Assert.Equal(0xFF, jpeg[0]);
            Assert.Equal(0xD8, jpeg[1]);

            Assert.True(encoder.TryEncodeBmp(decoded, out var bmp));
            Assert.True(bmp.Length > 2);
            Assert.Equal((byte)'B', bmp[0]);
            Assert.Equal((byte)'M', bmp[1]);
        }

        [Fact]
        public void Windows_Wic_WebPAndAvif_DecodeIfOsCodecPackAvailable()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("Windows-only: exercises the optional WIC WebP/AVIF codec packs.");
                return;
            }

            var decoder = PlatformImageCodecs.Decoder;
            Assert.NotNull(decoder);

            if (!decoder!.TryDecode(File.ReadAllBytes(BundledImages.WebP), out var webp))
            {
                Assert.Skip("The WIC WebP Image Extensions codec pack is not installed on this machine.");
                return;
            }

            Assert.Equal(16, webp.Width);
            Assert.Equal(16, webp.Height);

            if (!decoder.TryDecode(File.ReadAllBytes(BundledImages.Avif), out var avif))
            {
                Assert.Skip("The WIC AVIF Image Extension codec pack is not installed on this machine.");
                return;
            }

            Assert.Equal(16, avif.Width);
            Assert.Equal(16, avif.Height);
        }

        [Fact]
        public void Linux_LibavifLibwebp_AreInstalledAndDecode()
        {
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                Assert.Skip("Linux-only: exercises libavif/libwebp.");
                return;
            }

            // This is an enforced precondition (per this project's Linux native codec requirement),
            // not an "OS doesn't apply" skip - a Linux CI runner missing these libraries is a real
            // failure, since the CI workflow is expected to install them.
            var decoder = PlatformImageCodecs.Decoder;
            Assert.True(decoder is not null, "libwebp and/or libavif are not installed - see the CI workflow's apt-get step for the runtime packages required.");

            Assert.True(decoder!.TryDecode(File.ReadAllBytes(BundledImages.WebP), out var webp), "libwebp failed to decode the bundled WebP fixture.");
            Assert.Equal(16, webp.Width);
            Assert.Equal(16, webp.Height);

            Assert.True(decoder.TryDecode(File.ReadAllBytes(BundledImages.Avif), out var avif), "libavif failed to decode the bundled AVIF fixture.");
            Assert.Equal(16, avif.Width);
            Assert.Equal(16, avif.Height);
        }

        [Fact]
        public void MacOS_ImageIo_DecodesAllFormatsAndReEncodes()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Skip("macOS-only: exercises Image I/O.");
                return;
            }

            var decoder = PlatformImageCodecs.Decoder;
            var encoder = PlatformImageCodecs.Encoder;
            Assert.NotNull(decoder);
            Assert.NotNull(encoder);

            DecodedImage? forEncode = null;
            foreach (var path in new[] { BundledImages.Jpg, BundledImages.Png, BundledImages.Gif, BundledImages.WebP, BundledImages.Avif })
            {
                Assert.True(decoder!.TryDecode(File.ReadAllBytes(path), out var decoded), $"Image I/O failed to decode {path}");
                Assert.Equal(16, decoded.Width);
                Assert.Equal(16, decoded.Height);
                forEncode ??= decoded;
            }

            Assert.True(encoder!.TryEncodeJpeg(forEncode!.Value, 90, out var jpeg));
            Assert.Equal(0xFF, jpeg[0]);
            Assert.Equal(0xD8, jpeg[1]);

            Assert.True(encoder.TryEncodeBmp(forEncode.Value, out var bmp));
            Assert.Equal((byte)'B', bmp[0]);
            Assert.Equal((byte)'M', bmp[1]);
        }

        [Fact]
        public void Android_NdkImageDecoder_Decodes()
        {
            if (!OperatingSystem.IsAndroid())
            {
                Assert.Skip("Android-only: exercises the NDK ImageDecoder API. This repository's CI matrix " +
                    "(windows-latest/ubuntu-latest/macos-latest) has no Android runner, so this always skips here.");
                return;
            }

            var decoder = PlatformImageCodecs.Decoder;
            Assert.NotNull(decoder);

            Assert.True(decoder!.TryDecode(File.ReadAllBytes(BundledImages.Png), out var decoded));
            Assert.Equal(16, decoded.Width);
            Assert.Equal(16, decoded.Height);
        }
    }
}
