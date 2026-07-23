#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using System.Text.RegularExpressions;


namespace PeachPDF.PdfSharpCore.Utils
{
    internal static partial class LinuxSystemFontResolver
    {
        const string libfontconfig = "libfontconfig.so.1";

        [GeneratedRegex("<dir>(?<dir>.*)</dir>")]
        private static partial Regex ConfDirRegex();


        [LibraryImport(libfontconfig)] private static partial IntPtr FcInitLoadConfigAndFonts();

        static readonly Lazy<IntPtr> fcConfig = new Lazy<IntPtr>(FcInitLoadConfigAndFonts);


        // LibraryImport's source generator can't marshal a custom SafeHandle as a RETURN value (unlike
        // classic DllImport) - each *_Raw entry point returns the bare IntPtr instead, and a small wrapper
        // constructs the SafeHandle manually via each type's internal FromRaw factory.
        [LibraryImport(libfontconfig, EntryPoint = "FcPatternCreate")] private static partial IntPtr FcPatternCreate_Raw();
        public static FcPatternHandle FcPatternCreate() => FcPatternHandle.FromRaw(FcPatternCreate_Raw());

        [LibraryImport(libfontconfig, StringMarshalling = StringMarshalling.Utf8)] public static partial int FcPatternGetString(IntPtr p, string obj, int n, ref IntPtr s);
        [LibraryImport(libfontconfig)] public static partial void FcPatternDestroy(IntPtr pattern);

        internal class FcPatternHandle : SafeHandle
        {
            FcPatternHandle() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid => this.handle == IntPtr.Zero;

            protected override bool ReleaseHandle()
            {
                FcPatternDestroy(this.handle);
                return true;
            }

            internal static FcPatternHandle FromRaw(IntPtr raw)
            {
                var result = new FcPatternHandle();
                result.SetHandle(raw);
                return result;
            }
        }


        [LibraryImport(libfontconfig, EntryPoint = "FcObjectSetCreate")] private static partial IntPtr FcObjectSetCreate_Raw();
        public static FcObjectSetHandle FcObjectSetCreate() => FcObjectSetHandle.FromRaw(FcObjectSetCreate_Raw());

        [LibraryImport(libfontconfig, StringMarshalling = StringMarshalling.Utf8)] public static partial int FcObjectSetAdd(FcObjectSetHandle os, string obj);
        [LibraryImport(libfontconfig)] public static partial void FcObjectSetDestroy(IntPtr os);

        internal class FcObjectSetHandle : SafeHandle
        {
            FcObjectSetHandle() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid => this.handle == IntPtr.Zero;

            protected override bool ReleaseHandle()
            {
                FcObjectSetDestroy(this.handle);
                return true;
            }

            internal static FcObjectSetHandle FromRaw(IntPtr raw)
            {
                var result = new FcObjectSetHandle();
                result.SetHandle(raw);
                return result;
            }

            public static FcObjectSetHandle Create(params string[] objs)
            {
                var os = FcObjectSetCreate();
                foreach (var obj in objs)
                    FcObjectSetAdd(os, obj);
                FcObjectSetAdd(os, "");
                return os;
            }
        }


        [LibraryImport(libfontconfig, EntryPoint = "FcFontList")] private static partial IntPtr FcFontList_Raw(IntPtr config, FcPatternHandle pattern, FcObjectSetHandle os);
        public static FcFontSetHandle FcFontList(IntPtr config, FcPatternHandle pattern, FcObjectSetHandle os) => FcFontSetHandle.FromRaw(FcFontList_Raw(config, pattern, os));

        [LibraryImport(libfontconfig)] public static partial void FcFontSetDestroy(IntPtr fs);

        // Generic-family alias resolution (the managed equivalent of the `fc-match serif` CLI command):
        // build a pattern requesting the generic family name, let fontconfig apply the user/system config's
        // own substitution rules (exactly what maps "serif"/"sans-serif"/etc. to a real installed family on
        // this distro), then read back the resolved family from the matched pattern.
        [LibraryImport(libfontconfig, StringMarshalling = StringMarshalling.Utf8)] private static partial int FcPatternAddString(FcPatternHandle pattern, string obj, string value);
        [LibraryImport(libfontconfig)] private static partial void FcConfigSubstitute(IntPtr config, FcPatternHandle pattern, int kind);
        [LibraryImport(libfontconfig)] private static partial void FcDefaultSubstitute(FcPatternHandle pattern);

        [LibraryImport(libfontconfig, EntryPoint = "FcFontMatch")] private static partial IntPtr FcFontMatch_Raw(IntPtr config, FcPatternHandle pattern, ref int result);
        private static FcPatternHandle FcFontMatch(IntPtr config, FcPatternHandle pattern, ref int result) => FcPatternHandle.FromRaw(FcFontMatch_Raw(config, pattern, ref result));

        private const int FcMatchPattern = 0;

        internal struct FcFontSet
        {
            public int nfont;
            public int sfont;
            public IntPtr fonts;
        }

        internal class FcFontSetHandle : SafeHandle
        {
            FcFontSetHandle() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid => this.handle == IntPtr.Zero;

            protected override bool ReleaseHandle()
            {
                FcFontSetDestroy(this.handle);
                return true;
            }

            internal static FcFontSetHandle FromRaw(IntPtr raw)
            {
                var result = new FcFontSetHandle();
                result.SetHandle(raw);
                return result;
            }

            public FcFontSet Read()
            {
                return Marshal.PtrToStructure<FcFontSet>(this.handle);
            }
        }


        static string GetString(IntPtr handle, string obj)
        {
            var ptr = IntPtr.Zero;
            var result = FcPatternGetString(handle, obj, 0, ref ptr);
            if (result == 0)
                return Marshal.PtrToStringUTF8(ptr);
            else
                return null;
        }


        static IEnumerable<string> ResolveFontConfig()
        {
            var config = fcConfig.Value;
            using (var pattern = FcPatternCreate())
            using (var os = FcObjectSetHandle.Create("family", "style", "file"))
            using (var fs = FcFontList(config, pattern, os))
            {
                var fset = fs.Read();
                for (int index = 0; index < fset.nfont; index++)
                {
                    var font = Marshal.ReadIntPtr(fset.fonts, index * Marshal.SizeOf<IntPtr>());
                    var family = GetString(font, "family");
                    var style = GetString(font, "style");
                    var file = GetString(font, "file");

                    if (family is null || style is null || file is null)
                        continue;

                    yield return file;
                }
            }
        }


        /// <summary>
        /// Resolves a CSS generic family name (<c>serif</c>/<c>sans-serif</c>/<c>monospace</c>/
        /// <c>cursive</c>/<c>fantasy</c>) to the real installed family fontconfig maps it to on this
        /// system - the managed equivalent of running <c>fc-match &lt;genericFamily&gt;</c>. This is how
        /// Chromium itself resolves generic families on Linux: it delegates to the platform's own
        /// fontconfig configuration rather than hardcoding one specific family name that would be wrong
        /// for whichever distro doesn't happen to have it (unlike Windows/macOS/Android, where Chromium
        /// does hardcode specific names - see <see cref="Html.Core.Utils.GenericFontFamilyResolver"/>).
        /// Returns null if <c>libfontconfig.so.1</c> isn't available or resolution otherwise fails, so the
        /// caller can fall back to a reasonable hardcoded substitute.
        /// </summary>
        public static string? ResolveGenericFamily(string genericFamily)
        {
            try
            {
                var config = fcConfig.Value;
                using var pattern = FcPatternCreate();
                if (pattern.IsInvalid) return null;

                FcPatternAddString(pattern, "family", genericFamily);
                FcConfigSubstitute(config, pattern, FcMatchPattern);
                FcDefaultSubstitute(pattern);

                var result = 0;
                using var matched = FcFontMatch(config, pattern, ref result);
                if (matched.IsInvalid) return null;

                return GetString(matched.DangerousGetHandle(), "family");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return null;
            }
        }

        internal static bool IsSupportedFontFile(string path) =>
            path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);

        public static string[] Resolve()
        {
            try
            {
                return ResolveFontConfig().Where(IsSupportedFontFile).ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return ResolveFallback().Where(IsSupportedFontFile).ToArray();
            }
        }


        static IEnumerable<string> ResolveFallback()
        {
            var fontList = new List<string>();

            void AddFontsToFontList(string path)
            {
                if (!Directory.Exists(path))
                    return;

                foreach (string subDir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    fontList.AddRange(Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories));
            }

            var hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in SearchPaths())
            {
                if (hs.Contains(path))
                    continue;
                hs.Add(path);
                AddFontsToFontList(path);
            }

            return fontList.ToArray();
        }

        static IEnumerable<string> SearchPaths()
        {
            var dirs = new List<string>();
            try
            {
                Regex confRegex = ConfDirRegex();
                using (var reader = new StreamReader(File.OpenRead("/etc/fonts/fonts.conf")))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Match match = confRegex.Match(line);
                        if (!match.Success)
                            continue;

                        string path = match.Groups["dir"].Value.Trim();
                        if (path.StartsWith("~"))
                        {
                            path = Environment.GetEnvironmentVariable("HOME") + path.Substring(1);
                        }

                        dirs.Add(path);
                    } // Whend 
                } // End Using reader 
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
            }

            dirs.Add("/usr/share/fonts");
            dirs.Add("/usr/local/share/fonts");
            dirs.Add(Environment.GetEnvironmentVariable("HOME") + "/.fonts");
            return dirs;
        }
    }
}
