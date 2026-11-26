using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PeachBrackets.Scripting.Internal.Idna;

/// <summary>
/// Domain to ASCII as the URL standard asks for it: UTS #46 processing (non-transitional, CheckHyphens off, CheckBidi and CheckJoiners on,
/// UseSTD3ASCIIRules off, no DNS length check) over the mapping table in <c>idna-uts46.bin</c> (Brotli; generated from the Unicode 18 <c>IdnaMappingTable.txt</c> and <c>UnicodeData.txt</c> by <c>assets/unicode/generate_idna_table.py</c>).
/// </summary>
internal static class Uts46
{
    private static readonly Lazy<Table> Data = new(Load);

    private sealed class Table(int[] starts, byte[] statuses, int[][] replacements, HashSet<int> viramas)
    {
        public int[] Starts { get; } = starts;
        public byte[] Statuses { get; } = statuses;
        public int[][] Replacements { get; } = replacements;
        public HashSet<int> Viramas { get; } = viramas;

        public int IndexOf(int codePoint)
        {
            int low = 0, high = Starts.Length - 1;
            while (low < high)
            {
                var middle = (low + high + 1) >>> 1;
                if (Starts[middle] <= codePoint) low = middle; else high = middle - 1;
            }

            return low;
        }
    }

    private static Table Load()
    {
        using var resource = typeof(Uts46).Assembly.GetManifestResourceStream("idna-uts46.bin") ?? throw new InvalidOperationException("The IDNA mapping table is missing.");
        using var inflated = new BrotliStream(resource, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        inflated.CopyTo(buffer);
        var data = buffer.ToArray();
        var at = 0;

        int Read()
        {
            var value = 0;
            for (var shift = 0; ; shift += 7)
            {
                var b = data[at++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return value;
            }
        }

        var versionLength = Read(); // the Unicode version, which only the generator and a person reading the file care about
        at += versionLength;
        var count = Read();
        var starts = new int[count];
        var statuses = new byte[count];
        var replacements = new int[count][];
        var previous = 0;
        for (var i = 0; i < count; i++)
        {
            previous += Read();
            starts[i] = previous;
            statuses[i] = data[at++];
            if (statuses[i] == 'M')
            {
                var length = Read();
                replacements[i] = new int[length];
                for (var j = 0; j < length; j++)
                    replacements[i][j] = Read();
            }
        }

        var viramas = new HashSet<int>();
        var virama = 0;
        for (var n = Read(); n > 0; n--)
        {
            virama += Read();
            viramas.Add(virama);
        }

        return new Table(starts, statuses, replacements, viramas);
    }

    /// <summary>The ASCII form of a domain, or null when the processing finds an error.</summary>
    public static string? ToAscii(string domain)
    {
        var table = Data.Value;

        // 1. Map.
        var mapped = new StringBuilder(domain.Length);
        for (var i = 0; i < domain.Length; i++)
        {
            int cp = domain[i];
            if (char.IsHighSurrogate(domain[i]) && i + 1 < domain.Length && char.IsLowSurrogate(domain[i + 1]))
                cp = char.ConvertToUtf32(domain[i], domain[++i]);
            else if (char.IsSurrogate(domain[i]))
                return null;

            var index = table.IndexOf(cp);
            switch ((char)table.Statuses[index])
            {
                case 'V' or 'D':
                    mapped.Append(char.ConvertFromUtf32(cp));
                    break;
                case 'I':
                    break;
                case 'M':
                    foreach (var r in table.Replacements[index])
                        mapped.Append(char.ConvertFromUtf32(r));
                    break;
                default:
                    return null;
            }
        }

        // 2. Normalize.
        string normalized;
        try
        {
            normalized = mapped.ToString().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return null;
        }

        // 3-4. Break into labels, decode the xn-- ones, validate.
        var labels = normalized.Split('.');
        var decoded = new string[labels.Length];
        var bidiDomain = false;
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label.StartsWith("xn--", StringComparison.Ordinal))
            {
                var text = Punycode.Decode(label[4..]);
                if (text is null || text.Length == 0 || IsAscii(text))
                    return null;

                label = text;
            }
            else if (label.Length > 0 && !Valid(table, label, punycode: false))
            {
                return null;
            }

            decoded[i] = label;
            bidiDomain |= HasRightToLeft(label);
        }

        if (bidiDomain)
        {
            foreach (var label in decoded)
            {
                if (label.Length > 0 && !BidiRule(label))
                    return null;
            }
        }

        // 5. Convert each non-ASCII label to its A-label.
        var result = new StringBuilder();
        for (var i = 0; i < decoded.Length; i++)
        {
            if (i > 0)
                result.Append('.');

            if (IsAscii(decoded[i]))
            {
                result.Append(decoded[i]);
                continue;
            }

            var encoded = Punycode.Encode(decoded[i]);
            if (encoded is null)
                return null;

            result.Append("xn--").Append(encoded);
        }

        return result.ToString();
    }

    private static bool IsAscii(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F)
                return false;
        }

        return true;
    }

    // The validity criteria of UTS #46 section 4.1 for a label, with CheckHyphens off and CheckJoiners on.
    private static bool Valid(Table table, string label, bool punycode)
    {
        if (!label.IsNormalized(NormalizationForm.FormC))
            return false;
        if (punycode && label.StartsWith("xn--", StringComparison.Ordinal))
            return false;
        if (label.Contains('.'))
            return false;

        var first = char.ConvertToUtf32(label, 0);
        if (CharUnicodeInfo.GetUnicodeCategory(first) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return false;

        var runes = new List<int>();
        for (var i = 0; i < label.Length; i++)
        {
            var cp = char.ConvertToUtf32(label, i);
            if (char.IsHighSurrogate(label[i]))
                i++;
            runes.Add(cp);
        }

        foreach (var cp in runes)
        {
            if ((char)table.Statuses[table.IndexOf(cp)] is not ('V' or 'D'))
                return false;
        }

        for (var i = 0; i < runes.Count; i++)
        {
            if (runes[i] is 0x200C or 0x200D && !ContextJ(table, runes, i))
                return false;
        }

        return true;
    }

    // RFC 5892 appendix A.1 and A.2: a joiner needs a virama before it, or (ZWNJ) a joining context.
    private static bool ContextJ(Table table, List<int> label, int position)
    {
        if (position > 0 && table.Viramas.Contains(label[position - 1]))
            return true;

        if (label[position] == 0x200D)
            return false;

        var before = false;
        for (var i = position - 1; i >= 0; i--)
        {
            var type = ArabicJoining.TypeOf(label[i]);
            if (type == ArabicJoiningType.T)
                continue;

            before = type is ArabicJoiningType.L or ArabicJoiningType.D;
            break;
        }

        if (!before)
            return false;

        for (var i = position + 1; i < label.Count; i++)
        {
            var type = ArabicJoining.TypeOf(label[i]);
            if (type == ArabicJoiningType.T)
                continue;

            return type is ArabicJoiningType.R or ArabicJoiningType.D;
        }

        return false;
    }

    private static bool HasRightToLeft(string label)
    {
        foreach (var rune in label.EnumerateRunes())
        {
            if (Bidi.ClassOf(rune) is BidiClass.R or BidiClass.AL or BidiClass.AN)
                return true;
        }

        return false;
    }

    // RFC 5893 section 2, the Bidi Rule.
    private static bool BidiRule(string label)
    {
        var classes = new List<BidiClass>();
        foreach (var rune in label.EnumerateRunes())
            classes.Add(Bidi.ClassOf(rune));

        var rtl = classes[0] is BidiClass.R or BidiClass.AL;
        if (!rtl && classes[0] != BidiClass.L)
            return false;

        var allowed = rtl
            ? new[] { BidiClass.R, BidiClass.AL, BidiClass.AN, BidiClass.EN, BidiClass.ES, BidiClass.CS, BidiClass.ET, BidiClass.ON, BidiClass.BN, BidiClass.NSM }
            : new[] { BidiClass.L, BidiClass.EN, BidiClass.ES, BidiClass.CS, BidiClass.ET, BidiClass.ON, BidiClass.BN, BidiClass.NSM };
        var endings = rtl ? new[] { BidiClass.R, BidiClass.AL, BidiClass.EN, BidiClass.AN } : [BidiClass.L, BidiClass.EN];

        var validEnding = false;
        BidiClass? number = null;
        foreach (var c in classes)
        {
            if (Array.IndexOf(allowed, c) < 0)
                return false;

            if (Array.IndexOf(endings, c) >= 0)
                validEnding = true;
            else if (c != BidiClass.NSM)
                validEnding = false;

            if (rtl && c is BidiClass.EN or BidiClass.AN)
            {
                if (number is null)
                    number = c;
                else if (number != c)
                    return false;
            }
        }

        return validEnding;
    }
}

/// <summary>RFC 3492 Punycode.</summary>
internal static class Punycode
{
    private const int Base = 36, TMin = 1, TMax = 26, Skew = 38, Damp = 700, InitialBias = 72, InitialN = 128;

    private static int Adapt(int delta, int points, bool first)
    {
        delta = first ? delta / Damp : delta / 2;
        delta += delta / points;
        var k = 0;
        while (delta > ((Base - TMin) * TMax) / 2)
        {
            delta /= Base - TMin;
            k += Base;
        }

        return k + (Base - TMin + 1) * delta / (delta + Skew);
    }

    private static char Digit(int d) => (char)(d < 26 ? 'a' + d : '0' + (d - 26));

    /// <summary>The Punycode of a label (without the <c>xn--</c>), or null when it overflows.</summary>
    public static string? Encode(string input)
    {
        var codePoints = new List<int>();
        for (var i = 0; i < input.Length; i++)
        {
            codePoints.Add(char.ConvertToUtf32(input, i));
            if (char.IsHighSurrogate(input[i]))
                i++;
        }

        var output = new StringBuilder();
        foreach (var cp in codePoints)
        {
            if (cp < 0x80)
                output.Append((char)cp);
        }

        var basic = output.Length;
        var handled = basic;
        if (basic > 0)
            output.Append('-');

        var n = InitialN;
        var delta = 0;
        var bias = InitialBias;
        while (handled < codePoints.Count)
        {
            var m = int.MaxValue;
            foreach (var cp in codePoints)
            {
                if (cp >= n && cp < m)
                    m = cp;
            }

            if ((long)(m - n) * (handled + 1) > int.MaxValue - delta)
                return null;

            delta += (m - n) * (handled + 1);
            n = m;
            foreach (var cp in codePoints)
            {
                if (cp < n)
                    delta++;
                if (cp != n)
                    continue;

                var q = delta;
                for (var k = Base; ; k += Base)
                {
                    var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                    if (q < t)
                        break;

                    output.Append(Digit(t + (q - t) % (Base - t)));
                    q = (q - t) / (Base - t);
                }

                output.Append(Digit(q));
                bias = Adapt(delta, handled + 1, handled == basic);
                delta = 0;
                handled++;
            }

            delta++;
            n++;
        }

        return output.ToString();
    }

    /// <summary>The label a Punycode string stands for, or null when it is not valid Punycode.</summary>
    public static string? Decode(string input)
    {
        var output = new List<int>();
        var delimiter = input.LastIndexOf('-');
        var start = 0;
        if (delimiter > 0)
        {
            for (var i = 0; i < delimiter; i++)
            {
                if (input[i] > 0x7F)
                    return null;

                output.Add(input[i]);
            }

            start = delimiter + 1;
        }
        else if (delimiter == 0)
        {
            start = 1;
        }

        var n = InitialN;
        var iValue = 0;
        var bias = InitialBias;
        for (var index = start; index < input.Length;)
        {
            var oldI = iValue;
            var w = 1;
            for (var k = Base; ; k += Base)
            {
                if (index >= input.Length)
                    return null;

                var c = input[index++];
                var digit = c is >= 'a' and <= 'z' ? c - 'a' : c is >= 'A' and <= 'Z' ? c - 'A' : c is >= '0' and <= '9' ? c - '0' + 26 : -1;
                if (digit < 0)
                    return null;
                if (digit > (int.MaxValue - iValue) / w)
                    return null;

                iValue += digit * w;
                var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                if (digit < t)
                    break;
                if (w > int.MaxValue / (Base - t))
                    return null;

                w *= Base - t;
            }

            bias = Adapt(iValue - oldI, output.Count + 1, oldI == 0);
            if (iValue / (output.Count + 1) > int.MaxValue - n)
                return null;

            n += iValue / (output.Count + 1);
            iValue %= output.Count + 1;
            if (n > 0x10FFFF || n is >= 0xD800 and <= 0xDFFF)
                return null;

            output.Insert(iValue++, n);
        }

        var text = new StringBuilder();
        foreach (var cp in output)
            text.Append(char.ConvertFromUtf32(cp));

        return text.ToString();
    }
}
