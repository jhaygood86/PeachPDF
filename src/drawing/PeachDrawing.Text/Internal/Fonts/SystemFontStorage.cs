using System;
using System.Collections.Generic;
using System.IO;
using PeachDrawing.Text.Internal.Fonts.Apple;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// Opens the "path" the system font list names a font by. Nearly every path is a file. A font the platform
    /// will not hand out as a file (every system font on iOS) is named by <see cref="VirtualPrefix"/> and its
    /// PostScript name, and is made on demand from the tables the platform does hand out.
    /// </summary>
    internal static class SystemFontStorage
    {
        /// <summary>Starts the path of a font that has no file: the rest of the path is its PostScript name.</summary>
        internal const string VirtualPrefix = "coretext:";

        /// <summary>The tables a font description is read from (names, weight and width class, variation axes).</summary>
        internal static readonly string[] DescriptionTables = ["name", "OS/2", "fvar"];

        /// <summary>The tables the codepoints a font covers are read from.</summary>
        internal static readonly string[] CoverageTables = ["cmap"];

        /// <summary>Produces the font behind a virtual path as an sfnt holding the requested tables (all of them when null), or null when there is no such font.</summary>
        internal delegate byte[]? VirtualFontLoader(string virtualPath, IReadOnlyList<string>? tables);

        internal static bool IsVirtual(string path) => path.StartsWith(VirtualPrefix, StringComparison.Ordinal);

        /// <summary>The whole font.</summary>
        internal static Stream OpenRead(string path, VirtualFontLoader? loader = null) => Open(path, null, loader);

        /// <summary>A stream to read the font's description from. For a virtual path only the tables a description needs are made.</summary>
        internal static Stream OpenForDescription(string path, VirtualFontLoader? loader = null) => Open(path, DescriptionTables, loader);

        /// <summary>A stream to read the font's coverage from. For a virtual path only the <c>cmap</c> is made.</summary>
        internal static Stream OpenForCoverage(string path, VirtualFontLoader? loader = null) => Open(path, CoverageTables, loader);

        /// <summary>The bytes of a virtual path's font as a standalone sfnt.</summary>
        internal static byte[] ReadVirtual(string path, VirtualFontLoader? loader = null) =>
            (loader ?? DefaultLoader)(path, null) ?? throw new FileNotFoundException("The platform has no font with this name.", path);

        private static Stream Open(string path, IReadOnlyList<string>? tables, VirtualFontLoader? loader)
        {
            if (!IsVirtual(path))
                return File.OpenRead(path);

            var bytes = (loader ?? DefaultLoader)(path, tables)
                        ?? throw new FileNotFoundException("The platform has no font with this name.", path);
            return new MemoryStream(bytes, writable: false);
        }

        private static byte[]? DefaultLoader(string virtualPath, IReadOnlyList<string>? tables)
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
                return AppleSystemFonts.Assemble(virtualPath, tables);

            return null;
        }
    }
}
