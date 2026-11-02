using Microsoft.UI.Input;
using System;
using System.Runtime.InteropServices;

namespace PeachBrackets.WinUI;

// WinUI's own pointers are the system shapes, but the framework can wrap any native cursor: the InputCursor factory also implements
// IInputCursorStaticsInterop (Microsoft.UI.Input.InputCursor.Interop.h), whose CreateFromHCursor makes an InputCursor from an HCURSOR. So a pointer made
// from a document's image is a Win32 HCURSOR - built from the decoded pixels - wrapped in an InputCursor and assigned to ProtectedCursor like any other.
#pragma warning disable SYSLIB1054 // the marshalling here is blittable by construction; DllImport keeps the file free of generated code
internal static class Win32Cursors
{
    private static readonly Guid s_interopId = new("ac6f5065-90c4-46ce-beb7-05e138e54117");

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorUsed;
        public uint ColorImportant;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateFromHCursorMethod(IntPtr self, IntPtr cursor, out IntPtr result);

    [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = true)] private static extern int RoGetActivationFactory(IntPtr className, ref Guid iid, out IntPtr factory);
    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", PreserveSig = true, CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);
    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", PreserveSig = true)] private static extern int WindowsDeleteString(IntPtr hstring);

    /// <summary>Makes an HCURSOR from premultiplied BGRA pixels (top row first); release it with <see cref="Destroy"/>. Zero when it could not be made.</summary>
    /// <param name="bgra">width x height x 4 bytes</param>
    /// <param name="width">the width in pixels</param>
    /// <param name="height">the height in pixels</param>
    /// <param name="hotspotX">the pointer's active point from the left, in pixels</param>
    /// <param name="hotspotY">the active point from the top, in pixels</param>
    public static IntPtr Create(ReadOnlySpan<byte> bgra, int width, int height, int hotspotX, int hotspotY)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            return IntPtr.Zero;

        var header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var screen = GetDC(IntPtr.Zero);
        var color = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        if (color == IntPtr.Zero || bits == IntPtr.Zero)
            return IntPtr.Zero;

        Marshal.Copy(bgra[..(width * height * 4)].ToArray(), 0, bits, width * height * 4);

        // A cursor with an alpha channel still needs a mask bitmap; with alpha present it is not read, so it is all zeros.
        var mask = CreateBitmap(width, height, 1, 1, IntPtr.Zero);
        var info = new IconInfo { IsIcon = false, HotspotX = (uint)Math.Clamp(hotspotX, 0, width - 1), HotspotY = (uint)Math.Clamp(hotspotY, 0, height - 1), Mask = mask, Color = color };
        var cursor = CreateIconIndirect(ref info);
        DeleteObject(color);
        if (mask != IntPtr.Zero)
            DeleteObject(mask);

        return cursor;
    }

    /// <summary>Releases a cursor made by <see cref="Create"/>. The <see cref="InputCursor"/> made from it must not be used afterwards.</summary>
    public static void Destroy(IntPtr cursor)
    {
        if (cursor != IntPtr.Zero)
            DestroyIcon(cursor);
    }

    /// <summary>Wraps a native cursor as an <see cref="InputCursor"/> a view can use as its <c>ProtectedCursor</c>; <see langword="null"/> when the framework refuses.</summary>
    public static InputCursor? ToInputCursor(IntPtr cursor)
    {
        if (cursor == IntPtr.Zero)
            return null;

        IntPtr name = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        IntPtr abi = IntPtr.Zero;
        try
        {
            if (WindowsCreateString("Microsoft.UI.Input.InputCursor", "Microsoft.UI.Input.InputCursor".Length, out name) < 0)
                return null;

            var id = s_interopId;
            if (RoGetActivationFactory(name, ref id, out factory) < 0 || factory == IntPtr.Zero)
                return null;

            // IUnknown (3 methods) and IInspectable (3 more) come first: CreateFromHCursor is the seventh slot of the table.
            var table = Marshal.ReadIntPtr(factory);
            var method = Marshal.GetDelegateForFunctionPointer<CreateFromHCursorMethod>(Marshal.ReadIntPtr(table, 6 * IntPtr.Size));
            if (method(factory, cursor, out abi) < 0 || abi == IntPtr.Zero)
                return null;

            return WinRT.MarshalInspectable<InputCursor>.FromAbi(abi);
        }
        finally
        {
            if (abi != IntPtr.Zero)
                Marshal.Release(abi);

            if (factory != IntPtr.Zero)
                Marshal.Release(factory);

            if (name != IntPtr.Zero)
                WindowsDeleteString(name);
        }
    }
}
