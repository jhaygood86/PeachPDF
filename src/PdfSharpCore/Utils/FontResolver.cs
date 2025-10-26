
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Internal;
using SixLabors.Fonts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;


namespace PeachPDF.PdfSharpCore.Utils
{


    public class FontResolver : IFontResolver
    {
        private static readonly Dictionary<string, string> _SystemFontPaths = [];

        private readonly Dictionary<string, byte[]> _CustomFonts = [];
        private readonly Dictionary<string, FontFamilyModel> InstalledFonts = [];

        public static string[] SupportedFonts { get; }

        static FontResolver()
        {
            string fontDir;

            var isOSX = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            if (isOSX)
            {
                fontDir = "/Library/Fonts/";
                SupportedFonts = Directory.GetFiles(fontDir, "*.ttf", System.IO.SearchOption.AllDirectories);
                return;
            }

            var isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            if (isLinux)
            {
                SupportedFonts = LinuxSystemFontResolver.Resolve();
                return;
            }

            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            if (isWindows)
            {
                fontDir = System.Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts");
                var fontPaths = new List<string>();

                var systemFontPaths = System.IO.Directory.GetFiles(fontDir, "*.ttf", SearchOption.AllDirectories);
                fontPaths.AddRange(systemFontPaths);

                var appdataFontDir = System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts");
                if (Directory.Exists(appdataFontDir))
                {
                    var appdataFontPaths = System.IO.Directory.GetFiles(appdataFontDir, "*.ttf", SearchOption.AllDirectories);
                    fontPaths.AddRange(appdataFontPaths);
                }

                SupportedFonts = fontPaths.ToArray();

                return;
            }

            throw new System.NotImplementedException("FontResolver not implemented for this platform (PeachPDF.PdfSharpCore.Utils.FontResolver.cs).");
        }


        public FontResolver()
        {
            SetupFontsFiles(SupportedFonts);
        }

        private readonly struct FontFileInfo
        {
            private FontFileInfo(FontDescription fontDescription)
            {
                this.FontDescription = fontDescription;
            }

            public FontDescription FontDescription { get; }

            public string FamilyName => this.FontDescription.FontFamilyInvariantCulture;


            public XFontStyle GuessFontStyle()
            {
                return this.FontDescription.Style switch
                {
                    FontStyle.Bold => XFontStyle.Bold,
                    FontStyle.Italic => XFontStyle.Italic,
                    FontStyle.BoldItalic => XFontStyle.BoldItalic,
                    _ => XFontStyle.Regular
                };
            }

            public static FontFileInfo Load(string path)
            {
                var fontDescription = FontDescription.LoadDescription(path);
                return new FontFileInfo(fontDescription);
            }

            public static FontFileInfo Load(Stream stream)
            {
                var fontDescription = FontDescription.LoadDescription(stream);
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

            if (InstalledFonts.TryGetValue(fontFamilyName.ToLower(), out var family))
            {
                family.FontFiles[fontFileInfo.GuessFontStyle()] = fontFileInfo.FontDescription;
            }
            else
            {
                var fontFamilyModel = DeserializeFontFamily(fontFamilyName.ToLower(), [fontFileInfo]);
                InstalledFonts.Add(fontFamilyName.ToLower(), fontFamilyModel);
            }

            _CustomFonts[fontFileInfo.FontDescription.FontNameInvariantCulture] = fontBytes;
        }

        public void SetupFontsFiles(string[] sSupportedFonts)
        {
            var tempFontInfoList = new List<FontFileInfo>();

            foreach (var fontPathFile in sSupportedFonts)
            {
                try
                {
                    var fontInfo = FontFileInfo.Load(fontPathFile);
                    Debug.WriteLine(fontPathFile);
                    tempFontInfoList.Add(fontInfo);
                    _SystemFontPaths.Add(fontInfo.FontDescription.FontNameInvariantCulture, fontPathFile);
                }
                catch (System.Exception e)
                {
#if DEBUG
                    System.Console.Error.WriteLine(e);
#endif
                }
            }

            // Deserialize all font families
            foreach (var familyGroup in tempFontInfoList.GroupBy(info => info.FamilyName))
                try
                {
                    var familyName = familyGroup.Key;
                    var family = DeserializeFontFamily(familyName, familyGroup);
                    InstalledFonts.Add(familyName.ToLower(), family);
                }
                catch (System.Exception e)
                {
#if DEBUG
                    System.Console.Error.WriteLine(e);
#endif
                }
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

            if (_SystemFontPaths.TryGetValue(fontFaceName, out var fontPath))
            {
                return File.ReadAllBytes(fontPath);
            }

            throw new ArgumentOutOfRangeException(nameof(fontFaceName), "Unknown Font Face Name");
        }

        public bool HasFont(string fontFaceName)
        {
            return _CustomFonts.ContainsKey(fontFaceName) || _SystemFontPaths.ContainsKey(fontFaceName);
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
