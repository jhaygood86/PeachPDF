using System.Runtime.InteropServices;
using Android.Hardware;
using Android.Runtime;
using SkiaSharp;

namespace PeachBrackets.Media.Android.Internal;

/// <summary>
/// Turns a decoder's <see cref="HardwareBuffer"/> into a Skia image without copying it: the buffer becomes an EGL image, the EGL image a
/// <c>GL_TEXTURE_EXTERNAL_OES</c> texture in the context the host draws with, and that texture an <see cref="SKImage"/>. It must run on the thread whose
/// GL context the <see cref="GRContext"/> belongs to (the host's render thread, which is where a canvas resolves the picture it draws).
/// </summary>
internal static unsafe partial class HardwareBufferImporter
{
    private const uint ExternalTexture = 0x8D65;      // GL_TEXTURE_EXTERNAL_OES
    private const uint Rgba8 = 0x8058;                // GL_RGBA8
    private const int NativeBuffer = 0x3140;          // EGL_NATIVE_BUFFER_ANDROID
    private const int ImagePreserved = 0x30D2;        // EGL_IMAGE_PRESERVED_KHR
    private const int None = 0x3038;                  // EGL_NONE

    [LibraryImport("libandroid.so")]
    private static partial nint AHardwareBuffer_fromHardwareBuffer(nint env, nint hardwareBuffer);

    [LibraryImport("libEGL.so")]
    private static partial nint eglGetCurrentDisplay();

    [LibraryImport("libEGL.so")]
    private static partial nint eglGetCurrentContext();

    [LibraryImport("libEGL.so")]
    private static partial nint eglGetNativeClientBufferANDROID(nint buffer);

    [LibraryImport("libEGL.so")]
    private static partial nint eglCreateImageKHR(nint display, nint context, int target, nint buffer, int* attributes);

    [LibraryImport("libEGL.so")]
    private static partial int eglDestroyImageKHR(nint display, nint image);

    [LibraryImport("libEGL.so", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint eglGetProcAddress(string name);

    [LibraryImport("libGLESv2.so")]
    private static partial void glGenTextures(int count, uint* textures);

    [LibraryImport("libGLESv2.so")]
    private static partial void glBindTexture(uint target, uint texture);

    [LibraryImport("libGLESv2.so")]
    private static partial void glDeleteTextures(int count, uint* textures);

    private static nint s_imageTarget;

    /// <summary>Whether the device's EGL can wrap a hardware buffer (the extension, and the entry points it needs).</summary>
    public static bool IsSupported() => OperatingSystem.IsAndroidVersionAtLeast(29) && ImageTarget() != 0;

    private static nint ImageTarget() => s_imageTarget != 0 ? s_imageTarget : (s_imageTarget = eglGetProcAddress("glEGLImageTargetTexture2DOES"));

    /// <summary>
    /// The image of <paramref name="buffer"/>, or null when the device cannot wrap it. The texture and EGL image are freed when the image is released: at once when that
    /// happens on the thread holding the GL context, otherwise on the next import made on it (a GL object cannot be deleted from another thread).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    public static SKImage? Import(GRContext context, HardwareBuffer buffer, int width, int height)
    {
        var current = eglGetCurrentContext();
        DrainFreed(current);
        var target = ImageTarget();
        var display = eglGetCurrentDisplay();
        if (target == 0 || display == 0)
            return null;

        var native = AHardwareBuffer_fromHardwareBuffer(JNIEnv.Handle, buffer.Handle);
        if (native == 0)
            return null;

        var client = eglGetNativeClientBufferANDROID(native);
        if (client == 0)
            return null;

        var attributes = stackalloc int[] { ImagePreserved, 1, None };
        var eglImage = eglCreateImageKHR(display, 0, NativeBuffer, client, attributes);
        if (eglImage == 0)
            return null;

        uint generated;
        glGenTextures(1, &generated);
        var texture = generated;
        glBindTexture(ExternalTexture, texture);
        ((delegate* unmanaged<uint, nint, void>)target)(ExternalTexture, eglImage);
        glBindTexture(ExternalTexture, 0);

        // The buffer is usually larger than the picture (a decoder aligns its width and height), and a texture is sampled across its whole size, so the texture is
        // made at the buffer's size and the picture is the part of it that shows.
        var bufferWidth = buffer.Width;
        var bufferHeight = buffer.Height;
        using var backend = new GRBackendTexture(bufferWidth, bufferHeight, false, new GRGlTextureInfo(ExternalTexture, texture, Rgba8));
        var whole = SKImage.FromTexture(context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888, SKAlphaType.Premul, null,
            _ => Free(current, display, eglImage, texture), null);
        if (whole is null)
        {
            Free(current, display, eglImage, texture);
            return null;
        }

        if (bufferWidth == width && bufferHeight == height)
            return whole;

        using (whole)
            return whole.Subset(context, new SKRectI(0, 0, width, height));
    }

    // What a release on another thread left for the thread that owns the context.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<(nint Context, nint Display, nint EglImage, uint Texture)> s_freed = new();

    private static void Free(nint context, nint display, nint eglImage, uint texture)
    {
        if (eglGetCurrentContext() == context)
        {
            Destroy(display, eglImage, texture);
            return;
        }

        s_freed.Enqueue((context, display, eglImage, texture));
    }

    private static void DrainFreed(nint current)
    {
        for (var n = s_freed.Count; n > 0 && s_freed.TryDequeue(out var item); n--)
        {
            if (item.Context == current)
                Destroy(item.Display, item.EglImage, item.Texture);
            else
                s_freed.Enqueue(item);
        }
    }

    private static void Destroy(nint display, nint eglImage, uint texture)
    {
        glDeleteTextures(1, &texture);
        eglDestroyImageKHR(display, eglImage);
    }
}
