using System;
using System.Collections.Generic;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Chooses the installed font family that stands in for a font a PDF names but does not embed: the standard 14 and the families
/// they are commonly aliased to first, then the named family itself, then a generic family from the font descriptor's flags.
/// </summary>
internal static class PdfFontSubstitution
{
    // FontDescriptor /Flags bits (ISO 32000-1 table 123).
    const int FixedPitch = 1, Serif = 2, Italic = 64, ForceBold = 1 << 18;

    /// <summary>Families that exist on most Windows, Apple, Android and Linux systems, tried last when nothing better was found.</summary>
    internal static readonly string[] CommonFamilies =
        ["Arial", "Segoe UI", "Helvetica", "Helvetica Neue", "Roboto", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Verdana", "Tahoma"];

    /// <summary>
    /// Installed families that carry the ideographs of a CJK character collection, best first, or null for another collection. A
    /// family the font names itself (MS-Mincho, SimSun...) leads the list when it is one of the usual ones.
    /// </summary>
    internal static string[]? CjkFamilies(string? ordering, string baseName)
    {
        string[] list = ordering switch
        {
            "Japan1" => ["Yu Gothic", "Meiryo", "MS Gothic", "MS Mincho", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP", "Noto Sans JP", "Source Han Sans", "IPAexGothic", "TakaoGothic", "VL Gothic"],
            "GB1" => ["Microsoft YaHei", "SimSun", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "STHeiti"],
            "CNS1" => ["Microsoft JhengHei", "PMingLiU", "MingLiU", "PingFang TC", "Noto Sans CJK TC", "Noto Sans TC", "Source Han Sans TC", "WenQuanYi Micro Hei"],
            "Korea1" => ["Malgun Gothic", "Batang", "Gulim", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "Source Han Sans K", "NanumGothic"],
            _ => null!,
        };
        if (list is null) return null;
        string name = StripSubsetTag(baseName);
        string key = Key(name);
        string? named = key switch
        {
            _ when key.StartsWith("msmincho") => "MS Mincho",
            _ when key.StartsWith("msgothic") => "MS Gothic",
            _ when key.StartsWith("simsun") => "SimSun",
            _ when key.StartsWith("simhei") => "SimHei",
            _ when key.StartsWith("mingliu") || key.StartsWith("msung") => "MingLiU",
            _ when key.StartsWith("batang") => "Batang",
            _ when key.StartsWith("gulim") => "Gulim",
            _ => null,
        };
        return named is null ? list : [named, .. list];
    }

    /// <summary>The family, style and weight to draw a font with.</summary>
    /// <param name="baseName">the <c>/BaseFont</c> name without its slash, possibly with a subset tag and a style suffix</param>
    /// <param name="flags">the descriptor's <c>/Flags</c>, or 0</param>
    /// <param name="fontWeight">the descriptor's <c>/FontWeight</c>, or 0</param>
    /// <param name="exists">whether a family name is installed or registered</param>
    public static (string Family, PaintFontStyle Style, double? Weight) Choose(string baseName, int flags, double fontWeight, Func<string, bool> exists)
    {
        string name = StripSubsetTag(baseName);
        string lower = name.ToLowerInvariant();
        bool bold = lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy") || (flags & ForceBold) != 0 || fontWeight >= 600;
        bool italic = lower.Contains("italic") || lower.Contains("oblique") || (flags & Italic) != 0;

        string key = Key(name);
        var candidates = new List<string>();
        string generic;
        if (key.StartsWith("arial") || key.StartsWith("helvetica"))
        {
            candidates.AddRange(["Arial", "Helvetica", "Liberation Sans", "Arimo", "DejaVu Sans"]);
            generic = "sans-serif";
        }
        else if (key.StartsWith("timesnewroman") || key.StartsWith("times"))
        {
            candidates.AddRange(["Times New Roman", "Times", "Liberation Serif", "Tinos", "DejaVu Serif"]);
            generic = "serif";
        }
        else if (key.StartsWith("couriernew") || key.StartsWith("courier"))
        {
            candidates.AddRange(["Courier New", "Courier", "Liberation Mono", "Cousine", "DejaVu Sans Mono"]);
            generic = "monospace";
        }
        else if (key.StartsWith("symbol"))
        {
            // The Symbol font itself keys its glyphs by private-use codes, so a font with the real Greek letters comes first.
            candidates.AddRange(["Cambria Math", "Segoe UI Symbol", "DejaVu Sans", "Noto Sans", "Arial", "Symbol"]);
            generic = "sans-serif";
        }
        else if (key.StartsWith("zapfdingbats"))
        {
            candidates.AddRange(["Zapf Dingbats", "ITC Zapf Dingbats", "Dingbats", "Noto Sans Symbols 2", "DejaVu Sans"]);
            generic = "sans-serif";
        }
        else
        {
            candidates.Add(Family(name));
            generic = (flags & FixedPitch) != 0 ? "monospace" : (flags & Serif) != 0 ? "serif" : "sans-serif";
        }

        string family = generic;
        foreach (var candidate in candidates)
            if (candidate.Length > 0 && exists(candidate)) { family = candidate; break; }

        return (family, (bold ? PaintFontStyle.Bold : PaintFontStyle.Regular) | (italic ? PaintFontStyle.Italic : PaintFontStyle.Regular), bold ? 700 : 400);
    }

    /// <summary>
    /// The text of a code in the built-in encoding of the standard Symbol font (Greek letters, the arithmetic signs of the first column), which
    /// a file may use with no encoding or ToUnicode map of its own; null for a code this table does not cover.
    /// </summary>
    internal static string? SymbolText(int code)
    {
        const string Upper = "ΑΒΧΔΕΦΓΗΙϑΚΛΜΝΟΠΘΡΣΤΥςΩΞΨΖ";       // 0x41 A .. 0x5A Z
        const string Lower = "αβχδεφγηιϕκλμνοπθρστυϖωξψζ";       // 0x61 a .. 0x7A z
        if (code is >= 0x41 and <= 0x5A) return Upper[code - 0x41].ToString();
        if (code is >= 0x61 and <= 0x7A) return Lower[code - 0x61].ToString();
        return code switch
        {
            0x20 => " ",
            0x21 => "!", 0x23 => "#", 0x25 => "%", 0x26 => "&", 0x28 => "(", 0x29 => ")", 0x2B => "+", 0x2C => ",", 0x2E => ".", 0x2F => "/",
            >= 0x30 and <= 0x39 => ((char)code).ToString(),
            0x3A => ":", 0x3B => ";", 0x3C => "<", 0x3D => "=", 0x3E => ">", 0x3F => "?",
            0x22 => "∀", 0x24 => "∃", 0x27 => "∋", 0x2A => "∗", 0x2D => "−", 0x40 => "≅",
            0x5B => "[", 0x5D => "]", 0x5C => "∴", 0x5E => "⊥", 0x5F => "_", 0x7B => "{", 0x7C => "|", 0x7D => "}", 0x7E => "∼",
            0xB1 => "±", 0xB4 => "×", 0xB8 => "÷", 0xB9 => "≠", 0xBA => "≡", 0xBB => "≈", 0xA3 => "≤", 0xB3 => "≥", 0xA5 => "∞",
            0xB6 => "∂", 0xD1 => "∇", 0xD5 => "∏", 0xE5 => "∑", 0xF2 => "∫", 0xD6 => "√", 0xB7 => "•",
            0xAE => "→", 0xAC => "←", 0xAD => "↑", 0xAF => "↓", 0xAB => "↔", 0xDE => "⇒", 0xDC => "⇐", 0xDB => "⇔",
            0xCE => "∈", 0xCF => "∉", 0xC7 => "∩", 0xC8 => "∪", 0xCC => "⊂", 0xC9 => "⊃", 0xCD => "⊆", 0xC5 => "⊕", 0xC4 => "⊗", 0xC6 => "∅",
            _ => null,
        };
    }

    /// <summary>The advance, in em, of every code of a Courier font, which the standard 14 fix at 600 units; null for another family.</summary>
    internal static double? FixedAdvanceOf(string baseName) => Key(StripSubsetTag(baseName)).StartsWith("courier") ? 0.6 : null;

    /// <summary>A name without its <c>ABCDEF+</c> subset tag.</summary>
    internal static string StripSubsetTag(string name)
    {
        if (name.Length > 7 && name[6] == '+')
        {
            bool tag = true;
            for (int i = 0; i < 6; i++) tag &= name[i] is >= 'A' and <= 'Z';
            if (tag) return name[7..];
        }
        return name;
    }

    /// <summary>The comparison key of a name: lower case, the style part after a comma or hyphen dropped, spaces and "MT"/"PS" suffixes removed.</summary>
    internal static string Key(string name)
    {
        int cut = name.IndexOfAny([',', '-']);
        string baseOnly = (cut >= 0 ? name[..cut] : name).Replace(" ", "").ToLowerInvariant();
        foreach (var suffix in new[] { "psmt", "mt", "ps" })
            if (baseOnly.EndsWith(suffix) && baseOnly.Length > suffix.Length + 2) { baseOnly = baseOnly[..^suffix.Length]; break; }
        return baseOnly;
    }

    /// <summary>The family a PostScript-style name stands for: "TrebuchetMS-Bold" gives "Trebuchet MS", "SegoeUI,Italic" gives "Segoe UI".</summary>
    internal static string Family(string name)
    {
        int cut = name.IndexOfAny([',', '-']);
        string baseOnly = cut >= 0 ? name[..cut] : name;
        if (baseOnly.EndsWith("PSMT")) baseOnly = baseOnly[..^4];
        else if (baseOnly.EndsWith("MT") && baseOnly.Length > 4) baseOnly = baseOnly[..^2];
        if (baseOnly.Contains(' ')) return baseOnly;
        // Insert a space before each capital that follows a lower-case letter or digit, and between an acronym and the word after it.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < baseOnly.Length; i++)
        {
            char c = baseOnly[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(baseOnly[i - 1]) || char.IsDigit(baseOnly[i - 1])
                || (char.IsUpper(baseOnly[i - 1]) && i + 1 < baseOnly.Length && char.IsLower(baseOnly[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
