using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PeachBrackets.Media.GStreamer.Internal;

/// <summary>
/// The few GStreamer, GObject and GLib entry points the provider uses, bound with source-generated <c>LibraryImport</c> (no reflection marshalling, so
/// trimming and ahead-of-time compilation are safe). The libraries are the ones installed on the machine: nothing is bundled.
/// </summary>
internal static unsafe partial class GstNative
{
    public const int StateNull = 1;
    public const int StatePaused = 3;
    public const int StatePlaying = 4;

    public const int ChangeFailure = 0;

    public const int FormatTime = 3;
    public const int SeekFlush = 1;
    public const int SeekAccurate = 2;
    public const int SeekTypeNone = 0;
    public const int SeekTypeSet = 1;

    public const uint MessageEos = 1;
    public const uint MessageError = 2;

    public const long Second = 1_000_000_000;

    private const nuint GTypeBoolean = 5 << 2;
    private const nuint GTypeDouble = 15 << 2;
    private const nuint GTypeObject = 20 << 2;

    private static readonly object s_gate = new();
    private static int s_state; // 0 not tried, 1 ready, 2 unavailable

    /// <summary>Loads and initializes GStreamer once; false when it is not installed or cannot start.</summary>
    public static bool EnsureInitialized()
    {
        if (s_state != 0)
            return s_state == 1;

        lock (s_gate)
        {
            if (s_state != 0)
                return s_state == 1;

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(GstNative).Assembly, Resolve);
                s_state = gst_init_check(0, 0, out var error) != 0 ? 1 : 2;
                if (error != 0)
                    g_error_free(error);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                s_state = 2;
            }

            return s_state == 1;
        }
    }

    private static nint Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        foreach (var candidate in Candidates(name))
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }

        return 0;
    }

    private static IEnumerable<string> Candidates(string name)
    {
        var (linux, windows, mac) = name switch
        {
            "gstreamer" => ("libgstreamer-1.0.so.0", "gstreamer-1.0-0.dll", "libgstreamer-1.0.0.dylib"),
            "gstapp" => ("libgstapp-1.0.so.0", "gstapp-1.0-0.dll", "libgstapp-1.0.0.dylib"),
            "gobject" => ("libgobject-2.0.so.0", "gobject-2.0-0.dll", "libgobject-2.0.0.dylib"),
            "glib" => ("libglib-2.0.so.0", "glib-2.0-0.dll", "libglib-2.0.0.dylib"),
            _ => (name, name, name),
        };

        if (OperatingSystem.IsWindows())
        {
            // The GStreamer installer sets one of these to its root; its DLLs are in bin and are not on the default search path of a .NET process.
            foreach (var variable in new[] { "GSTREAMER_1_0_ROOT_MSVC_X86_64", "GSTREAMER_1_0_ROOT_X86_64", "GSTREAMER_1_0_ROOT_MINGW_X86_64" })
            {
                if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root)
                    yield return Path.Combine(root, "bin", windows);
            }

            yield return windows;
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
        {
            yield return "/Library/Frameworks/GStreamer.framework/Libraries/" + mac;
            yield return "/opt/homebrew/lib/" + mac;
            yield return "/usr/local/lib/" + mac;
            yield return mac;
        }
        else
        {
            yield return linux;
        }
    }

    /// <summary>Sets a <c>double</c> property.</summary>
    public static void SetDouble(nint obj, string property, double value)
    {
        var gvalue = stackalloc byte[32];
        new Span<byte>(gvalue, 32).Clear();
        g_value_init((nint)gvalue, GTypeDouble);
        g_value_set_double((nint)gvalue, value);
        g_object_set_property(obj, property, (nint)gvalue);
        g_value_unset((nint)gvalue);
    }

    /// <summary>Sets an object property (the element is referenced, not consumed: the caller still owns its own reference).</summary>
    public static void SetObject(nint obj, string property, nint value)
    {
        var gvalue = stackalloc byte[32];
        new Span<byte>(gvalue, 32).Clear();
        g_value_init((nint)gvalue, GTypeObject);
        g_value_set_object((nint)gvalue, value);
        g_object_set_property(obj, property, (nint)gvalue);
        g_value_unset((nint)gvalue);
    }

    /// <summary>Sets a <c>boolean</c> property.</summary>
    public static void SetBoolean(nint obj, string property, bool value)
    {
        var gvalue = stackalloc byte[32];
        new Span<byte>(gvalue, 32).Clear();
        g_value_init((nint)gvalue, GTypeBoolean);
        g_value_set_boolean((nint)gvalue, value ? 1 : 0);
        g_object_set_property(obj, property, (nint)gvalue);
        g_value_unset((nint)gvalue);
    }

    /// <summary>Whether an element factory of that name is installed.</summary>
    public static bool HasElement(string name)
    {
        var factory = gst_element_factory_find(name);
        if (factory == 0)
            return false;

        gst_object_unref(factory);
        return true;
    }

    /// <summary>Reads a string field of a structure, or null.</summary>
    public static string? GetString(nint structure, string field)
    {
        var text = gst_structure_get_string(structure, field);
        return text == 0 ? null : Marshal.PtrToStringUTF8(text);
    }

    [LibraryImport("gstreamer")]
    private static partial int gst_init_check(nint argc, nint argv, out nint error);

    [LibraryImport("glib")]
    private static partial void g_error_free(nint error);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint gst_parse_launch(string description, out nint error);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint gst_parse_bin_from_description(string description, int ghostUnlinkedPads, out nint error);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint gst_bin_get_by_name(nint bin, string name);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint gst_element_factory_find(string name);

    [LibraryImport("gstreamer")]
    public static partial int gst_element_set_state(nint element, int state);

    [LibraryImport("gstreamer")]
    public static partial int gst_element_get_state(nint element, out int state, out int pending, ulong timeout);

    [LibraryImport("gstreamer")]
    public static partial int gst_element_query_duration(nint element, int format, out long duration);

    [LibraryImport("gstreamer")]
    public static partial int gst_element_query_position(nint element, int format, out long position);

    [LibraryImport("gstreamer")]
    public static partial int gst_element_seek_simple(nint element, int format, int flags, long position);

    [LibraryImport("gstreamer")]
    public static partial int gst_element_seek(nint element, double rate, int format, int flags, int startType, long start, int stopType, long stop);

    [LibraryImport("gstreamer")]
    public static partial nint gst_element_get_bus(nint element);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint gst_element_get_static_pad(nint element, string name);

    [LibraryImport("gstreamer")]
    public static partial nint gst_pad_get_current_caps(nint pad);

    [LibraryImport("gstreamer")]
    public static partial nint gst_caps_get_structure(nint caps, uint index);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gst_structure_get_int(nint structure, string field, out int value);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gst_structure_get_fraction(nint structure, string field, out int numerator, out int denominator);

    [LibraryImport("gstreamer", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint gst_structure_get_string(nint structure, string field);

    [LibraryImport("gstreamer")]
    public static partial nint gst_bus_pop_filtered(nint bus, uint types);

    [LibraryImport("gstreamer")]
    public static partial void gst_mini_object_unref(nint miniObject);

    [LibraryImport("gstreamer")]
    public static partial void gst_object_unref(nint obj);

    [LibraryImport("gstreamer")]
    public static partial nint gst_sample_get_buffer(nint sample);

    [LibraryImport("gstreamer")]
    public static partial nint gst_sample_get_caps(nint sample);

    [LibraryImport("gstreamer")]
    public static partial int gst_buffer_map(nint buffer, nint info, int flags);

    [LibraryImport("gstreamer")]
    public static partial void gst_buffer_unmap(nint buffer, nint info);

    [LibraryImport("gstapp")]
    public static partial nint gst_app_sink_try_pull_preroll(nint sink, ulong timeout);

    [LibraryImport("gstapp")]
    public static partial nint gst_app_sink_try_pull_sample(nint sink, ulong timeout);

    [LibraryImport("gobject", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void g_object_set_property(nint obj, string name, nint value);

    [LibraryImport("gobject")]
    private static partial nint g_value_init(nint value, nuint type);

    [LibraryImport("gobject")]
    private static partial void g_value_set_double(nint value, double v);

    [LibraryImport("gobject")]
    private static partial void g_value_set_object(nint value, nint v);

    [LibraryImport("gobject")]
    private static partial void g_value_set_boolean(nint value, int v);

    [LibraryImport("gobject")]
    private static partial void g_value_unset(nint value);
}
