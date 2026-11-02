using Microsoft.Graphics.Canvas;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PeachDrawing.Win2D;

/// <summary>
/// The Direct2D bitmap a <see cref="CanvasDrawingSession"/> draws into, reached through the native interface Win2D gives every session, so
/// pixels already drawn can be read back. A session over a bitmap of any kind (a render target, a control's drawing surface) has one; a
/// session recording into a command list has none, and nothing here is made for it.
/// </summary>
/// <remarks>
/// <para>
/// Win2D wraps an <c>ID2D1DeviceContext</c> and answers <c>ICanvasResourceWrapperNative</c> for it; the context's target is an
/// <c>ID2D1Bitmap1</c>, which is copied into a CPU-readable staging bitmap of the same format. Every call is a slot of a Direct2D 1.1
/// interface, whose layout is fixed by the ABI, and anything unexpected (an interface that is missing, a format that is not 32-bit BGRA, a
/// failed copy) makes the reader unusable rather than throwing: the caller draws without it.
/// </para>
/// <para>
/// <b>Ownership.</b> The reader holds one reference to each COM object it touches and releases all of them in <see cref="Dispose"/>, which
/// must be called before the session ends. It never closes or draws on the session.
/// </para>
/// </remarks>
internal sealed unsafe class Win2dNativeTarget : IDisposable
{
    private static readonly Guid WrapperNativeId = new("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");
    private static readonly Guid DeviceContextId = new("E8F7FE7A-191C-466D-AD95-975678BDA998");
    private static readonly Guid Bitmap1Id = new("A898A84C-3873-4588-B08B-EBBF978DF041");

    private const uint FormatB8G8R8A8 = 87;
    private const uint OptionsCpuReadCannotDraw = 2 | 4;

    // Slots of the interfaces called: IUnknown's three, then each base interface's methods in declaration order.
    private const int DeviceContextGetTransform = 31;
    private const int DeviceContextCreateBitmap = 57;
    private const int DeviceContextGetTarget = 75;
    private const int BitmapGetPixelSize = 5;
    private const int BitmapGetPixelFormat = 6;
    private const int BitmapCopyFromBitmap = 8;
    private const int Bitmap1Map = 14;
    private const int Bitmap1Unmap = 15;

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeU { public uint Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectU { public uint Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointU { public uint X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat { public uint Format, AlphaMode; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapProperties1 { public uint Format, AlphaMode; public float DpiX, DpiY; public uint Options; public IntPtr ColorContext; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedRect { public uint Pitch; public IntPtr Bits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix3x2F { public float M11, M12, M21, M22, Dx, Dy; }

    private readonly CanvasDrawingSession _session;
    private readonly Win2dResourceCounts _counts;
    private IntPtr _context;
    private IntPtr _bitmap;
    private IntPtr _staging;
    private PixelFormat _format;
    private uint _stagingWidth;
    private uint _stagingHeight;
    private bool _disposed;

    private Win2dNativeTarget(CanvasDrawingSession session, Win2dResourceCounts counts)
    {
        _session = session;
        _counts = counts;
    }

    /// <summary>The width of the target, in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>The height of the target, in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Whether the target has no alpha channel (every pixel is opaque), which is what blending against it needs.</summary>
    public bool IsOpaque { get; private set; }

    /// <summary>Makes a reader for <paramref name="session"/>'s target, or <see langword="null"/> when it has none that can be read.</summary>
    public static Win2dNativeTarget? TryCreate(CanvasDrawingSession session, Win2dResourceCounts counts)
    {
        var reader = new Win2dNativeTarget(session, counts);
        if (!reader.Open())
        {
            reader.Dispose();
            return null;
        }

        counts.NativeTarget(1);
        return reader;
    }

    private bool Open()
    {
        IntPtr sessionPtr = IntPtr.Zero, devicePtr = IntPtr.Zero, wrapper = IntPtr.Zero, target = IntPtr.Zero;
        try
        {
            if (_session.Device is not CanvasDevice device)
                return false;

            sessionPtr = WinRT.MarshalInspectable<CanvasDrawingSession>.FromManaged(_session);
            devicePtr = WinRT.MarshalInspectable<CanvasDevice>.FromManaged(device);
            if (sessionPtr == IntPtr.Zero || devicePtr == IntPtr.Zero)
                return false;

            var wrapperId = WrapperNativeId;
            if (Marshal.QueryInterface(sessionPtr, in wrapperId, out wrapper) < 0)
                return false;

            // ICanvasResourceWrapperNative::GetNativeResource is the first method after IUnknown's.
            var contextId = DeviceContextId;
            IntPtr context;
            var getNative = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, float, Guid*, IntPtr*, int>)Slot(wrapper, 3);
            if (getNative(wrapper, devicePtr, 0f, &contextId, &context) < 0 || context == IntPtr.Zero)
                return false;

            _context = context;

            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(_context, DeviceContextGetTarget))(_context, &target);
            if (target == IntPtr.Zero)
                return false;

            var bitmapId = Bitmap1Id;
            if (Marshal.QueryInterface(target, in bitmapId, out _bitmap) < 0)
                return false;

            PixelFormat format;
            SizeU size;
            ((delegate* unmanaged[Stdcall]<IntPtr, PixelFormat*, IntPtr>)Slot(_bitmap, BitmapGetPixelFormat))(_bitmap, &format);
            ((delegate* unmanaged[Stdcall]<IntPtr, SizeU*, IntPtr>)Slot(_bitmap, BitmapGetPixelSize))(_bitmap, &size);
            if (format.Format != FormatB8G8R8A8 || size.Width == 0 || size.Height == 0)
                return false;

            Width = (int)size.Width;
            Height = (int)size.Height;
            _format = format;

            // DXGI alpha mode 3 is "ignore": the target has no alpha, so every pixel is opaque.
            IsOpaque = format.AlphaMode == 3;
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or NullReferenceException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            Release(ref target);
            Release(ref wrapper);
            Release(ref devicePtr);
            Release(ref sessionPtr);
        }
    }

    /// <summary>
    /// Where the session's drawing space starts in the target, in pixels: a control that draws one region of a shared surface has the region at
    /// an offset into it. Read with the session's own transform set aside, because the offset is part of the transform Direct2D holds.
    /// </summary>
    public (double X, double Y) Origin()
    {
        var saved = _session.Transform;
        _session.Transform = Matrix3x2.Identity;
        try
        {
            Matrix3x2F m;
            ((delegate* unmanaged[Stdcall]<IntPtr, Matrix3x2F*, void>)Slot(_context, DeviceContextGetTransform))(_context, &m);
            var scale = _session.Dpi / 96.0;
            return (m.Dx * scale, m.Dy * scale);
        }
        finally
        {
            _session.Transform = saved;
        }
    }

    /// <summary>
    /// Reads the pixels of a rectangle of the target, in the target's own pixels, as BGRA with no padding between rows, into
    /// <paramref name="pixels"/>. False when the rectangle is not inside the target or the copy fails.
    /// </summary>
    public bool TryRead(int x, int y, int width, int height, byte[] pixels)
    {
        if (_disposed || width <= 0 || height <= 0 || x < 0 || y < 0 || x + width > Width || y + height > Height
            || pixels.Length < width * height * 4)
        {
            return false;
        }

        try
        {
            // Drawing is asynchronous: what has been issued is not in the bitmap until it is flushed.
            _session.Flush();

            if (!EnsureStaging((uint)width, (uint)height))
                return false;

            var destination = new PointU();
            var source = new RectU { Left = (uint)x, Top = (uint)y, Right = (uint)(x + width), Bottom = (uint)(y + height) };
            var copy = (delegate* unmanaged[Stdcall]<IntPtr, PointU*, IntPtr, RectU*, int>)Slot(_staging, BitmapCopyFromBitmap);
            if (copy(_staging, &destination, _bitmap, &source) < 0)
                return false;

            MappedRect mapped;
            var map = (delegate* unmanaged[Stdcall]<IntPtr, uint, MappedRect*, int>)Slot(_staging, Bitmap1Map);
            if (map(_staging, 1, &mapped) < 0)
                return false;

            try
            {
                for (var row = 0; row < height; row++)
                    Marshal.Copy(mapped.Bits + (row * (int)mapped.Pitch), pixels, row * width * 4, width * 4);
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(_staging, Bitmap1Unmap))(_staging);
            }

            return true;
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException)
        {
            return false;
        }
    }

    // A CPU-readable bitmap at least as big as the rectangle asked for; kept and reused, and replaced by a larger one when needed.
    private bool EnsureStaging(uint width, uint height)
    {
        if (_staging != IntPtr.Zero && _stagingWidth >= width && _stagingHeight >= height)
            return true;

        Release(ref _staging);
        var wanted = new SizeU { Width = Math.Max(width, 64), Height = Math.Max(height, 32) };
        var properties = new BitmapProperties1 { Format = _format.Format, AlphaMode = _format.AlphaMode, DpiX = 96, DpiY = 96, Options = OptionsCpuReadCannotDraw };
        IntPtr bitmap;
        var create = (delegate* unmanaged[Stdcall]<IntPtr, SizeU, IntPtr, uint, BitmapProperties1*, IntPtr*, int>)Slot(_context, DeviceContextCreateBitmap);
        if (create(_context, wanted, IntPtr.Zero, 0, &properties, &bitmap) < 0 || bitmap == IntPtr.Zero)
            return false;

        _staging = bitmap;
        _stagingWidth = wanted.Width;
        _stagingHeight = wanted.Height;
        return true;
    }

    private static IntPtr Slot(IntPtr instance, int index) => *(IntPtr*)(*(IntPtr*)instance + (index * IntPtr.Size));

    private static void Release(ref IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return;

        Marshal.Release(pointer);
        pointer = IntPtr.Zero;
    }

    /// <summary>Releases every reference the reader holds.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Release(ref _staging);
        Release(ref _bitmap);
        Release(ref _context);

        // Only a reader that opened counted itself (see TryCreate).
        if (Width > 0)
            _counts.NativeTarget(-1);
    }
}
