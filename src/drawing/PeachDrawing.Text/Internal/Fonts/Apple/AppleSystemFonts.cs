using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Versioning;

namespace PeachDrawing.Text.Internal.Fonts.Apple
{
    /// <summary>
    /// The installed fonts of macOS, Mac Catalyst, iOS and tvOS, found and read through CoreText: the only
    /// source of system fonts on those platforms. The directories a font may live in are not scanned. CoreText
    /// knows every installed font (also those a directory scan never saw: downloadable assets, fonts an app
    /// registered, fonts the system keeps out of reach of an app's sandbox).
    /// </summary>
    /// <remarks>
    /// A font is named to the rest of the pipeline by a "path". Where CoreText reports a readable font file
    /// (macOS), the path is that file's. Otherwise (every system font on iOS) it is a <see cref="SystemFontStorage.VirtualPrefix"/>
    /// followed by the font's PostScript name, and the bytes are made by <see cref="SfntAssembler"/> from the
    /// tables CoreText hands out. Excluded from code coverage like the other platform-specific native lookups.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("maccatalyst")]
    [SupportedOSPlatform("tvos")]
    internal static class AppleSystemFonts
    {
        private static readonly string[] FileExtensions = [".ttf", ".otf", ".ttc", ".otc"];

        private static string UiVirtualPath(AppleUiDesign design) => SystemFontStorage.VirtualPrefix + UiMarker + design.ToString().ToLowerInvariant();

        private const string UiMarker = "@ui:";

        /// <summary>
        /// Every installed font. With <paramref name="preferFiles"/>, a font CoreText backs with a readable
        /// <c>.ttf</c>/<c>.otf</c>/<c>.ttc</c>/<c>.otc</c> file is named by that file (a collection once, however many
        /// faces it holds); every other font, and every font when it is false, is named by its PostScript name.
        /// Empty when CoreText cannot be reached.
        /// </summary>
        internal static string[] Discover(bool preferFiles) => Discover(preferFiles, out _);

        /// <summary>
        /// <see cref="Discover(bool)"/>, also telling where the system interface font of each <see cref="AppleUiDesign"/> was found (by
        /// the "path" the list names it by), which is how the resolver finds the family one of them was registered under. Empty when
        /// CoreText cannot be reached.
        /// </summary>
        internal static string[] Discover(bool preferFiles, out IReadOnlyDictionary<AppleUiDesign, string> uiPaths)
        {
            uiPaths = new Dictionary<AppleUiDesign, string>();
            try
            {
                var collection = CoreTextNative.CTFontCollectionCreateFromAvailableFonts(0);
                if (collection == 0)
                    return [];

                try
                {
                    var descriptors = CoreTextNative.CTFontCollectionCreateMatchingFontDescriptors(collection);
                    if (descriptors == 0)
                        return [];

                    try
                    {
                        return AddUiFonts(ListDescriptors(descriptors, preferFiles), preferFiles, (Dictionary<AppleUiDesign, string>)uiPaths);
                    }
                    finally
                    {
                        CoreTextNative.CFRelease(descriptors);
                    }
                }
                finally
                {
                    CoreTextNative.CFRelease(collection);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return [];
            }
        }

        /// <summary>
        /// CoreText's list of available fonts leaves out the fonts the system keeps for its own interface (San
        /// Francisco, New York, SF Mono, SF Rounded), which is why they are asked for by what they are for.
        /// Each is added to <paramref name="listed"/> and recorded in <paramref name="uiPaths"/>.
        /// </summary>
        private static string[] AddUiFonts(string[] listed, bool preferFiles, Dictionary<AppleUiDesign, string> uiPaths)
        {
            var result = new List<string>(listed);
            var seen = new HashSet<string>(listed, StringComparer.Ordinal);

            foreach (var design in Enum.GetValues<AppleUiDesign>())
            {
                var font = OpenUiFont(design);
                if (font == 0)
                    continue;

                try
                {
                    string? path = null;
                    if (preferFiles)
                    {
                        var urlKey = CoreTextNative.ReadConstant("kCTFontURLAttribute");
                        var url = urlKey == 0 ? 0 : CoreTextNative.CTFontCopyAttribute(font, urlKey);
                        if (url != 0)
                        {
                            try
                            {
                                path = ReadableFile(CoreTextNative.ReadFilePath(url));
                            }
                            finally
                            {
                                CoreTextNative.CFRelease(url);
                            }
                        }
                    }

                    path ??= UiVirtualPath(design);
                    uiPaths[design] = path;
                    if (seen.Add(path))
                        result.Add(path);
                }
                finally
                {
                    CoreTextNative.CFRelease(font);
                }
            }

            return [.. result];
        }

        /// <summary>A CTFont for the system user interface font in <paramref name="design"/>; zero when the system has none. The caller releases it.</summary>
        private static nint OpenUiFont(AppleUiDesign design)
        {
            var system = CoreTextNative.CTFontCreateUIFontForLanguage(CoreTextNative.UiFontSystem, 12, 0);
            if (system == 0 || design == AppleUiDesign.System)
                return system;

            try
            {
                var traits = CoreTextNative.ReadConstant("kCTFontTraitsAttribute");
                var designKey = CoreTextNative.ReadConstant("kCTFontUIFontDesignTrait");
                var designValue = CoreTextNative.ReadConstant(design switch
                {
                    AppleUiDesign.Serif => "kCTFontUIFontDesignSerif",
                    AppleUiDesign.Monospaced => "kCTFontUIFontDesignMonospaced",
                    _ => "kCTFontUIFontDesignRounded"
                });

                if (traits == 0)
                    return 0;

                var designTraits = CoreTextNative.CreateDictionary(designKey, designValue);
                if (designTraits == 0)
                    return 0;

                try
                {
                    var attributes = CoreTextNative.CreateDictionary(traits, designTraits);
                    if (attributes == 0)
                        return 0;

                    try
                    {
                        var original = CoreTextNative.CTFontCopyFontDescriptor(system);
                        if (original == 0)
                            return 0;

                        try
                        {
                            var changed = CoreTextNative.CTFontDescriptorCreateCopyWithAttributes(original, attributes);
                            if (changed == 0)
                                return 0;

                            try
                            {
                                return CoreTextNative.CTFontCreateWithFontDescriptor(changed, 12, 0);
                            }
                            finally
                            {
                                CoreTextNative.CFRelease(changed);
                            }
                        }
                        finally
                        {
                            CoreTextNative.CFRelease(original);
                        }
                    }
                    finally
                    {
                        CoreTextNative.CFRelease(attributes);
                    }
                }
                finally
                {
                    CoreTextNative.CFRelease(designTraits);
                }
            }
            finally
            {
                CoreTextNative.CFRelease(system);
            }
        }

        private static string[] ListDescriptors(nint descriptors, bool preferFiles)
        {
            var nameKey = CoreTextNative.ReadConstant("kCTFontNameAttribute");
            var urlKey = CoreTextNative.ReadConstant("kCTFontURLAttribute");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            var count = (int)CoreTextNative.CFArrayGetCount(descriptors);
            for (var i = 0; i < count; i++)
            {
                var descriptor = CoreTextNative.CFArrayGetValueAtIndex(descriptors, i);

                string? path = null;
                if (preferFiles && urlKey != 0)
                    path = ReadableFontFile(descriptor, urlKey);

                path ??= NameOf(descriptor, nameKey) is { Length: > 0 } name ? SystemFontStorage.VirtualPrefix + name : null;

                if (path is not null && seen.Add(path))
                    result.Add(path);
            }

            return [.. result];
        }

        private static string? ReadableFontFile(nint descriptor, nint urlKey)
        {
            var url = CoreTextNative.CTFontDescriptorCopyAttribute(descriptor, urlKey);
            if (url == 0)
                return null;

            try
            {
                return ReadableFile(CoreTextNative.ReadFilePath(url));
            }
            finally
            {
                CoreTextNative.CFRelease(url);
            }
        }

        /// <summary>The path when it is a font file this pipeline reads and exists, else null.</summary>
        private static string? ReadableFile(string? path)
        {
            if (path is null || Array.IndexOf(FileExtensions, Path.GetExtension(path).ToLowerInvariant()) < 0)
                return null;

            return File.Exists(path) ? path : null;
        }

        private static string? NameOf(nint descriptor, nint nameKey)
        {
            if (nameKey == 0)
                return null;

            var name = CoreTextNative.CTFontDescriptorCopyAttribute(descriptor, nameKey);
            if (name == 0)
                return null;

            try
            {
                return CoreTextNative.ReadString(name);
            }
            finally
            {
                CoreTextNative.CFRelease(name);
            }
        }

        /// <summary>
        /// The font named by the virtual path <paramref name="virtualPath"/> as a standalone sfnt, holding every
        /// table CoreText has for it, or only the tables in <paramref name="tags"/> that it has. Null when CoreText
        /// has no font of that PostScript name.
        /// </summary>
        internal static byte[]? Assemble(string virtualPath, IReadOnlyList<string>? tags)
        {
            var name = virtualPath[SystemFontStorage.VirtualPrefix.Length..];

            var font = name.StartsWith(UiMarker, StringComparison.Ordinal)
                ? (Enum.TryParse<AppleUiDesign>(name[UiMarker.Length..], ignoreCase: true, out var design) ? OpenUiFont(design) : 0)
                : OpenByPostScriptName(name);
            if (font == 0)
                return null;

            try
            {
                var wanted = new List<uint>();
                if (tags is null)
                {
                    var available = CoreTextNative.CTFontCopyAvailableTables(font, 0);
                    if (available != 0)
                    {
                        try
                        {
                            var count = (int)CoreTextNative.CFArrayGetCount(available);
                            for (var i = 0; i < count; i++)
                                wanted.Add((uint)CoreTextNative.CFArrayGetValueAtIndex(available, i));
                        }
                        finally
                        {
                            CoreTextNative.CFRelease(available);
                        }
                    }
                }
                else
                {
                    foreach (var tag in tags)
                        wanted.Add(SfntAssembler.Tag(tag));
                }

                var tables = new List<(uint Tag, byte[] Data)>(wanted.Count);
                foreach (var tag in wanted)
                {
                    var data = CoreTextNative.CTFontCopyTable(font, tag, 0);
                    if (data == 0)
                        continue;

                    try
                    {
                        tables.Add((tag, CoreTextNative.ReadData(data)));
                    }
                    finally
                    {
                        CoreTextNative.CFRelease(data);
                    }
                }

                return tables.Count == 0 ? null : SfntAssembler.Build(tables);
            }
            finally
            {
                CoreTextNative.CFRelease(font);
            }
        }

        /// <summary>
        /// A CTFont for exactly the font with this PostScript name. CoreText answers a name it does not know with
        /// a substitute font, so the answer is only taken when it carries the name that was asked for.
        /// </summary>
        private static nint OpenByPostScriptName(string postScriptName)
        {
            var name = CoreTextNative.CreateString(postScriptName);
            if (name == 0)
                return 0;

            try
            {
                var font = CoreTextNative.CTFontCreateWithName(name, 12, 0);
                if (font == 0)
                    return 0;

                var actual = CoreTextNative.CTFontCopyPostScriptName(font);
                try
                {
                    if (string.Equals(CoreTextNative.ReadString(actual), postScriptName, StringComparison.Ordinal))
                        return font;
                }
                finally
                {
                    if (actual != 0)
                        CoreTextNative.CFRelease(actual);
                }

                CoreTextNative.CFRelease(font);
                return 0;
            }
            finally
            {
                CoreTextNative.CFRelease(name);
            }
        }
    }
}
