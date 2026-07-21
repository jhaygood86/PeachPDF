#nullable enable

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PeachPDF.Network
{
    /// <summary>
    /// Resolves a file's MIME type from its extension, used to populate the synthesized
    /// <c>Content-Type</c> header on <see cref="FileUriNetworkLoader"/> responses so the rest of the
    /// pipeline (the stylesheet loader's <c>text/css</c> gate, the image loader's
    /// <c>image/svg+xml</c> detection) treats a local file exactly like a fetched network resource.
    /// </summary>
    /// <remarks>
    /// An OS-provided mechanism is consulted first — the Windows shell association API, the Apple
    /// Uniform Type Identifiers C API, or the Linux <c>/etc/mime.types</c> database — falling back to a
    /// built-in static map covering HTML, CSS, SVG and the raster formats StbImageSharp decodes. Every
    /// OS lookup is best-effort and fail-soft: any failure (missing library on a platform, an
    /// unregistered extension, an interop error) falls through to the static map, so the formats PeachPDF
    /// itself renders resolve even on a host whose OS registrations are missing. Results are memoized per
    /// extension for the process lifetime, so the (potentially syscall- or file-backed) OS lookup runs at
    /// most once per distinct extension.
    /// </remarks>
    internal static class MimeTypeResolver
    {
        private const string DefaultMimeType = "application/octet-stream";

        // The extension -> MIME mapping is stable for a process's lifetime, so memoize it: a document with
        // many images of the same type then costs one OS lookup, not one per resource. Keyed by the
        // lowercase dot-less extension.
        private static readonly ConcurrentDictionary<string, string> Cache = new();

        /// <summary>
        /// Resolves the MIME type for <paramref name="pathOrFileName"/> from its extension. Returns
        /// <c>application/octet-stream</c> when nothing else matches (or there is no extension).
        /// </summary>
        public static string GetMimeType(string pathOrFileName)
        {
            var extension = Path.GetExtension(pathOrFileName);

            if (string.IsNullOrEmpty(extension))
            {
                return DefaultMimeType;
            }

            // Path.GetExtension keeps the leading dot and never returns an all-dots string (it returns ""
            // for those), so trimming the dot always yields a non-empty key here.
            var extensionNoDot = extension.TrimStart('.').ToLowerInvariant();

            return Cache.GetOrAdd(extensionNoDot, Resolve);
        }

        private static string Resolve(string extensionNoDot)
        {
            // OS-provided mechanism first (per the "OS by default" requirement), static map as the
            // fallback for the formats PeachPDF renders. Each OS helper already validates its result
            // (see LooksLikeMimeType) and returns null for a non-MIME answer, so a host with no real
            // registration for the extension falls through to the static map / octet-stream rather than
            // leaking a bogus value into the synthesized Content-Type.
            return TryGetFromOperatingSystem("." + extensionNoDot, extensionNoDot)
                   ?? TryGetFromStaticMap(extensionNoDot)
                   ?? DefaultMimeType;
        }

        /// <summary>
        /// Whether <paramref name="value"/> looks like a real <c>type/subtype</c> MIME type, used to reject
        /// non-MIME strings some OS association APIs return for an extension that has no registered content
        /// type — e.g. Windows' shell returns a property descriptor like
        /// <c>prop:System.ItemTypeText;System.Size;…</c>, which must not become a <c>Content-Type</c> header
        /// (it isn't a valid MIME type and would throw when parsed). Exposed <c>internal</c> for direct testing.
        /// </summary>
        internal static bool LooksLikeMimeType(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value.IndexOf('/') > 0
            && !value.StartsWith("prop:", StringComparison.OrdinalIgnoreCase);

        // Each OS-native lookup below only executes on its own platform, so on any single CI leg the other
        // platforms' branches are unreachable - they are verified by the platform-gated MimeTypeResolverTests
        // (which, by design, run only on their own OS and don't contribute to cross-platform coverage), so
        // they're excluded from the coverage metric rather than dragging the per-leg diff-coverage gate down.
        [ExcludeFromCodeCoverage]
        private static string? TryGetFromOperatingSystem(string extensionWithDot, string extensionNoDot)
        {
            if (OperatingSystem.IsWindows())
            {
                return TryGetFromWindows(extensionWithDot);
            }

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
            {
                return TryGetFromApple(extensionNoDot);
            }

            if (OperatingSystem.IsLinux())
            {
                return TryGetFromLinux(extensionNoDot);
            }

            return null;
        }

        /// <summary>
        /// The built-in fallback map. Covers the document formats PeachPDF parses (HTML/CSS/SVG), every
        /// raster format <c>StbImageSharpImageSource</c> decodes, and the <c>@font-face</c> font formats
        /// PeachPDF loads (TTF/OTF/WOFF/WOFF2), so these always resolve regardless of the host OS's own
        /// registrations. Exposed <c>internal</c> for direct testing.
        /// </summary>
        internal static string? TryGetFromStaticMap(string extensionNoDot) => extensionNoDot switch
        {
            "html" or "htm" => "text/html",
            "css" => "text/css",
            "svg" => "image/svg+xml",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "bmp" => "image/bmp",
            "gif" => "image/gif",
            "tga" => "image/x-tga",
            "psd" => "image/vnd.adobe.photoshop",
            "hdr" => "image/vnd.radiance",
            // RFC 8081 font MIME types, matching the formats PdfSharpAdapter.AddFontFamilyFromUrl accepts.
            "ttf" => "font/ttf",
            "otf" => "font/otf",
            "woff" => "font/woff",
            "woff2" => "font/woff2",
            _ => null
        };

        /// <summary>
        /// Windows: queries the shell file-association database via <c>AssocQueryString</c>
        /// (<c>ASSOCSTR_CONTENTTYPE</c>) — the documented association API, rather than reading
        /// <c>HKEY_CLASSES_ROOT</c> directly (no <c>Microsoft.Win32.Registry</c> dependency).
        /// <paramref name="extensionWithDot"/> must include the leading dot (e.g. <c>.png</c>).
        /// Exposed <c>internal</c> so a Windows-only test can exercise it directly.
        /// </summary>
        [SupportedOSPlatform("windows")]
        [ExcludeFromCodeCoverage]
        internal static string? TryGetFromWindows(string extensionWithDot)
        {
            try
            {
                uint length = 0;

                // First call sizes the output buffer (pcchOut). A non-zero length means an association
                // exists; anything else (no association, error) falls through to the static map.
                _ = NativeMethods.AssocQueryStringW(NativeMethods.AssocfNone, NativeMethods.AssocStrContentType,
                    extensionWithDot, null, null, ref length);

                if (length == 0)
                {
                    return null;
                }

                var buffer = new StringBuilder((int)length);
                var hr = NativeMethods.AssocQueryStringW(NativeMethods.AssocfNone, NativeMethods.AssocStrContentType,
                    extensionWithDot, null, buffer, ref length);

                if (hr != 0) // S_OK == 0
                {
                    return null;
                }

                var contentType = buffer.ToString();

                // A bare Windows install often has no registered Content Type for an extension, in which
                // case AssocQueryString returns a non-MIME shell property string (prop:System.…) rather
                // than failing - reject anything that isn't a real MIME type.
                return LooksLikeMimeType(contentType) ? contentType : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Apple platforms (macOS, iOS, Mac Catalyst, tvOS): maps the extension to a Uniform Type
        /// Identifier and then to its preferred MIME type via the LaunchServices C API in
        /// <c>CoreServices.framework</c>. The tag-class constants are passed by their documented literal
        /// values (<c>public.filename-extension</c>, <c>public.mime-type</c>) so no framework global has
        /// to be dlsym'd. Exposed <c>internal</c> so an Apple-only test can exercise it directly.
        /// </summary>
        [SupportedOSPlatform("macos")]
        [SupportedOSPlatform("ios")]
        [SupportedOSPlatform("maccatalyst")]
        [SupportedOSPlatform("tvos")]
        [ExcludeFromCodeCoverage]
        internal static string? TryGetFromApple(string extensionNoDot)
        {
            var extensionRef = IntPtr.Zero;
            var tagClassExtensionRef = IntPtr.Zero;
            var tagClassMimeRef = IntPtr.Zero;
            var utiRef = IntPtr.Zero;
            var mimeRef = IntPtr.Zero;

            try
            {
                extensionRef = NativeMethods.CFStringCreateWithCString(IntPtr.Zero, extensionNoDot, NativeMethods.KCFStringEncodingUtf8);
                tagClassExtensionRef = NativeMethods.CFStringCreateWithCString(IntPtr.Zero, "public.filename-extension", NativeMethods.KCFStringEncodingUtf8);
                tagClassMimeRef = NativeMethods.CFStringCreateWithCString(IntPtr.Zero, "public.mime-type", NativeMethods.KCFStringEncodingUtf8);

                if (extensionRef == IntPtr.Zero || tagClassExtensionRef == IntPtr.Zero || tagClassMimeRef == IntPtr.Zero)
                {
                    return null;
                }

                utiRef = NativeMethods.UTTypeCreatePreferredIdentifierForTag(tagClassExtensionRef, extensionRef, IntPtr.Zero);
                if (utiRef == IntPtr.Zero)
                {
                    return null;
                }

                mimeRef = NativeMethods.UTTypeCopyPreferredTagWithClass(utiRef, tagClassMimeRef);
                if (mimeRef == IntPtr.Zero)
                {
                    return null;
                }

                var mime = CFStringToManaged(mimeRef);
                return LooksLikeMimeType(mime) ? mime : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (mimeRef != IntPtr.Zero) NativeMethods.CFRelease(mimeRef);
                if (utiRef != IntPtr.Zero) NativeMethods.CFRelease(utiRef);
                if (tagClassMimeRef != IntPtr.Zero) NativeMethods.CFRelease(tagClassMimeRef);
                if (tagClassExtensionRef != IntPtr.Zero) NativeMethods.CFRelease(tagClassExtensionRef);
                if (extensionRef != IntPtr.Zero) NativeMethods.CFRelease(extensionRef);
            }
        }

        [SupportedOSPlatform("macos")]
        [SupportedOSPlatform("ios")]
        [SupportedOSPlatform("maccatalyst")]
        [SupportedOSPlatform("tvos")]
        [ExcludeFromCodeCoverage]
        private static string? CFStringToManaged(IntPtr cfString)
        {
            // Fast path: a direct pointer to the backing UTF-8 buffer, if the string has one.
            var direct = NativeMethods.CFStringGetCStringPtr(cfString, NativeMethods.KCFStringEncodingUtf8);
            if (direct != IntPtr.Zero)
            {
                return Marshal.PtrToStringUTF8(direct);
            }

            // Slow path: copy into our own buffer, sized from the UTF-16 length (ample for UTF-8 here).
            var length = NativeMethods.CFStringGetLength(cfString);
            var capacity = (int)(length * 3) + 1;
            var buffer = new byte[capacity];

            if (!NativeMethods.CFStringGetCString(cfString, buffer, capacity, NativeMethods.KCFStringEncodingUtf8))
            {
                return null;
            }

            var terminator = Array.IndexOf(buffer, (byte)0);
            var count = terminator >= 0 ? terminator : buffer.Length;
            return Encoding.UTF8.GetString(buffer, 0, count);
        }

        /// <summary>
        /// Linux: looks the extension up in the system <c>/etc/mime.types</c> database (each non-comment
        /// line is a MIME type followed by whitespace-separated extensions). Exposed <c>internal</c> so a
        /// Linux-only test can exercise it directly.
        /// </summary>
        [SupportedOSPlatform("linux")]
        [ExcludeFromCodeCoverage]
        internal static string? TryGetFromLinux(string extensionNoDot)
        {
            const string mimeTypesPath = "/etc/mime.types";

            try
            {
                if (!File.Exists(mimeTypesPath))
                {
                    return null;
                }

                foreach (var rawLine in File.ReadLines(mimeTypesPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    // Each line is "<mime-type> <ext> <ext> ...", whitespace-separated.
                    var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 2)
                    {
                        continue;
                    }

                    for (var i = 1; i < tokens.Length; i++)
                    {
                        if (tokens[i].Equals(extensionNoDot, StringComparison.OrdinalIgnoreCase))
                        {
                            return LooksLikeMimeType(tokens[0]) ? tokens[0] : null;
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// P/Invoke declarations for the OS-native MIME lookups. Trim-safe (no reflection); each entry
        /// point is only ever reached under the matching <see cref="OperatingSystem"/> guard.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private static class NativeMethods
        {
            // ---- Windows: Shlwapi AssocQueryString ----

            internal const int AssocfNone = 0;
            internal const int AssocStrContentType = 12; // ASSOCSTR_CONTENTTYPE

            [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
            internal static extern int AssocQueryStringW(int flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);

            // ---- Apple: CoreFoundation + LaunchServices (CoreServices) ----

            private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

            internal const uint KCFStringEncodingUtf8 = 0x08000100;

            [DllImport(CoreFoundation)]
            internal static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr, uint encoding);

            [DllImport(CoreFoundation)]
            internal static extern void CFRelease(IntPtr cf);

            [DllImport(CoreFoundation)]
            internal static extern IntPtr CFStringGetCStringPtr(IntPtr theString, uint encoding);

            [DllImport(CoreFoundation)]
            [return: MarshalAs(UnmanagedType.I1)]
            internal static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

            [DllImport(CoreFoundation)]
            internal static extern long CFStringGetLength(IntPtr theString);

            [DllImport(CoreServices)]
            internal static extern IntPtr UTTypeCreatePreferredIdentifierForTag(IntPtr inTagClass, IntPtr inTag, IntPtr inConformingToUti);

            [DllImport(CoreServices)]
            internal static extern IntPtr UTTypeCopyPreferredTagWithClass(IntPtr inUti, IntPtr inTagClass);
        }
    }
}
