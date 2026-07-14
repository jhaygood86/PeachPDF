#nullable disable warnings


using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Internal;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;


namespace PeachPDF.PdfSharpCore.Utils
{


    internal class FontResolver : IFontResolver
    {
        private static readonly FrozenDictionary<string, string> _systemFontPaths;
        private static readonly FrozenDictionary<string, FontFamilyModel> _systemFamilies;

        private readonly Dictionary<string, byte[]> _CustomFonts = [];
        private readonly Dictionary<string, FontFamilyModel> InstalledFonts;

        public static string[] SupportedFonts { get; }

        private static readonly string[] FontExtensions = ["*.ttf", "*.otf"];

        private static string[] GetFontFiles(string dir)
        {
            if (!Directory.Exists(dir))
                return [];

            try
            {
                return FontExtensions
                    .SelectMany(pattern => Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                // Some directories (e.g. Android's /data/fonts, /product/fonts) may exist
                // but be unreadable depending on OEM/OS-version SELinux policy. Treat that
                // the same as "no fonts here" rather than failing font discovery entirely.
                return [];
            }
        }

        static FontResolver()
        {
            var isAndroid = System.OperatingSystem.IsAndroid();
            var isIOS = System.OperatingSystem.IsIOS();
            var isOSX = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            var isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            SupportedFonts = DiscoverSupportedFonts(isOSX, isLinux, isWindows, isAndroid, isIOS);

            (_systemFontPaths, _systemFamilies) = ParseSystemFonts(SupportedFonts);
        }

        internal static string[] DiscoverSupportedFonts(bool isOSX, bool isLinux, bool isWindows, bool isAndroid, bool isIOS)
        {
            // Checked first: RuntimeInformation.IsOSPlatform(OSPlatform.Linux) is not
            // guaranteed to exclude Android (Linux-kernel-based), so Android/iOS must be
            // routed to their own branches before isLinux/isOSX are consulted.
            if (isAndroid)
            {
                var candidateDirs = new[] { "/system/fonts", "/product/fonts", "/data/fonts" };

                return candidateDirs
                    .SelectMany(GetFontFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (isIOS)
            {
                // iOS sandboxes apps away from system font files entirely, and CoreText
                // exposes fonts only as opaque handles (CTFont/UIFont) with no public API
                // to extract raw file bytes. There is nothing on-disk this resolver can
                // discover here. iOS apps should embed their own fonts and register them
                // via PdfGenerator.AddFontFromStream.
                return [];
            }

            if (isOSX)
            {
                var homeDir = System.Environment.GetEnvironmentVariable("HOME");
                var candidateDirs = new List<string> { "/System/Library/Fonts", "/Library/Fonts" };
                if (!string.IsNullOrEmpty(homeDir))
                    candidateDirs.Add(Path.Combine(homeDir, "Library", "Fonts"));

                return candidateDirs
                    .SelectMany(GetFontFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (isLinux)
            {
                return LinuxSystemFontResolver.Resolve();
            }

            if (isWindows)
            {
                var fontDir = System.Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts");
                var fontPaths = new List<string>(GetFontFiles(fontDir));

                var appdataFontDir = System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts");
                fontPaths.AddRange(GetFontFiles(appdataFontDir));

                return fontPaths.ToArray();
            }

            // Other platforms without system font discovery (tvOS, watchOS, browser/WASM,
            // ...): start with no system fonts and rely on fonts registered via
            // PdfGenerator.AddFontFromStream.
            return [];
        }


        public FontResolver()
        {
            InstalledFonts = new Dictionary<string, FontFamilyModel>(_systemFamilies);
        }

        private readonly struct FontFileInfo
        {
            private FontFileInfo(TtfFontDescription fontDescription)
            {
                this.FontDescription = fontDescription;
            }

            public TtfFontDescription FontDescription { get; }

            public string FamilyName => this.FontDescription.FontFamilyInvariantCulture;

            public XFontStyle GuessFontStyle() => this.FontDescription.Style;

            public static FontFileInfo Load(string path)
            {
                var fontDescription = TtfFontDescription.LoadDescription(path);
                return new FontFileInfo(fontDescription);
            }

            public static FontFileInfo Load(Stream stream)
            {
                var fontDescription = TtfFontDescription.LoadDescription(stream);
                return new FontFileInfo(fontDescription);
            }
        }

        public void AddFont(Stream stream, string fontFamilyName)
        {
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);

            var fontBytes = memoryStream.ToArray();
            memoryStream.Seek(0, SeekOrigin.Begin);

            var fontFileInfo = FontFileInfo.Load(memoryStream);
            var key = fontFamilyName.ToLower();

            if (InstalledFonts.TryGetValue(key, out var family))
            {
                // family may be a shared static FontFamilyModel from _systemFamilies (or an
                // already-private clone from a prior AddFont call on this instance). Clone
                // before mutating so we never write into state shared with other FontResolver
                // instances/threads.
                var clonedFamily = new FontFamilyModel { Name = family.Name };
                foreach (var fontFile in family.FontFiles)
                {
                    clonedFamily.FontFiles[fontFile.Key] = fontFile.Value;
                }
                clonedFamily.FontFiles[fontFileInfo.GuessFontStyle()] = fontFileInfo.FontDescription;
                InstalledFonts[key] = clonedFamily;
            }
            else
            {
                var fontFamilyModel = DeserializeFontFamily(key, [fontFileInfo]);
                InstalledFonts.Add(key, fontFamilyModel);
            }

            _CustomFonts[fontFileInfo.FontDescription.FontNameInvariantCulture] = fontBytes;
        }

        private static (FrozenDictionary<string, string> Paths, FrozenDictionary<string, FontFamilyModel> Families) ParseSystemFonts(string[] sSupportedFonts)
        {
            var fontPaths = new Dictionary<string, string>();
            var tempFontInfoList = new List<FontFileInfo>();

            foreach (var fontPathFile in sSupportedFonts)
            {
                try
                {
                    var fontInfo = FontFileInfo.Load(fontPathFile);
                    Debug.WriteLine(fontPathFile);
                    tempFontInfoList.Add(fontInfo);

                    if (!fontPaths.ContainsKey(fontInfo.FontDescription.FontNameInvariantCulture))
                    {
                        fontPaths.Add(fontInfo.FontDescription.FontNameInvariantCulture, fontPathFile);
                    }
                }
                catch (System.Exception e)
                {
#if DEBUG
                    System.Console.Error.WriteLine(e);
#endif
                }
            }

            var families = new Dictionary<string, FontFamilyModel>();

            // Deserialize all font families
            foreach (var familyGroup in tempFontInfoList.GroupBy(info => info.FamilyName))
                try
                {
                    var familyName = familyGroup.Key;
                    var family = DeserializeFontFamily(familyName, familyGroup);
                    families.Add(familyName.ToLower(), family);
                }
                catch (System.Exception e)
                {
#if DEBUG
                    System.Console.Error.WriteLine(e);
#endif
                }

            return (fontPaths.ToFrozenDictionary(), families.ToFrozenDictionary());
        }


        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private static FontFamilyModel DeserializeFontFamily(string fontFamilyName, IEnumerable<FontFileInfo> fontList)
        {
            var font = new FontFamilyModel { Name = fontFamilyName };

            // there is only one font
            if (fontList.Count() == 1)
                font.FontFiles.Add(XFontStyle.Regular, fontList.First().FontDescription);
            else
            {
                foreach (var info in fontList)
                {
                    var style = info.GuessFontStyle();
                    if (!font.FontFiles.ContainsKey(style))
                        font.FontFiles.Add(style, info.FontDescription);
                }
            }

            return font;
        }

        public virtual byte[] GetFont(string fontFaceName)
        {
            if (_CustomFonts.TryGetValue(fontFaceName, out var fontBytes))
            {
                return fontBytes;
            }

            if (_systemFontPaths.TryGetValue(fontFaceName, out var fontPath))
            {
                return File.ReadAllBytes(fontPath);
            }

            throw new ArgumentOutOfRangeException(nameof(fontFaceName), "Unknown Font Face Name");
        }

        public bool HasFont(string fontFaceName)
        {
            return _CustomFonts.ContainsKey(fontFaceName) || _systemFontPaths.ContainsKey(fontFaceName);
        }

        public bool NullIfFontNotFound { get; set; } = false;

        public virtual FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (InstalledFonts.Count == 0)
                throw new System.IO.FileNotFoundException("No Fonts installed on this device!");

            if (InstalledFonts.TryGetValue(familyName.ToLower(), out var family))
            {
                switch (isBold)
                {
                    case true when isItalic && family.FontFiles.TryGetValue(XFontStyle.BoldItalic, out var boldItalicFile):
                        return new FontResolverInfo(boldItalicFile.FontNameInvariantCulture);
                    case true:
                        {
                            if (family.FontFiles.TryGetValue(XFontStyle.Bold, out var boldFile))
                                return new FontResolverInfo(boldFile.FontNameInvariantCulture);
                            break;
                        }
                    default:
                        {
                            if (isItalic)
                            {
                                if (family.FontFiles.TryGetValue(XFontStyle.Italic, out var italicFile))
                                    return new FontResolverInfo(italicFile.FontNameInvariantCulture);
                            }

                            break;
                        }
                }

                if (family.FontFiles.TryGetValue(XFontStyle.Regular, out var regularFile))
                    return new FontResolverInfo(regularFile.FontNameInvariantCulture);

                return new FontResolverInfo(family.FontFiles.First().Value.FontNameInvariantCulture);
            }

            if (NullIfFontNotFound)
                return null;

            var description = InstalledFonts.First().Value.FontFiles.First().Value;
            return new FontResolverInfo(description.FontNameInvariantCulture);
        }
    }
}
