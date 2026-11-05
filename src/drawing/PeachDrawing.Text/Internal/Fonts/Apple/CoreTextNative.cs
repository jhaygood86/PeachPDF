using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeachDrawing.Text.Internal.Fonts.Apple
{
    /// <summary>
    /// The CoreFoundation and CoreText entry points the Apple system font source needs. Source-generated
    /// <c>LibraryImport</c> only (trim- and AOT-safe, no reflection, no <c>Marshal.PtrToStructure</c>). Every
    /// reference CoreFoundation hands back from a <c>Copy</c> or <c>Create</c> call is owned by the caller and
    /// must be released with <see cref="CFRelease"/>; <c>Get</c> calls return borrowed references.
    /// </summary>
    /// <remarks>
    /// Excluded from code coverage like the other platform-specific native lookups
    /// (<c>LinuxSystemFontResolver</c>, <c>MimeTypeResolver.NativeMethods</c>): the lines only run on an Apple
    /// OS, and the managed logic around them is covered by tests that run everywhere.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("maccatalyst")]
    [SupportedOSPlatform("tvos")]
    internal static unsafe partial class CoreTextNative
    {
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";

        internal const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

        /// <summary><c>kCTFontUIFontSystem</c>: the regular system user interface font.</summary>
        internal const int UiFontSystem = 2;

        // ---- CoreFoundation ----

        [LibraryImport(CoreFoundation)]
        internal static partial void CFRelease(nint cf);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFGetTypeID(nint cf);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFStringGetTypeID();

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFURLGetTypeID();

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFArrayGetCount(nint array);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFStringCreateWithCString(nint allocator, byte* cString, uint encoding);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFStringGetLength(nint theString);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

        [LibraryImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static partial bool CFStringGetCString(nint theString, byte* buffer, nint bufferSize, uint encoding);

        [LibraryImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static partial bool CFURLGetFileSystemRepresentation(nint url, [MarshalAs(UnmanagedType.U1)] bool resolveAgainstBase, byte* buffer, nint maxBufferLength);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

        [LibraryImport(CoreFoundation)]
        internal static partial nint CFDataGetLength(nint data);

        [LibraryImport(CoreFoundation)]
        internal static partial byte* CFDataGetBytePtr(nint data);

        // ---- CoreText ----

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCollectionCreateFromAvailableFonts(nint options);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCollectionCreateMatchingFontDescriptors(nint collection);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontDescriptorCopyAttribute(nint descriptor, nint attribute);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCreateWithFontDescriptor(nint descriptor, double size, nint matrix);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCopyFontDescriptor(nint font);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCopyAttribute(nint font, nint attribute);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontDescriptorCreateCopyWithAttributes(nint original, nint attributes);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCreateUIFontForLanguage(int uiType, double size, nint language);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCopyPostScriptName(nint font);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCopyFamilyName(nint font);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCopyAvailableTables(nint font, uint options);

        [LibraryImport(CoreText)]
        internal static partial nint CTFontCopyTable(nint font, uint table, uint options);

        // ---- Constants (exported CFStringRef variables) ----

        private static nint _coreTextHandle;
        private static nint _coreFoundationHandle;

        /// <summary>The address of an exported CoreFoundation variable itself (not the value in it), such as <c>kCFTypeDictionaryKeyCallBacks</c>; zero when it is not exported.</summary>
        internal static nint CoreFoundationExport(string symbol)
        {
            if (_coreFoundationHandle == 0 && !NativeLibrary.TryLoad(CoreFoundation, out _coreFoundationHandle))
                return 0;

            return NativeLibrary.TryGetExport(_coreFoundationHandle, symbol, out var address) ? address : 0;
        }

        /// <summary>Reads an exported <c>CFStringRef</c> constant such as <c>kCTFontURLAttribute</c>; zero when it is not exported.</summary>
        internal static nint ReadConstant(string symbol)
        {
            if (_coreTextHandle == 0 && !NativeLibrary.TryLoad(CoreText, out _coreTextHandle))
                return 0;

            return NativeLibrary.TryGetExport(_coreTextHandle, symbol, out var address) ? Marshal.ReadIntPtr(address) : 0;
        }

        // ---- Helpers over the raw calls ----

        /// <summary>A new <c>CFString</c> for <paramref name="value"/>; the caller releases it.</summary>
        internal static nint CreateString(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value + '\0');
            fixed (byte* p = bytes)
                return CFStringCreateWithCString(0, p, Utf8);
        }

        /// <summary>The managed text of a <c>CFString</c>, or <see langword="null"/> when the reference is zero or not a string.</summary>
        internal static string? ReadString(nint cfString)
        {
            if (cfString == 0 || CFGetTypeID(cfString) != CFStringGetTypeID())
                return null;

            var length = CFStringGetLength(cfString);
            var capacity = CFStringGetMaximumSizeForEncoding(length, Utf8) + 1;
            var buffer = new byte[(int)capacity];
            fixed (byte* p = buffer)
            {
                if (!CFStringGetCString(cfString, p, capacity, Utf8))
                    return null;

                return Marshal.PtrToStringUTF8((nint)p);
            }
        }

        /// <summary>The POSIX path of a <c>CFURL</c>, or <see langword="null"/> when the reference is zero or not a file URL.</summary>
        internal static string? ReadFilePath(nint cfUrl)
        {
            if (cfUrl == 0 || CFGetTypeID(cfUrl) != CFURLGetTypeID())
                return null;

            const int Capacity = 4096;
            var buffer = new byte[Capacity];
            fixed (byte* p = buffer)
            {
                if (!CFURLGetFileSystemRepresentation(cfUrl, true, p, Capacity))
                    return null;

                return Marshal.PtrToStringUTF8((nint)p);
            }
        }

        /// <summary>A new <c>CFDictionary</c> of one key and value that are themselves CoreFoundation objects; the caller releases it. Zero when CoreFoundation does not export the callback tables.</summary>
        internal static nint CreateDictionary(nint key, nint value)
        {
            var keyCallBacks = CoreFoundationExport("kCFTypeDictionaryKeyCallBacks");
            var valueCallBacks = CoreFoundationExport("kCFTypeDictionaryValueCallBacks");
            if (key == 0 || value == 0 || keyCallBacks == 0 || valueCallBacks == 0)
                return 0;

            return CFDictionaryCreate(0, &key, &value, 1, keyCallBacks, valueCallBacks);
        }

        /// <summary>A copy of the bytes in a <c>CFData</c>; empty for a zero reference.</summary>
        internal static byte[] ReadData(nint cfData)
        {
            if (cfData == 0)
                return [];

            var length = (int)CFDataGetLength(cfData);
            var bytes = new byte[length];
            if (length > 0)
                new ReadOnlySpan<byte>(CFDataGetBytePtr(cfData), length).CopyTo(bytes);
            return bytes;
        }
    }
}
