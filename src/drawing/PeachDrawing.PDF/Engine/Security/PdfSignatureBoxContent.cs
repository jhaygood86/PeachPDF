using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PeachDrawing.PDF.Engine.Security;

/// <summary>
/// The content of a visible signature's appearance stream, as plain ASCII PDF syntax so the composer's objects and the objects appended to an
/// existing file are made the same way: a border, and lines of text in the standard Helvetica font.
/// </summary>
internal static class PdfSignatureBoxContent
{
    /// <summary>The font object every visible signature refers to as <c>/Helv</c>: a standard font, so nothing is embedded.</summary>
    public const string FontObjectText = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    /// <summary>The lines the box shows: the options' own text, or the signer, reason, place and time.</summary>
    public static string[] Lines(PdfSigningOptions options, DateTimeOffset now)
    {
        if (options.Box?.Text is { } text)
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var lines = new List<string>
        {
            "Digitally signed by " + (options.SignerName ?? options.Certificate!.GetNameInfo(X509NameType.SimpleName, forIssuer: false)),
        };
        if (!string.IsNullOrEmpty(options.Reason)) lines.Add("Reason: " + options.Reason);
        if (!string.IsNullOrEmpty(options.Location)) lines.Add("Location: " + options.Location);
        lines.Add("Date: " + now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
        return [.. lines];
    }

    /// <summary>The content stream for a box <paramref name="width"/> by <paramref name="height"/> points.</summary>
    public static string Content(double width, double height, IReadOnlyList<string> lines, double fontSize)
    {
        static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        var text = new StringBuilder();
        text.Append("q\n");
        text.Append("0.5 w 0 G ").Append(N(0.25)).Append(' ').Append(N(0.25)).Append(' ').Append(N(width - 0.5)).Append(' ').Append(N(height - 0.5)).Append(" re S\n");
        text.Append("2 2 ").Append(N(width - 4)).Append(' ').Append(N(height - 4)).Append(" re W n\n");     // the text is clipped to the box
        text.Append("BT /Helv ").Append(N(fontSize)).Append(" Tf 0 g ").Append(N(fontSize * 1.15)).Append(" TL ");
        text.Append("3 ").Append(N(height - 2 - fontSize)).Append(" Td\n");
        foreach (var line in lines)
            text.Append('(').Append(Escape(line)).Append(") Tj T*\n");
        text.Append("ET\nQ\n");
        return text.ToString();
    }

    /// <summary>A string for a PDF literal: Latin-1, with the delimiters and anything outside printable ASCII escaped, so the result is ASCII.</summary>
    static string Escape(string line)
    {
        var escaped = new StringBuilder(line.Length);
        foreach (var ch in line)
        {
            var c = ch > 0xFF ? '?' : ch;
            if (c is '(' or ')' or '\\')
                escaped.Append('\\').Append(c);
            else if (c < 32 || c > 126)
                escaped.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
            else
                escaped.Append(c);
        }

        return escaped.ToString();
    }
}
