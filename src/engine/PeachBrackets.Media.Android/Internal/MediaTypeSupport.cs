using Android.Media;

namespace PeachBrackets.Media.Android.Internal;

/// <summary>Answers <c>canPlayType</c> from the decoders this device has.</summary>
internal static class MediaTypeSupport
{
    // Containers the platform parses on every device.
    private static readonly HashSet<string> s_containers = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "audio/mp4", "video/x-m4v", "audio/x-m4a", "audio/aac", "audio/mpeg", "audio/mp3", "audio/wav", "audio/wave", "audio/x-wav", "audio/flac",
        "video/webm", "audio/webm", "video/x-matroska", "video/ogg", "audio/ogg", "application/ogg", "video/3gpp", "audio/3gpp",
    };

    // Codec name prefixes (as a type's codecs parameter spells them) and the MediaCodec mime type a decoder of it is listed under.
    private static readonly (string Prefix, string Mime)[] s_codecs =
    [
        ("avc1", "video/avc"), ("avc3", "video/avc"), ("hev1", "video/hevc"), ("hvc1", "video/hevc"),
        ("vp09", "video/x-vnd.on2.vp9"), ("vp9", "video/x-vnd.on2.vp9"), ("vp08", "video/x-vnd.on2.vp8"), ("vp8", "video/x-vnd.on2.vp8"),
        ("av01", "video/av01"), ("mp4a", "audio/mp4a-latm"), ("mp3", "audio/mpeg"), ("opus", "audio/opus"), ("vorbis", "audio/vorbis"), ("flac", "audio/flac"),
    ];

    private static HashSet<string>? s_decoded;

    public static string CanPlay(string mimeType)
    {
        var parts = mimeType.Split(';');
        if (!s_containers.Contains(parts[0].Trim()))
            return "";

        var codecs = parts.Skip(1)
            .Select(p => (Index: p.IndexOf('='), Text: p))
            .Where(p => p.Index > 0 && string.Equals(p.Text[..p.Index].Trim(), "codecs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Text[(p.Index + 1)..].Trim().Trim('"', '\'').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        if (codecs.Length == 0)
            return "maybe";

        var decoders = Decoders();
        foreach (var codec in codecs)
        {
            var entry = Array.Find(s_codecs, c => codec.StartsWith(c.Prefix, StringComparison.OrdinalIgnoreCase));
            if (entry.Prefix is null || !decoders.Contains(entry.Mime))
                return "";
        }

        return "probably";
    }

    // The mime types this device has a decoder for.
    private static HashSet<string> Decoders()
    {
        if (s_decoded is { } known)
            return known;

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in new MediaCodecList(MediaCodecListKind.RegularCodecs).GetCodecInfos() ?? [])
        {
            if (info is null || info.IsEncoder)
                continue;

            foreach (var type in info.GetSupportedTypes() ?? [])
                found.Add(type);
        }

        return s_decoded = found;
    }
}
