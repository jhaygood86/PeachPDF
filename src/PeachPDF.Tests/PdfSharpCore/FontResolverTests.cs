using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class FontResolverTests
    {
        [Fact]
        public void SupportedFonts_ContainsFonts()
        {
            Assert.NotEmpty(FontResolver.SupportedFonts);
        }

        [Fact]
        public void ResolveTypeface_KnownFamily_ReturnsResolverInfo()
        {
            var resolver = new FontResolver();

            // A system font if one was detected, otherwise the bundled test font is
            // registered — either way, at least one family is guaranteed.
            var familyName = BundledFonts.GetOrRegisterKnownFamily(resolver);

            var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);

            Assert.NotNull(info);
            Assert.NotEmpty(info.FaceName);
        }

        [Fact]
        public void ResolveTypeface_UnknownFamily_ReturnsFallback()
        {
            var resolver = new FontResolver();

            // Unknown families fall back to the first available font rather than throwing
            var info = resolver.ResolveTypeface("__NonExistentFamily__", isBold: false, isItalic: false);

            Assert.NotNull(info);
        }

        [Fact]
        public void ResolveTypeface_NullIfNotFound_ReturnsNull()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };

            var info = resolver.ResolveTypeface("__NonExistentFamily__", isBold: false, isItalic: false);

            Assert.Null(info);
        }

        [Fact]
        public void GetFont_ResolvableFaceName_ReturnsFontBytes()
        {
            var resolver = new FontResolver();
            var familyName = BundledFonts.GetOrRegisterKnownFamily(resolver);

            var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);
            var bytes = resolver.GetFont(info.FaceName);

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }

        [Fact]
        public void HasFont_KnownFaceName_ReturnsTrue()
        {
            var resolver = new FontResolver();
            var familyName = BundledFonts.GetOrRegisterKnownFamily(resolver);
            var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);

            Assert.True(resolver.HasFont(info.FaceName));
        }

        [Fact]
        public void HasFont_UnknownFaceName_ReturnsFalse()
        {
            var resolver = new FontResolver();

            Assert.False(resolver.HasFont("__NoSuchFace__"));
        }

        [Fact]
        public void AddFont_CustomFont_IsResolvableByFamilyName()
        {
            var resolver = new FontResolver();
            var fontPath = BundledFonts.AnySupportedFontPath;
            var fontDesc = TtfFontDescription.LoadDescription(fontPath);
            const string customFamilyName = "__TestCustomFamily__";

            using var stream = File.OpenRead(fontPath);
            resolver.AddFont(stream, customFamilyName);

            var info = resolver.ResolveTypeface(customFamilyName, isBold: false, isItalic: false);
            Assert.NotNull(info);
            Assert.NotEmpty(info.FaceName);
        }

        [Fact]
        public void AddFont_CustomFont_BytesRetrievableByFaceName()
        {
            var resolver = new FontResolver();
            var fontPath = BundledFonts.AnySupportedFontPath;
            const string customFamilyName = "__TestCustomFamily2__";

            using var stream = File.OpenRead(fontPath);
            resolver.AddFont(stream, customFamilyName);

            var info = resolver.ResolveTypeface(customFamilyName, isBold: false, isItalic: false);
            var bytes = resolver.GetFont(info.FaceName);

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }

        [Fact]
        public void DiscoverSupportedFonts_UnsupportedPlatform_ReturnsEmpty()
        {
            var fonts = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: false, isWindows: false, isAndroid: false, isIOS: false);

            Assert.Empty(fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_Linux_DelegatesToLinuxSystemFontResolver()
        {
            var fonts = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: true, isWindows: false, isAndroid: false, isIOS: false);

            Assert.Equal(LinuxSystemFontResolver.Resolve(), fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_Windows_ReturnsSystemFontFiles()
        {
            var fonts = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: false, isWindows: true, isAndroid: false, isIOS: false);

            Assert.NotNull(fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_OSX_UsesLibraryFontsDirectory()
        {
            // The macOS font directories don't exist on non-macOS test runners, but the
            // branch must still be exercised so it's covered on the platforms where it does.
            // Missing directories are skipped rather than throwing, so this returns an
            // empty (but non-null) array on non-macOS runners.
            var fonts = FontResolver.DiscoverSupportedFonts(
                isOSX: true, isLinux: false, isWindows: false, isAndroid: false, isIOS: false);

            Assert.NotNull(fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_OSX_IncludesUserLibraryFontsWhenHomeIsSet()
        {
            // Exercises the ~/Library/Fonts candidate path directly, regardless of whether
            // the HOME environment variable happens to be set on the host running the tests
            // (it typically isn't on Windows).
            var originalHome = Environment.GetEnvironmentVariable("HOME");
            try
            {
                Environment.SetEnvironmentVariable("HOME", Path.GetTempPath());

                var fonts = FontResolver.DiscoverSupportedFonts(
                    isOSX: true, isLinux: false, isWindows: false, isAndroid: false, isIOS: false);

                Assert.NotNull(fonts);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", originalHome);
            }
        }

        [Fact]
        public void DiscoverSupportedFonts_OSX_FindsFontsInAnExistingLibraryFontsDirectory()
        {
            // The candidate-path existence checks (Directory.Exists) mean GetFontFiles's
            // actual directory scan never runs on CI runners where none of the hardcoded
            // macOS font directories exist. Point HOME at a temp directory that really does
            // have a populated Library/Fonts subfolder so that scan executes for real,
            // regardless of host OS.
            var homeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var fontsDir = Path.Combine(homeDir, "Library", "Fonts");
            Directory.CreateDirectory(fontsDir);
            File.Copy(BundledFonts.Ttf, Path.Combine(fontsDir, Path.GetFileName(BundledFonts.Ttf)));

            var originalHome = Environment.GetEnvironmentVariable("HOME");
            try
            {
                Environment.SetEnvironmentVariable("HOME", homeDir);

                var fonts = FontResolver.DiscoverSupportedFonts(
                    isOSX: true, isLinux: false, isWindows: false, isAndroid: false, isIOS: false);

                Assert.Contains(fonts, f => Path.GetDirectoryName(f) == fontsDir);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", originalHome);
                Directory.Delete(homeDir, recursive: true);
            }
        }

        [Fact]
        public void DiscoverSupportedFonts_Android_ScansSystemFontDirectories()
        {
            // /system/fonts, /product/fonts and /data/fonts don't exist on non-Android test
            // runners, but the branch must still be exercised so it's covered on the
            // platforms where it does. Missing/unreadable directories are skipped rather
            // than throwing, so this returns an empty (but non-null) array here.
            var fonts = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: false, isWindows: false, isAndroid: true, isIOS: false);

            Assert.NotNull(fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_Android_TakesPriorityOverLinux()
        {
            // RuntimeInformation.IsOSPlatform(OSPlatform.Linux) is not guaranteed to exclude
            // Android (Linux-kernel-based). Guards against a regression where Android would
            // be routed into LinuxSystemFontResolver.Resolve() (which P/Invokes
            // libfontconfig.so.1 -- not present/meaningful on Android) instead of its own
            // branch when isLinux happens to also be true: the result must be identical to
            // isAndroid alone, regardless of isLinux.
            var androidOnly = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: false, isWindows: false, isAndroid: true, isIOS: false);
            var androidAndLinux = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: true, isWindows: false, isAndroid: true, isIOS: false);

            Assert.Equal(androidOnly, androidAndLinux);
        }

        [Fact]
        public void DiscoverSupportedFonts_IOS_ReturnsEmpty()
        {
            // iOS has no readable system font files and no public API to extract raw font
            // bytes from CoreText -- this is an intentional, permanent no-op, not a gap to
            // be filled in later. iOS apps are expected to embed and register their own
            // fonts via PdfGenerator.AddFontFromStream.
            var fonts = FontResolver.DiscoverSupportedFonts(
                isOSX: false, isLinux: false, isWindows: false, isAndroid: false, isIOS: true);

            Assert.Empty(fonts);
        }

        [Fact]
        public void AddFont_CollidingWithSystemFamily_DoesNotMutateOtherInstances()
        {
            // System-font family data is parsed once and shared (read-only) across every
            // FontResolver instance. AddFont must clone a family before overriding one of
            // its styles, or a custom font registered on one instance would corrupt the
            // family every other FontResolver instance -- and every other thread's
            // PdfGenerator -- resolves for that same family name.
            if (FontResolver.SupportedFonts.Length == 0)
                Assert.Skip("No system fonts detected on this host to collide with.");

            var familyName = TtfFontDescription.LoadDescription(FontResolver.SupportedFonts[0]).FontFamilyInvariantCulture;
            var bundledFaceName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture;

            var expectedFaceName = new FontResolver()
                .ResolveTypeface(familyName, isBold: false, isItalic: false).FaceName;

            // A different family's file would never collide with the bundled test font's own
            // name, which is the whole point: registering it under `familyName` overrides the
            // Regular style of an otherwise-unrelated system family on this instance only.
            Assert.NotEqual(bundledFaceName, expectedFaceName);

            var mutated = new FontResolver();
            using (var stream = File.OpenRead(BundledFonts.Ttf))
            {
                mutated.AddFont(stream, familyName);
            }

            var mutatedFaceName = mutated.ResolveTypeface(familyName, isBold: false, isItalic: false).FaceName;
            Assert.Equal(bundledFaceName, mutatedFaceName);

            // A resolver constructed after `mutated.AddFont` must still see the original
            // system font for this family -- proving the shared family data was cloned
            // rather than mutated in place.
            var untouchedFaceName = new FontResolver()
                .ResolveTypeface(familyName, isBold: false, isItalic: false).FaceName;

            Assert.Equal(expectedFaceName, untouchedFaceName);
        }

        [Fact]
        public async Task ConcurrentConstructionAndLocalFontResolution_DoesNotThrow()
        {
            // Regression test for a race between FontResolver's system-font initialization
            // and instance construction/local-font lookups running concurrently across many
            // threads, simulating many PdfGenerator instances (one per thread) in concurrent
            // use.
            var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    var resolver = new FontResolver();
                    var familyName = BundledFonts.GetOrRegisterKnownFamily(resolver);
                    var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);

                    Assert.NotNull(info);
                    Assert.True(resolver.HasFont(info.FaceName));

                    var bytes = resolver.GetFont(info.FaceName);
                    Assert.True(bytes.Length > 0);
                }
            }));

            await Task.WhenAll(tasks);
        }
    }
}
