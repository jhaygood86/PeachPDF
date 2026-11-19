using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachDrawing.PDF.PdfiumRunner;

/// <summary>
/// Expands one of PDFium's hand-written PDF templates (<c>.in</c> files: <c>{{object 1 0}}</c>, <c>{{streamlen}}</c>, <c>{{xref}}</c>...) into a
/// PDF with correct offsets and lengths, as PDFium's own <c>testing/tools/fixup_pdf_template.py</c> does. The bytes are handled as Latin-1 so each
/// byte is one char and lengths and offsets are byte counts.
/// </summary>
internal static partial class PdfTemplate
{
    private static readonly string[] LineEndingOverrides = [".js", ".fragment", ".in", ".xml"];

    [GeneratedRegex(@"^\s*\{\{include\s+(.+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex IncludeLine();

    [GeneratedRegex(@"\{\{object\s+(\d+)\s+(\d+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectToken();

    [GeneratedRegex(@"\{\{startxrefobj\s+(\d+)\s+(\d+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex StartXrefObjToken();

    /// <summary>Expands the template at <paramref name="path"/>, following its <c>{{include}}</c>s.</summary>
    public static byte[] Expand(string path)
    {
        var joined = new StringBuilder();
        Include(path, joined, []);
        return Encoding.Latin1.GetBytes(Process(joined.ToString()));
    }

    private static void Include(string path, StringBuilder output, HashSet<string> visited)
    {
        path = Path.GetFullPath(path);
        if (!visited.Add(path))
            return; // a circular include is ignored

        var overrideEndings = LineEndingOverrides.Contains(Path.GetExtension(path), StringComparer.Ordinal);
        var endsWithNewline = false;
        foreach (var raw in Lines(Encoding.Latin1.GetString(File.ReadAllBytes(path))))
        {
            var line = raw;
            if (IncludeLine().Match(line) is { Success: true } include)
            {
                var name = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(include.Groups[1].Value));
                Include(Path.Combine(Path.GetDirectoryName(path)!, name), output, visited);
                continue;
            }

            if (overrideEndings)
            {
                if (line.EndsWith("\r\n", StringComparison.Ordinal))
                {
                    line = line[..^2] + "\n";
                    endsWithNewline = true;
                }
                else
                {
                    endsWithNewline = line.EndsWith('\n');
                }
            }

            output.Append(line);
        }

        if (!endsWithNewline)
            output.Append('\n'); // an include ends on its own line
        visited.Remove(path);
    }

    /// <summary>Splits on LF, keeping each terminator (Python's iteration over a binary file).</summary>
    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            var stop = end < 0 ? text.Length : end + 1;
            yield return text[start..stop];
            start = stop;
        }
    }

    private static string Process(string text)
    {
        var lines = Lines(text).ToList();

        // First pass: the byte length of every stream that asks for {{streamlen}}.
        var streamLengths = new List<int>();
        var state = StreamLen.Start;
        foreach (var line in lines)
        {
            if (line.Contains("{{streamlen}}", StringComparison.Ordinal))
            {
                state = StreamLen.FindStream;
                streamLengths.Add(0);
                continue;
            }

            if (state == StreamLen.FindStream && line.TrimEnd() == "stream")
            {
                state = StreamLen.FindEndStream;
                continue;
            }

            if (state == StreamLen.FindEndStream)
            {
                if (line.TrimEnd() == "endstream")
                {
                    state = StreamLen.Start;
                    streamLengths[^1]--; // the final newline is not part of the stream
                }
                else
                {
                    streamLengths[^1] += line.Length;
                }
            }
        }

        // Second pass: the other tokens, tracking each line's offset.
        var objects = new Dictionary<int, (int Offset, int Generation)>();
        var maxObject = 0;
        var xrefOffset = 0;
        var offset = 0;
        var nextLength = 0;
        var output = new StringBuilder();
        foreach (var original in lines)
        {
            var line = original;
            if (line.Contains("{{header}}", StringComparison.Ordinal))
                line = line.Replace("{{header}}", "%PDF-1.7\n% ò¤ô", StringComparison.Ordinal);
            if (line.Contains("{{streamlen}}", StringComparison.Ordinal))
                line = line.Replace("{{streamlen}}", $"/Length {streamLengths[nextLength++]}", StringComparison.Ordinal);
            if (line.Contains("{{xref}}", StringComparison.Ordinal))
            {
                xrefOffset = offset;
                var table = new StringBuilder($"xref\n0 {maxObject + 1}\n");
                for (var i = 0; i <= maxObject; i++)
                    table.Append(objects.TryGetValue(i, out var entry) ? $"{entry.Offset:D10} {entry.Generation:D5} n \n" : "0000000000 65535 f \n");
                line = table.ToString();
            }

            if (line.Contains("{{trailer}}", StringComparison.Ordinal))
                line = line.Replace("{{trailer}}", $"trailer <<\n  /Root 1 0 R\n  /Size {maxObject + 1}\n>>", StringComparison.Ordinal);
            if (line.Contains("{{trailersize}}", StringComparison.Ordinal))
                line = line.Replace("{{trailersize}}", $"/Size {maxObject + 1}", StringComparison.Ordinal);
            if (line.Contains("{{startxref}}", StringComparison.Ordinal))
                line = line.Replace("{{startxref}}", $"startxref\n{xrefOffset}", StringComparison.Ordinal);

            if (ObjectToken().Match(line) is { Success: true, Index: 0 } declared)
            {
                var number = int.Parse(declared.Groups[1].Value, CultureInfo.InvariantCulture);
                objects[number] = (offset, int.Parse(declared.Groups[2].Value, CultureInfo.InvariantCulture));
                maxObject = Math.Max(maxObject, number);
            }

            line = ObjectToken().Replace(line, "$1 $2 obj");
            if (StartXrefObjToken().Match(line) is { Success: true, Index: 0 } target)
            {
                var (at, _) = objects[int.Parse(target.Groups[1].Value, CultureInfo.InvariantCulture)];
                line = StartXrefObjToken().Replace(line, $"startxref\n{at}");
            }

            offset += line.Length;
            output.Append(line);
        }

        return output.ToString();
    }

    private enum StreamLen { Start, FindStream, FindEndStream }
}
