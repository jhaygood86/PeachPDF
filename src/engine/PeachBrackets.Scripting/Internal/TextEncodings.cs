using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The decoders <c>TextDecoder</c> reads with besides the fast UTF-8 path: UTF-16 as the Encoding Standard defines it, the single-byte encodings
/// (tables built from the .NET code pages, with the bytes the standard leaves undefined listed here), <c>x-user-defined</c>, and the CJK encodings
/// through the .NET decoders. ISO-8859-10, -14 and -16 have no .NET code page and are not available.
/// </summary>
internal static class TextEncodings
{
    private static readonly Dictionary<string, int> CodePages = new(StringComparer.Ordinal)
    {
        ["ibm866"] = 866, ["iso-8859-2"] = 28592, ["iso-8859-3"] = 28593, ["iso-8859-4"] = 28594, ["iso-8859-5"] = 28595, ["iso-8859-6"] = 28596,
        ["iso-8859-7"] = 28597, ["iso-8859-8"] = 28598, ["iso-8859-8-i"] = 28598, ["iso-8859-13"] = 28603, ["iso-8859-15"] = 28605,
        ["koi8-r"] = 20866, ["koi8-u"] = 21866, ["macintosh"] = 10000, ["windows-874"] = 874, ["windows-1250"] = 1250, ["windows-1251"] = 1251,
        ["windows-1252"] = 1252, ["windows-1253"] = 1253, ["windows-1254"] = 1254, ["windows-1255"] = 1255, ["windows-1256"] = 1256,
        ["windows-1257"] = 1257, ["windows-1258"] = 1258, ["x-mac-cyrillic"] = 10007,
    };

    private static readonly Dictionary<string, int> MultiByteCodePages = new(StringComparer.Ordinal)
    {
        ["gbk"] = 936, ["gb18030"] = 54936, ["big5"] = 950, ["euc-jp"] = 51932, ["shift_jis"] = 932, ["euc-kr"] = 51949, ["iso-2022-jp"] = 50220,
    };

    // Bytes the Encoding Standard's index leaves without a code point.
    private static readonly Dictionary<string, int[]> Undefined = new(StringComparer.Ordinal)
    {
        ["iso-8859-3"] = [0xA5, 0xAE, 0xBE, 0xC3, 0xD0, 0xE3, 0xF0],
        ["iso-8859-6"] = [0xA1, 0xA2, 0xA3, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBC, 0xBD, 0xBE, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, .. Range(0xF3, 0xFF)],
        ["iso-8859-7"] = [0xAE, 0xD2, 0xFF],
        ["iso-8859-8"] = [0xA1, 0xBF, .. Range(0xC0, 0xDE), 0xFB, 0xFC, 0xFF],
        ["iso-8859-8-i"] = [0xA1, 0xBF, .. Range(0xC0, 0xDE), 0xFB, 0xFC, 0xFF],
        ["windows-874"] = [.. Range(0xDB, 0xDE), .. Range(0xFC, 0xFF)],
        ["windows-1253"] = [0xAA, 0xD2, 0xFF],
        ["windows-1255"] = [.. Range(0xD9, 0xDF), 0xFB, 0xFC, 0xFF],
        ["windows-1257"] = [0xA1, 0xA5],
    };

    private static readonly Dictionary<string, (int Byte, char Code)[]> Overrides = new(StringComparer.Ordinal)
    {
        ["iso-8859-7"] = [(0xA1, '‘'), (0xA2, '’'), (0xA4, '€'), (0xA5, '₯'), (0xAA, 'ͺ')],
        ["iso-8859-8"] = [(0xAF, '¯'), (0xFD, '‎'), (0xFE, '‏')],
        ["iso-8859-8-i"] = [(0xAF, '¯'), (0xFD, '‎'), (0xFE, '‏')],
        ["macintosh"] = [(0xBD, 'Ω')],
        ["x-mac-cyrillic"] = [(0xA2, 'Ґ'), (0xB6, 'ґ'), (0xFF, '€')],
    };

    private static readonly Dictionary<int, IChunkDecoder> Decoders = [];
    private static readonly Dictionary<string, char[]> Tables = new(StringComparer.Ordinal);
    private static int _nextId;
    private static bool _registered;

    private static int[] Range(int from, int to)
    {
        var values = new int[to - from + 1];
        for (var i = 0; i < values.Length; i++)
            values[i] = from + i;
        return values;
    }

    /// <summary>Makes a decoder for an encoding (a lower-case name of the Encoding Standard); returns its handle, or -1 when the encoding is not available.</summary>
    public static int Create(string name, bool fatal)
    {
        IChunkDecoder? decoder = name switch
        {
            "utf-16le" => new Utf16Decoder(false, fatal),
            "utf-16be" => new Utf16Decoder(true, fatal),
            "x-user-defined" => new UserDefinedDecoder(),
            _ when CodePages.ContainsKey(name) => new SingleByteDecoder(TableFor(name), fatal),
            _ when MultiByteCodePages.ContainsKey(name) => CreateMultiByte(name, fatal),
            _ => null,
        };

        if (decoder is null)
            return -1;

        var id = ++_nextId;
        Decoders[id] = decoder;
        return id;
    }

    /// <summary>Decodes a chunk with a decoder made by <see cref="Create"/>; null when the decoder is fatal and the bytes are invalid. A flush ends the stream and frees the decoder.</summary>
    public static string? Run(int id, byte[] bytes, bool flush)
    {
        if (!Decoders.TryGetValue(id, out var decoder))
            return null;

        var text = decoder.Decode(bytes, flush);
        if (flush || text is null)
            Decoders.Remove(id);

        return text;
    }

    private static IChunkDecoder? CreateMultiByte(string name, bool fatal)
    {
        EnsureProvider();
        try
        {
            var encoding = Encoding.GetEncoding(MultiByteCodePages[name], EncoderFallback.ReplacementFallback, fatal ? DecoderFallback.ExceptionFallback : new DecoderReplacementFallback("�"));
            return new ClrDecoder(encoding.GetDecoder());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static void EnsureProvider()
    {
        if (_registered)
            return;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _registered = true;
    }

    private static char[] TableFor(string name)
    {
        if (Tables.TryGetValue(name, out var table))
            return table;

        EnsureProvider();
        var encoding = Encoding.GetEncoding(CodePages[name]);
        var undefined = Undefined.TryGetValue(name, out var list) ? new HashSet<int>(list) : [];
        table = new char[256];
        for (var b = 0; b < 256; b++)
        {
            if (undefined.Contains(b))
            {
                table[b] = '￿';
                continue;
            }

            var text = encoding.GetString([(byte)b]);
            table[b] = text.Length == 1 && text[0] != '�' ? text[0] : '￿';
        }

        // The .NET tables differ from the Encoding Standard's in places: the C1 controls of the ISO-8859 family, and a few older ISO-8859-7 and -8 mappings.
        if (name.StartsWith("iso-8859-", StringComparison.Ordinal))
        {
            for (var b = 0x80; b <= 0x9F; b++)
                table[b] = (char)b;
        }

        foreach (var (b, c) in Overrides.GetValueOrDefault(name, []))
            table[b] = c;

        Tables[name] = table;
        return table;
    }

    private interface IChunkDecoder
    {
        string? Decode(byte[] bytes, bool flush);
    }

    private sealed class SingleByteDecoder(char[] table, bool fatal) : IChunkDecoder
    {
        public string? Decode(byte[] bytes, bool flush)
        {
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                var c = table[bytes[i]];
                if (c == '￿')
                {
                    if (fatal)
                        return null;
                    c = '�';
                }

                chars[i] = c;
            }

            return new string(chars);
        }
    }

    private sealed class UserDefinedDecoder : IChunkDecoder
    {
        public string? Decode(byte[] bytes, bool flush)
        {
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
                chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : (char)(0xF780 + bytes[i] - 0x80);
            return new string(chars);
        }
    }

    private sealed class ClrDecoder(Decoder decoder) : IChunkDecoder
    {
        public string? Decode(byte[] bytes, bool flush)
        {
            try
            {
                var chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length, flush)];
                var count = decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush);
                return new string(chars, 0, count);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
    }

    /// <summary>The UTF-16 decoder of the Encoding Standard (one replacement for a truncated end, a lone surrogate replaced and the next unit kept).</summary>
    private sealed class Utf16Decoder(bool bigEndian, bool fatal) : IChunkDecoder
    {
        private int _leadByte = -1;
        private int _leadSurrogate = -1;

        public string? Decode(byte[] bytes, bool flush)
        {
            var output = new StringBuilder();
            foreach (var b in bytes)
            {
                if (_leadByte < 0)
                {
                    _leadByte = b;
                    continue;
                }

                var unit = bigEndian ? (_leadByte << 8) + b : (b << 8) + _leadByte;
                _leadByte = -1;
                if (!Unit(unit, output))
                    return null;
            }

            if (flush && (_leadByte >= 0 || _leadSurrogate >= 0))
            {
                _leadByte = -1;
                _leadSurrogate = -1;
                if (fatal)
                    return null;
                output.Append('�');
            }

            return output.ToString();
        }

        private bool Unit(int unit, StringBuilder output)
        {
            if (_leadSurrogate >= 0)
            {
                var lead = _leadSurrogate;
                _leadSurrogate = -1;
                if (unit is >= 0xDC00 and <= 0xDFFF)
                {
                    output.Append((char)lead).Append((char)unit);
                    return true;
                }

                if (fatal)
                    return false;
                output.Append('�');
            }

            if (unit is >= 0xD800 and <= 0xDBFF)
            {
                _leadSurrogate = unit;
                return true;
            }

            if (unit is >= 0xDC00 and <= 0xDFFF)
            {
                if (fatal)
                    return false;
                output.Append('�');
                return true;
            }

            output.Append((char)unit);
            return true;
        }
    }
}
