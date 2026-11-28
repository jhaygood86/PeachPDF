using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace PeachBrackets.Media.MediaFoundation.Internal;

/// <summary>Answers <c>canPlayType</c> from the decoders and container handlers this Windows has.</summary>
internal static unsafe class MediaTypeSupport
{
    // The containers and the extension Media Foundation registers a byte-stream handler under; null for the ones every Windows has.
    private static readonly Dictionary<string, string?> s_containers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = null,
        ["audio/mp4"] = null,
        ["video/x-m4v"] = null,
        ["audio/x-m4a"] = null,
        ["audio/aac"] = null,
        ["audio/mpeg"] = null,
        ["audio/mp3"] = null,
        ["audio/wav"] = null,
        ["audio/wave"] = null,
        ["audio/x-wav"] = null,
        ["audio/flac"] = ".flac",
        ["video/webm"] = ".webm",
        ["audio/webm"] = ".webm",
        ["video/ogg"] = ".ogg",
        ["audio/ogg"] = ".ogg",
        ["application/ogg"] = ".ogg",
        ["video/x-matroska"] = ".mkv",
    };

    // Codec name prefixes (as a type's codecs parameter spells them), the kind and the decoder's input subtype as four characters (or a GUID's first group);
    // null for the ones every Windows decodes.
    private static readonly (string Prefix, bool Video, uint? Subtype)[] s_codecs =
    [
        ("avc1", true, null),
        ("avc3", true, null),
        ("hev1", true, FourCc("HEVC")),
        ("hvc1", true, FourCc("HEVC")),
        ("vp09", true, FourCc("VP90")),
        ("vp9", true, FourCc("VP90")),
        ("vp08", true, FourCc("VP80")),
        ("vp8", true, FourCc("VP80")),
        ("av01", true, FourCc("AV01")),
        ("mp4a", false, null),
        ("mp3", false, null),
        ("opus", false, 0x704F),
        ("vorbis", false, 0x674F),
        ("flac", false, 0xF1AC),
    ];

    private static readonly Dictionary<string, bool> s_known = new(StringComparer.OrdinalIgnoreCase);

    private static uint FourCc(string text) => text[0] | ((uint)text[1] << 8) | ((uint)text[2] << 16) | ((uint)text[3] << 24);

    public static string CanPlay(string mimeType)
    {
        var parts = mimeType.Split(';');
        if (!s_containers.TryGetValue(parts[0].Trim(), out var extension) || (extension is not null && !HasHandler(extension)))
            return "";

        var codecs = parts.Skip(1)
            .Select(p => (Index: p.IndexOf('='), Text: p))
            .Where(p => p.Index > 0 && string.Equals(p.Text[..p.Index].Trim(), "codecs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Text[(p.Index + 1)..].Trim().Trim('"', '\'').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        if (codecs.Length == 0)
            return "maybe";

        foreach (var codec in codecs)
        {
            var entry = Array.Find(s_codecs, c => codec.StartsWith(c.Prefix, StringComparison.OrdinalIgnoreCase));
            if (entry.Prefix is null || (entry.Subtype is { } subtype && !HasDecoder(entry.Video, subtype)))
                return "";
        }

        return "probably";
    }

    // Whether Media Foundation has a handler for a file extension, which is how an installed demuxer (the Web Media Extensions for WebM and Ogg) shows.
    private static bool HasHandler(string extension) => Cached("handler:" + extension, () =>
    {
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows Media Foundation\ByteStreamHandlers\", @"SOFTWARE\WOW6432Node\Microsoft\Windows Media Foundation\ByteStreamHandlers\" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path + extension);
            if (key is not null)
                return true;
        }

        return false;
    });

    // Whether a transform that decodes a subtype is registered.
    private static bool HasDecoder(bool video, uint subtype) => Cached((video ? "v:" : "a:") + subtype, () =>
    {
        Mf.Ensure();
        var category = video ? PInvoke.MFT_CATEGORY_VIDEO_DECODER : PInvoke.MFT_CATEGORY_AUDIO_DECODER;
        var input = new MFT_REGISTER_TYPE_INFO
        {
            guidMajorType = video ? PInvoke.MFMediaType_Video : PInvoke.MFMediaType_Audio,

            // Every Media Foundation subtype is a GUID of the form {fourcc-0000-0010-8000-00AA00389B71}.
            guidSubtype = new Guid(subtype, 0, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71),
        };

        IMFActivate** activates;
        uint count;
        try
        {
            PInvoke.MFTEnumEx(category, MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_ASYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER, input, null, out activates, out count).ThrowOnFailure();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }

        for (var i = 0u; i < count; i++)
            activates[i]->Release();

        if (activates is not null)
            PInvoke.CoTaskMemFree(activates);

        return count > 0;
    });

    private static bool Cached(string key, Func<bool> compute)
    {
        lock (s_known)
        {
            if (!s_known.TryGetValue(key, out var value))
                s_known[key] = value = compute();

            return value;
        }
    }
}
