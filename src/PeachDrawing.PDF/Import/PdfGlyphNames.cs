using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Glyph names of the Adobe standard Latin set: the CFF standard strings (SIDs 0 to 228, ISO 32000-1 / Adobe TN 5176 Appendix A) and the
/// Unicode value of each name, enough to turn the names a Type 1, CFF or Differences array uses into characters.
/// </summary>
internal static class PdfGlyphNames
{
    /// <summary>The CFF standard strings in SID order; SIDs 229 to 390 (the expert set) are not named.</summary>
    public static readonly string[] StandardStrings =
    [
        ".notdef", "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quoteright", "parenleft", "parenright",
        "asterisk", "plus", "comma", "hyphen", "period", "slash", "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "colon", "semicolon", "less", "equal", "greater", "question", "at",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "bracketleft", "backslash", "bracketright", "asciicircum", "underscore", "quoteleft",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        "braceleft", "bar", "braceright", "asciitilde", "exclamdown", "cent", "sterling", "fraction", "yen", "florin", "section", "currency",
        "quotesingle", "quotedblleft", "guillemotleft", "guilsinglleft", "guilsinglright", "fi", "fl", "endash", "dagger", "daggerdbl",
        "periodcentered", "paragraph", "bullet", "quotesinglbase", "quotedblbase", "quotedblright", "guillemotright", "ellipsis", "perthousand",
        "questiondown", "grave", "acute", "circumflex", "tilde", "macron", "breve", "dotaccent", "dieresis", "ring", "cedilla", "hungarumlaut",
        "ogonek", "caron", "emdash", "AE", "ordfeminine", "Lslash", "Oslash", "OE", "ordmasculine", "ae", "dotlessi", "lslash", "oslash", "oe",
        "germandbls", "onesuperior", "logicalnot", "mu", "trademark", "Eth", "onehalf", "plusminus", "Thorn", "onequarter", "divide", "brokenbar",
        "degree", "thorn", "threequarters", "twosuperior", "registered", "minus", "eth", "multiply", "threesuperior", "copyright",
        "Aacute", "Acircumflex", "Adieresis", "Agrave", "Aring", "Atilde", "Ccedilla", "Eacute", "Ecircumflex", "Edieresis", "Egrave", "Iacute",
        "Icircumflex", "Idieresis", "Igrave", "Ntilde", "Oacute", "Ocircumflex", "Odieresis", "Ograve", "Otilde", "Scaron", "Uacute", "Ucircumflex",
        "Udieresis", "Ugrave", "Yacute", "Ydieresis", "Zcaron", "aacute", "acircumflex", "adieresis", "agrave", "aring", "atilde", "ccedilla",
        "eacute", "ecircumflex", "edieresis", "egrave", "iacute", "icircumflex", "idieresis", "igrave", "ntilde", "oacute", "ocircumflex",
        "odieresis", "ograve", "otilde", "scaron", "uacute", "ucircumflex", "udieresis", "ugrave", "yacute", "ydieresis", "zcaron",
    ];

    /// <summary>The number of standard strings (the first custom string has this SID).</summary>
    public const int StandardStringCount = 391;

    /// <summary>The name Adobe's StandardEncoding gives a character code, or null when it assigns none.</summary>
    public static string? StandardEncodingName(int code)
    {
        int sid = code switch
        {
            >= 32 and <= 126 => code - 31,
            >= 161 and <= 175 => code - 65,
            >= 177 and <= 180 => code - 66,
            >= 182 and <= 189 => code - 67,
            191 => 123,
            >= 193 and <= 200 => code - 69,
            202 or 203 => code - 70,
            >= 205 and <= 208 => code - 71,
            225 => 138,
            227 => 139,
            >= 232 and <= 235 => code - 92,
            241 => 144,
            245 => 145,
            >= 248 and <= 251 => code - 102,
            _ => 0,
        };
        return sid == 0 ? null : StandardStrings[sid];
    }

    static readonly Dictionary<string, int> CodePoints = BuildCodePoints();

    static Dictionary<string, int> BuildCodePoints()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["space"] = 0x20, ["exclam"] = 0x21, ["quotedbl"] = 0x22, ["numbersign"] = 0x23, ["dollar"] = 0x24, ["percent"] = 0x25,
            ["ampersand"] = 0x26, ["quoteright"] = 0x2019, ["parenleft"] = 0x28, ["parenright"] = 0x29, ["asterisk"] = 0x2A, ["plus"] = 0x2B,
            ["comma"] = 0x2C, ["hyphen"] = 0x2D, ["period"] = 0x2E, ["slash"] = 0x2F, ["zero"] = 0x30, ["one"] = 0x31, ["two"] = 0x32,
            ["three"] = 0x33, ["four"] = 0x34, ["five"] = 0x35, ["six"] = 0x36, ["seven"] = 0x37, ["eight"] = 0x38, ["nine"] = 0x39,
            ["colon"] = 0x3A, ["semicolon"] = 0x3B, ["less"] = 0x3C, ["equal"] = 0x3D, ["greater"] = 0x3E, ["question"] = 0x3F, ["at"] = 0x40,
            ["bracketleft"] = 0x5B, ["backslash"] = 0x5C, ["bracketright"] = 0x5D, ["asciicircum"] = 0x5E, ["underscore"] = 0x5F,
            ["quoteleft"] = 0x2018, ["braceleft"] = 0x7B, ["bar"] = 0x7C, ["braceright"] = 0x7D, ["asciitilde"] = 0x7E,
            ["exclamdown"] = 0xA1, ["cent"] = 0xA2, ["sterling"] = 0xA3, ["fraction"] = 0x2044, ["yen"] = 0xA5, ["florin"] = 0x192,
            ["section"] = 0xA7, ["currency"] = 0xA4, ["quotesingle"] = 0x27, ["quotedblleft"] = 0x201C, ["guillemotleft"] = 0xAB,
            ["guilsinglleft"] = 0x2039, ["guilsinglright"] = 0x203A, ["fi"] = 0xFB01, ["fl"] = 0xFB02, ["endash"] = 0x2013, ["dagger"] = 0x2020,
            ["daggerdbl"] = 0x2021, ["periodcentered"] = 0xB7, ["paragraph"] = 0xB6, ["bullet"] = 0x2022, ["quotesinglbase"] = 0x201A,
            ["quotedblbase"] = 0x201E, ["quotedblright"] = 0x201D, ["guillemotright"] = 0xBB, ["ellipsis"] = 0x2026, ["perthousand"] = 0x2030,
            ["questiondown"] = 0xBF, ["grave"] = 0x60, ["acute"] = 0xB4, ["circumflex"] = 0x2C6, ["tilde"] = 0x2DC, ["macron"] = 0xAF,
            ["breve"] = 0x2D8, ["dotaccent"] = 0x2D9, ["dieresis"] = 0xA8, ["ring"] = 0x2DA, ["cedilla"] = 0xB8, ["hungarumlaut"] = 0x2DD,
            ["ogonek"] = 0x2DB, ["caron"] = 0x2C7, ["emdash"] = 0x2014, ["AE"] = 0xC6, ["ordfeminine"] = 0xAA, ["Lslash"] = 0x141,
            ["Oslash"] = 0xD8, ["OE"] = 0x152, ["ordmasculine"] = 0xBA, ["ae"] = 0xE6, ["dotlessi"] = 0x131, ["lslash"] = 0x142,
            ["oslash"] = 0xF8, ["oe"] = 0x153, ["germandbls"] = 0xDF, ["onesuperior"] = 0xB9, ["logicalnot"] = 0xAC, ["mu"] = 0xB5,
            ["trademark"] = 0x2122, ["Eth"] = 0xD0, ["onehalf"] = 0xBD, ["plusminus"] = 0xB1, ["Thorn"] = 0xDE, ["onequarter"] = 0xBC,
            ["divide"] = 0xF7, ["brokenbar"] = 0xA6, ["degree"] = 0xB0, ["thorn"] = 0xFE, ["threequarters"] = 0xBE, ["twosuperior"] = 0xB2,
            ["registered"] = 0xAE, ["minus"] = 0x2212, ["eth"] = 0xF0, ["multiply"] = 0xD7, ["threesuperior"] = 0xB3, ["copyright"] = 0xA9,
            ["Aacute"] = 0xC1, ["Acircumflex"] = 0xC2, ["Adieresis"] = 0xC4, ["Agrave"] = 0xC0, ["Aring"] = 0xC5, ["Atilde"] = 0xC3,
            ["Ccedilla"] = 0xC7, ["Eacute"] = 0xC9, ["Ecircumflex"] = 0xCA, ["Edieresis"] = 0xCB, ["Egrave"] = 0xC8, ["Iacute"] = 0xCD,
            ["Icircumflex"] = 0xCE, ["Idieresis"] = 0xCF, ["Igrave"] = 0xCC, ["Ntilde"] = 0xD1, ["Oacute"] = 0xD3, ["Ocircumflex"] = 0xD4,
            ["Odieresis"] = 0xD6, ["Ograve"] = 0xD2, ["Otilde"] = 0xD5, ["Scaron"] = 0x160, ["Uacute"] = 0xDA, ["Ucircumflex"] = 0xDB,
            ["Udieresis"] = 0xDC, ["Ugrave"] = 0xD9, ["Yacute"] = 0xDD, ["Ydieresis"] = 0x178, ["Zcaron"] = 0x17D, ["aacute"] = 0xE1,
            ["acircumflex"] = 0xE2, ["adieresis"] = 0xE4, ["agrave"] = 0xE0, ["aring"] = 0xE5, ["atilde"] = 0xE3, ["ccedilla"] = 0xE7,
            ["eacute"] = 0xE9, ["ecircumflex"] = 0xEA, ["edieresis"] = 0xEB, ["egrave"] = 0xE8, ["iacute"] = 0xED, ["icircumflex"] = 0xEE,
            ["idieresis"] = 0xEF, ["igrave"] = 0xEC, ["ntilde"] = 0xF1, ["oacute"] = 0xF3, ["ocircumflex"] = 0xF4, ["odieresis"] = 0xF6,
            ["ograve"] = 0xF2, ["otilde"] = 0xF5, ["scaron"] = 0x161, ["uacute"] = 0xFA, ["ucircumflex"] = 0xFB, ["udieresis"] = 0xFC,
            ["ugrave"] = 0xF9, ["yacute"] = 0xFD, ["ydieresis"] = 0xFF, ["zcaron"] = 0x17E,
            ["Euro"] = 0x20AC, ["nbspace"] = 0xA0, ["nonbreakingspace"] = 0xA0, ["sfthyphen"] = 0xAD, ["softhyphen"] = 0xAD,
        };
        return d;
    }

    /// <summary>
    /// The Unicode text a glyph name stands for: a standard name, <c>uniXXXX</c> (one or more groups of four hex digits), <c>uXXXX</c> to
    /// <c>uXXXXXX</c>, a one-letter or one-digit name, or a ligature written as underscore-joined names (<c>f_f_i</c>); null when unknown.
    /// A name's suffix after a period (<c>a.sc</c>) is ignored.
    /// </summary>
    public static string? ToText(string name)
    {
        if (name.Length == 0) return null;
        int dot = name.IndexOf('.');
        if (dot == 0) return null;                 // .notdef and friends
        if (dot > 0) name = name[..dot];
        if (CodePoints.TryGetValue(name, out int cp)) return char.ConvertFromUtf32(cp);
        if (name.Length == 1) return name;
        if (name.Contains('_'))
        {
            var parts = name.Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                var piece = ToText(part);
                if (piece is null) return null;
                sb.Append(piece);
            }
            return sb.ToString();
        }
        if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length >= 7 && (name.Length - 3) % 4 == 0)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 3; i < name.Length; i += 4)
            {
                if (!int.TryParse(name.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u) || u is >= 0xD800 and <= 0xDFFF) return null;
                sb.Append((char)u);
            }
            return sb.ToString();
        }
        if (name[0] == 'u' && name.Length is >= 5 and <= 7 && int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cpv)
            && cpv is <= 0x10FFFF and (< 0xD800 or > 0xDFFF))
            return char.ConvertFromUtf32(cpv);
        return null;
    }
}
