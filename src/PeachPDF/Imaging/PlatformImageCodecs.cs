using System;
using System.Diagnostics.CodeAnalysis;
using PeachPDF.Imaging.Android;
using PeachPDF.Imaging.Apple;
using PeachPDF.Imaging.Linux;
using PeachPDF.Imaging.Windows;

namespace PeachPDF.Imaging
{
    /// <summary>
    /// Resolves the current OS's native image codec, if any, once per process. Selection is a plain
    /// OperatingSystem.Is*() branch - deliberately not reflection/plugin-based, so this stays AOT/trim
    /// safe. <see cref="OperatingSystem.IsLinux"/> is also true on Android, so Android must be checked
    /// first (matching the existing convention in e.g. GenericFontFamilyIntegrationTests).
    /// </summary>
    internal static class PlatformImageCodecs
    {
        // Only one branch is ever reachable on a given OS, so no single CI matrix job can exercise all of
        // them - excluded from coverage for the same reason the platform codec implementations themselves
        // are (see coverlet.runsettings).
        [ExcludeFromCodeCoverage]
        static PlatformImageCodecs()
        {
            if (OperatingSystem.IsWindows())
            {
                var wic = new WicImageCodec();
                Decoder = wic;
                Encoder = wic;
            }
            else if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            {
                var imageIo = new AppleImageIoCodec();
                Decoder = imageIo;
                Encoder = imageIo;
            }
            else if (OperatingSystem.IsAndroid())
            {
                Decoder = new AndroidNdkImageDecoder();
            }
            else if (OperatingSystem.IsLinux())
            {
                Decoder = LinuxPlatformCodec.TryCreate();
            }
        }

        /// <summary>
        /// The current OS's native decoder, or null if this OS has none (or none of its libraries loaded).
        /// </summary>
        public static IPlatformImageDecoder? Decoder { get; }

        /// <summary>
        /// The current OS's native JPEG/BMP encoder, or null if this OS has none - every OS lacking one
        /// always falls back to StbImageWriteSharp for re-encoding.
        /// </summary>
        public static IPlatformImageEncoder? Encoder { get; }

        /// <summary>
        /// Test-only seam (see InternalsVisibleTo PeachPDF.Tests) that forces the STB fallback path so it
        /// can be exercised deterministically regardless of which OS the test suite is actually running on.
        /// </summary>
        internal static bool DisableNativeCodecsForTesting { get; set; }
    }
}
