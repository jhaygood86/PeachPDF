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


        [DllImport(libfontconfig)] private static extern IntPtr FcInitLoadConfigAndFonts();

        static readonly Lazy<IntPtr> fcConfig = new Lazy<IntPtr>(FcInitLoadConfigAndFonts);


        [DllImport(libfontconfig)] public static extern FcPatternHandle FcPatternCreate();
        [DllImport(libfontconfig)] public static extern int FcPatternGetString(IntPtr p, [MarshalAs(UnmanagedType.LPStr)] string obj, int n, ref IntPtr s);
        [DllImport(libfontconfig)] public static extern void FcPatternDestroy(IntPtr pattern);

        internal class FcPatternHandle : SafeHandle
        {
            FcPatternHandle() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid => this.handle == IntPtr.Zero;

            protected override bool ReleaseHandle()
            {
                FcPatternDestroy(this.handle);
                return true;
            }
        }


        [DllImport(libfontconfig)] public static extern FcObjectSetHandle FcObjectSetCreate();
        [DllImport(libfontconfig)] public static extern int FcObjectSetAdd(FcObjectSetHandle os, [MarshalAs(UnmanagedType.LPStr)] string obj);
        [DllImport(libfontconfig)] public static extern void FcObjectSetDestroy(IntPtr os);

        internal class FcObjectSetHandle : SafeHandle
        {
            FcObjectSetHandle() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid => this.handle == IntPtr.Zero;

            protected override bool ReleaseHandle()
            {
                FcObjectSetDestroy(this.handle);
                return true;
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


        [DllImport(libfontconfig)] public static extern FcFontSetHandle FcFontList(IntPtr config, FcPatternHandle pattern, FcObjectSetHandle os);
        [DllImport(libfontconfig)] public static extern void FcFontSetDestroy(IntPtr fs);

        // Generic-family alias resolution (the managed equivalent of the `fc-match serif` CLI command):
        // build a pattern requesting the generic family name, let fontconfig apply the user/system config's
        // own substitution rules (exactly what maps "serif"/"sans-serif"/etc. to a real installed family on
        // this distro), then read back the resolved family from the matched pattern.
        [DllImport(libfontconfig)] private static extern int FcPatternAddString(FcPatternHandle pattern, [MarshalAs(UnmanagedType.LPStr)] string obj, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport(libfontconfig)] private static extern void FcConfigSubstitute(IntPtr config, FcPatternHandle pattern, int kind);
        [DllImport(libfontconfig)] private static extern void FcDefaultSubstitute(FcPatternHandle pattern);
        [DllImport(libfontconfig)] private static extern FcPatternHandle FcFontMatch(IntPtr config, FcPatternHandle pattern, ref int result);

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
                return Marshal.PtrToStringAnsi(ptr);
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
