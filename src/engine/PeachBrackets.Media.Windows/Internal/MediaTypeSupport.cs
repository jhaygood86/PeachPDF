using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Media.Core;

namespace PeachBrackets.Media.Windows.Internal;

/// <summary>Answers <c>canPlayType</c> from the decoders and container handlers this Windows has.</summary>
internal static class MediaTypeSupport
{
    // The containers and the file extension Media Foundation registers a byte-stream handler under; null for the ones every Windows has.
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

    // Codec name prefixes (as a type's codecs parameter spells them), the media kind and the Media Foundation subtype to find a decoder of; null for the ones every
    // Windows decodes (H.264, AAC, MP3).
    private static readonly (string Prefix, CodecKind Kind, string? Subtype)[] s_codecs =
    [
        ("avc1", CodecKind.Video, null),
        ("avc3", CodecKind.Video, null),
        ("hev1", CodecKind.Video, "HEVC"),
        ("hvc1", CodecKind.Video, "HEVC"),
        ("vp09", CodecKind.Video, "VP90"),
        ("vp9", CodecKind.Video, "VP90"),
        ("vp08", CodecKind.Video, "VP80"),
        ("vp8", CodecKind.Video, "VP80"),
        ("av01", CodecKind.Video, "AV01"),
        ("mp4a", CodecKind.Audio, null),
        ("mp3", CodecKind.Audio, null),
        ("opus", CodecKind.Audio, "{0000704F-0000-0010-8000-00AA00389B71}"),
        ("vorbis", CodecKind.Audio, "{0000674F-0000-0010-8000-00AA00389B71}"),
        ("flac", CodecKind.Audio, "{0000F1AC-0000-0010-8000-00AA00389B71}"),
    ];

    private static readonly Dictionary<string, bool> s_known = new(StringComparer.OrdinalIgnoreCase);

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
            if (entry.Prefix is null || (entry.Subtype is not null && !HasDecoder(entry.Kind, entry.Subtype)))
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

    private static bool HasDecoder(CodecKind kind, string subtype) => Cached(kind + ":" + subtype, () =>
    {
        try
        {
            // The query is asynchronous only in its shape; it answers from a local registry of transforms.
            var found = new CodecQuery().FindAllAsync(kind, CodecCategory.Decoder, subtype).AsTask().GetAwaiter().GetResult();
            return found.Count > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return false;
        }
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
