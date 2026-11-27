using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Media.GStreamer.Internal;

/// <summary>Answers <c>canPlayType</c> from the elements the installed GStreamer has.</summary>
internal static class MediaTypeSupport
{
    // Each entry is a list of alternatives: one of the named elements being installed is enough.
    private static readonly Dictionary<string, string[][]> s_containers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/webm"] = [["matroskademux"]],
        ["audio/webm"] = [["matroskademux"]],
        ["video/x-matroska"] = [["matroskademux"]],
        ["video/mp4"] = [["qtdemux"]],
        ["audio/mp4"] = [["qtdemux"]],
        ["video/x-m4v"] = [["qtdemux"]],
        ["audio/x-m4a"] = [["qtdemux"]],
        ["video/ogg"] = [["oggdemux"]],
        ["audio/ogg"] = [["oggdemux"]],
        ["application/ogg"] = [["oggdemux"]],
        ["audio/wav"] = [["wavparse"]],
        ["audio/wave"] = [["wavparse"]],
        ["audio/x-wav"] = [["wavparse"]],
        ["audio/mpeg"] = [["mpegaudioparse"]],
        ["audio/mp3"] = [["mpegaudioparse"]],
        ["audio/flac"] = [["flacparse", "flacdec"]],
        ["audio/aac"] = [["aacparse"]],
    };

    // Codec name prefixes (as a type's codecs parameter spells them) and the decoders that play them.
    private static readonly (string Prefix, string[] Decoders)[] s_codecs =
    [
        ("vp8", ["vp8dec", "avdec_vp8"]),
        ("vp08", ["vp8dec", "avdec_vp8"]),
        ("vp9", ["vp9dec", "avdec_vp9"]),
        ("vp09", ["vp9dec", "avdec_vp9"]),
        ("avc1", ["avdec_h264", "openh264dec", "vah264dec", "nvh264dec"]),
        ("avc3", ["avdec_h264", "openh264dec", "vah264dec", "nvh264dec"]),
        ("hev1", ["avdec_h265", "vah265dec", "nvh265dec"]),
        ("hvc1", ["avdec_h265", "vah265dec", "nvh265dec"]),
        ("av01", ["av1dec", "dav1ddec", "avdec_av1", "vaav1dec"]),
        ("theora", ["theoradec"]),
        ("opus", ["opusdec", "avdec_opus"]),
        ("vorbis", ["vorbisdec", "avdec_vorbis"]),
        ("flac", ["flacdec", "avdec_flac"]),
        ("mp3", ["mpg123audiodec", "avdec_mp3", "mpg123"]),
        ("mp4a.6b", ["mpg123audiodec", "avdec_mp3", "mpg123"]),
        ("mp4a", ["avdec_aac", "faad", "fdkaacdec"]),
    ];

    private static readonly Dictionary<string, bool> s_elements = new(StringComparer.Ordinal);

    public static string CanPlay(string mimeType)
    {
        var parts = mimeType.Split(';');
        var container = parts[0].Trim();
        if (!s_containers.TryGetValue(container, out var required) || !required.All(AnyInstalled))
            return "";

        string[] codecs = [];
        foreach (var parameter in parts.Skip(1))
        {
            var equals = parameter.IndexOf('=');
            if (equals > 0 && string.Equals(parameter[..equals].Trim(), "codecs", StringComparison.OrdinalIgnoreCase))
            {
                codecs = parameter[(equals + 1)..].Trim().Trim('"', '\'').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        if (codecs.Length == 0)
            return "maybe";

        foreach (var codec in codecs)
        {
            var found = false;
            foreach (var (prefix, decoders) in s_codecs)
            {
                if (codec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (prefix != "mp4a" || !codec.StartsWith("mp4a.6b", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!AnyInstalled(decoders))
                        return "";

                    found = true;
                    break;
                }
            }

            if (!found)
                return "";
        }

        return "probably";
    }

    private static bool AnyInstalled(string[] names)
    {
        foreach (var name in names)
        {
            bool installed;
            lock (s_elements)
            {
                if (!s_elements.TryGetValue(name, out installed))
                    s_elements[name] = installed = GstNative.HasElement(name);
            }

            if (installed)
                return true;
        }

        return false;
    }
}
