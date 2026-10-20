using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// The general-purpose stream decoders a reader needs (Flate, LZW, ASCII85, ASCIIHex, RunLength, and the PNG/TIFF
/// predictors). Image codecs (DCT, JPX, CCITT, JBIG2) are not decoded here: <see cref="PdfSourceDocument.DecodeStream"/>
/// stops at the first one and reports it.
/// </summary>
internal static class PdfStreamFilters
{
    /// <summary>Whether <paramref name="name"/> (with or without its abbreviation) is a filter this class decodes.</summary>
    public static bool IsGeneral(string name) => Canonical(name) is "FlateDecode" or "LZWDecode" or "ASCII85Decode" or "ASCIIHexDecode" or "RunLengthDecode";

    /// <summary>Maps an inline-image abbreviation to the full filter name.</summary>
    public static string Canonical(string name) => name switch
    {
        "Fl" => "FlateDecode",
        "LZW" => "LZWDecode",
        "A85" => "ASCII85Decode",
        "AHx" => "ASCIIHexDecode",
        "RL" => "RunLengthDecode",
        "DCT" => "DCTDecode",
        "CCF" => "CCITTFaxDecode",
        _ => name,
    };

    /// <summary>Decodes <paramref name="data"/> with one general filter.</summary>
    public static byte[] Decode(string filter, byte[] data, int predictor = 1, int colors = 1, int bitsPerComponent = 8, int columns = 1, int earlyChange = 1)
    {
        byte[] decoded = Canonical(filter) switch
        {
            "FlateDecode" => Inflate(data),
            "LZWDecode" => Lzw(data, earlyChange),
            "ASCII85Decode" => Ascii85(data),
            "ASCIIHexDecode" => AsciiHex(data),
            "RunLengthDecode" => RunLength(data),
            _ => data,
        };
        return Canonical(filter) is "FlateDecode" or "LZWDecode" && predictor > 1
            ? Predict(decoded, predictor, colors, bitsPerComponent, columns)
            : decoded;
    }

    static byte[] Inflate(byte[] data)
    {
        // A truncated or slightly damaged stream still yields what decoded before the damage.
        var output = new MemoryStream();
        try
        {
            using var input = new MemoryStream(data);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            z.CopyTo(output);
        }
        catch (InvalidDataException) { }
        catch (EndOfStreamException) { }
        if (output.Length == 0 && data.Length > 2)
        {
            // Some writers omit the zlib header: retry as raw deflate.
            try
            {
                using var input = new MemoryStream(data);
                using var d = new DeflateStream(input, CompressionMode.Decompress);
                d.CopyTo(output);
            }
            catch (InvalidDataException) { }
            catch (EndOfStreamException) { }
        }
        return output.ToArray();
    }

    static byte[] AsciiHex(byte[] data)
    {
        var o = new List<byte>(data.Length / 2);
        int hi = -1;
        foreach (byte b in data)
        {
            if (b == '>') break;
            int v = b switch { >= (byte)'0' and <= (byte)'9' => b - '0', >= (byte)'a' and <= (byte)'f' => b - 'a' + 10, >= (byte)'A' and <= (byte)'F' => b - 'A' + 10, _ => -1 };
            if (v < 0) continue;
            if (hi < 0) hi = v; else { o.Add((byte)(hi * 16 + v)); hi = -1; }
        }
        if (hi >= 0) o.Add((byte)(hi * 16));
        return o.ToArray();
    }

    static byte[] Ascii85(byte[] data)
    {
        var o = new List<byte>(data.Length * 4 / 5);
        uint acc = 0;
        int n = 0;
        int i = 0;
        if (data.Length >= 2 && data[0] == '<' && data[1] == '~') i = 2;
        for (; i < data.Length; i++)
        {
            byte b = data[i];
            if (b == '~') break;
            if (PdfLexer.IsWhite(b)) continue;
            if (b == 'z' && n == 0) { o.AddRange(new byte[4]); continue; }
            if (b < '!' || b > 'u') continue;
            acc = unchecked(acc * 85 + (uint)(b - '!'));
            if (++n == 5)
            {
                o.Add((byte)(acc >> 24)); o.Add((byte)(acc >> 16)); o.Add((byte)(acc >> 8)); o.Add((byte)acc);
                acc = 0; n = 0;
            }
        }
        if (n > 1)
        {
            for (int k = n; k < 5; k++) acc = unchecked(acc * 85 + 84);
            for (int k = 0; k < n - 1; k++) o.Add((byte)(acc >> (24 - 8 * k)));
        }
        return o.ToArray();
    }

    static byte[] RunLength(byte[] data)
    {
        var o = new List<byte>(data.Length * 2);
        int i = 0;
        while (i < data.Length)
        {
            int len = data[i++];
            if (len == 128) break;
            if (len < 128)
            {
                int count = Math.Min(len + 1, data.Length - i);
                for (int k = 0; k < count; k++) o.Add(data[i + k]);
                i += count;
            }
            else if (i < data.Length)
            {
                byte v = data[i++];
                for (int k = 0; k < 257 - len; k++) o.Add(v);
            }
        }
        return o.ToArray();
    }

    static byte[] Lzw(byte[] data, int earlyChange)
    {
        var o = new List<byte>(data.Length * 3);
        var table = new List<byte[]>(4096);
        void Reset()
        {
            table.Clear();
            for (int k = 0; k < 256; k++) table.Add([(byte)k]);
            table.Add([]); // 256 clear
            table.Add([]); // 257 eod
        }
        Reset();
        int bits = 9, bitPos = 0;
        byte[]? prev = null;
        while (bitPos + bits <= data.Length * 8)
        {
            int code = 0;
            for (int k = 0; k < bits; k++, bitPos++)
                code = (code << 1) | ((data[bitPos >> 3] >> (7 - (bitPos & 7))) & 1);
            if (code == 256) { Reset(); bits = 9; prev = null; continue; }
            if (code == 257) break;
            byte[] entry;
            if (code < table.Count) entry = table[code];
            else if (prev is not null && code == table.Count) entry = [.. prev, prev[0]];
            else break;
            o.AddRange(entry);
            if (prev is not null && table.Count < 4096) table.Add([.. prev, entry[0]]);
            prev = entry;
            int size = table.Count + earlyChange;
            bits = size >= 4096 ? 12 : size >= 2048 ? 12 : size >= 1024 ? 11 : size >= 512 ? 10 : 9;
        }
        return o.ToArray();
    }

    /// <summary>Undoes a TIFF (2) or PNG (10-15) predictor.</summary>
    public static byte[] Predict(byte[] data, int predictor, int colors, int bpc, int columns)
    {
        int bpp = Math.Max(1, (colors * bpc + 7) / 8);
        int rowBytes = (columns * colors * bpc + 7) / 8;
        if (rowBytes <= 0) return data;
        if (predictor == 2)
        {
            if (bpc != 8) return data; // 16-bit and sub-byte TIFF prediction are not seen in practice
            var res = (byte[])data.Clone();
            for (int row = 0; row + rowBytes <= res.Length; row += rowBytes)
                for (int x = bpp; x < rowBytes; x++)
                    res[row + x] = (byte)(res[row + x] + res[row + x - bpp]);
            return res;
        }
        if (predictor < 10) return data;
        int stride = rowBytes + 1;
        int rows = (data.Length + stride - 1) / stride;
        var output = new byte[rows * rowBytes];
        for (int r = 0; r < rows; r++)
        {
            int src = r * stride;
            if (src >= data.Length) break;
            int filter = data[src];
            int dst = r * rowBytes;
            int n = Math.Min(rowBytes, data.Length - src - 1);
            for (int x = 0; x < n; x++)
            {
                int raw = data[src + 1 + x];
                int a = x >= bpp ? output[dst + x - bpp] : 0;
                int b = r > 0 ? output[dst - rowBytes + x] : 0;
                int c = x >= bpp && r > 0 ? output[dst - rowBytes + x - bpp] : 0;
                output[dst + x] = (byte)(filter switch
                {
                    1 => raw + a,
                    2 => raw + b,
                    3 => raw + ((a + b) >> 1),
                    4 => raw + Paeth(a, b, c),
                    _ => raw,
                });
            }
        }
        return output;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
