using System.Text;

namespace PeachReader.Core.Tests;

/// <summary>One page of a <see cref="TestDocument"/>: its size, the text it shows and the annotations on it.</summary>
internal sealed record TestPage(int Width, int Height, string Content, params string[] Annotations);

/// <summary>One entry of a <see cref="TestDocument"/>'s outline, pointing at a page, <c>Top</c> points below its top.</summary>
internal sealed record TestOutline(string Title, int Page, double Top = 0, params TestOutline[] Children);

/// <summary>
/// Writes small PDFs by hand for the tests: pages that show text in a font whose glyphs are all 500 units wide (so a character at 10 pt
/// is 5 pt wide on any machine), link annotations, and an outline.
/// </summary>
internal static class TestDocument
{
    /// <summary>A page showing <paramref name="text"/> at 10 pt, its baseline <paramref name="fromTop"/> points below the top of the page.</summary>
    public static string Show(string text, double fromTop = 20, double x = 10, double height = 200) =>
        $"BT /F1 10 Tf {x} {height - fromTop} Td ({text}) Tj ET";

    /// <summary>A link annotation over a rectangle given in PDF user space (y up) that opens a web address.</summary>
    public static string UriLink(double x0, double y0, double x1, double y1, string uri) =>
        $"<< /Type /Annot /Subtype /Link /Rect [{x0} {y0} {x1} {y1}] /Border [0 0 0] /A << /S /URI /URI ({uri}) >> >>";

    /// <summary>A link annotation over a rectangle that goes to <paramref name="page"/>, <paramref name="top"/> points below its top (the page is 200 high).</summary>
    public static string PageLink(double x0, double y0, double x1, double y1, int page, double top, int pageHeight = 200) =>
        $"<< /Type /Annot /Subtype /Link /Rect [{x0} {y0} {x1} {y1}] /Border [0 0 0] /Dest [{PageObject(page)} 0 R /XYZ 0 {pageHeight - top} 0] >>";

    private static int PageObject(int page) => 4 + 2 * page;

    public static byte[] Build(IReadOnlyList<TestPage> pages, IReadOnlyList<TestOutline>? outline = null, string? title = null, string catalogExtras = "")
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new SortedDictionary<int, int>();
        void Obj(int number, string body)
        {
            offsets[number] = sb.Length;
            sb.Append(number).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        int outlineRoot = 4 + 2 * pages.Count;
        var flat = new List<(TestOutline Node, int Number, int Parent, int Prev, int Next, int First, int Last, int Count)>();
        int counter = outlineRoot + 1;
        if (outline is { Count: > 0 }) Number(outline, outlineRoot);

        void Number(IReadOnlyList<TestOutline> siblings, int parent)
        {
            var numbers = siblings.Select(_ => counter++).ToArray();
            for (int i = 0; i < siblings.Count; i++)
            {
                int self = numbers[i];
                int slot = flat.Count;
                flat.Add(default);
                int first = 0, last = 0, count = 0;
                if (siblings[i].Children.Length > 0)
                {
                    int before = counter;
                    Number(siblings[i].Children, self);
                    first = before;
                    last = before + siblings[i].Children.Length - 1;
                    count = siblings[i].Children.Length;
                }

                flat[slot] = (siblings[i], self, parent, i > 0 ? numbers[i - 1] : 0, i + 1 < siblings.Count ? numbers[i + 1] : 0, first, last, count);
            }
        }

        Obj(1, "<< /Type /Catalog /Pages 2 0 R" + (outline is { Count: > 0 } ? $" /Outlines {outlineRoot} 0 R" : "") + " " + catalogExtras + " >>");
        Obj(2, "<< /Type /Pages /Kids [" + string.Join(" ", pages.Select((_, i) => $"{PageObject(i)} 0 R")) + $"] /Count {pages.Count} >>");
        Obj(3, "<< /Type /Font /Subtype /TrueType /BaseFont /Arial /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 /Widths ["
            + string.Join(" ", Enumerable.Repeat("500", 224)) + "] >>");
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            string annots = p.Annotations.Length > 0 ? " /Annots [" + string.Join(" ", p.Annotations) + "]" : "";
            Obj(PageObject(i), $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {p.Width} {p.Height}] /Contents {PageObject(i) + 1} 0 R /Resources << /Font << /F1 3 0 R >> >>{annots} >>");
            Obj(PageObject(i) + 1, $"<< /Length {p.Content.Length} >>\nstream\n{p.Content}\nendstream");
        }

        if (outline is { Count: > 0 })
        {
            var top = flat.Where(f => f.Parent == outlineRoot).ToList();
            Obj(outlineRoot, $"<< /Type /Outlines /First {top[0].Number} 0 R /Last {top[^1].Number} 0 R /Count {flat.Count} >>");
            foreach (var f in flat.OrderBy(f => f.Number))
            {
                string entry = $"<< /Title ({f.Node.Title}) /Parent {f.Parent} 0 R /Dest [{PageObject(f.Node.Page)} 0 R /XYZ 0 {pages[f.Node.Page].Height - f.Node.Top} 0]";
                if (f.Prev != 0) entry += $" /Prev {f.Prev} 0 R";
                if (f.Next != 0) entry += $" /Next {f.Next} 0 R";
                if (f.First != 0) entry += $" /First {f.First} 0 R /Last {f.Last} 0 R /Count {f.Count}";
                Obj(f.Number, entry + " >>");
            }
        }

        int info = 0;
        if (title is not null)
        {
            info = offsets.Keys.Max() + 1;
            Obj(info, $"<< /Title ({title}) >>");
        }

        int size = offsets.Keys.Max() + 1;
        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(size).Append("\n0000000000 65535 f \n");
        for (int n = 1; n < size; n++) sb.Append(offsets[n].ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(size).Append(" /Root 1 0 R").Append(info != 0 ? $" /Info {info} 0 R" : "").Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>A document of three 300 x 200 pages with a two-level outline, a web link and a link to the last page.</summary>
    public static byte[] Sample(string? title = "Sample") => Build(
        [
            new TestPage(300, 200, Show("Introduction to peaches", 30) + " " + Show("Peaches are fruit", 60),
                UriLink(10, 160, 120, 175, "https://example.com/peaches"), PageLink(10, 100, 120, 115, 2, 50, pageHeight: 300)),
            new TestPage(300, 200, Show("Methods and cherries", 30)),
            new TestPage(200, 300, Show("Results: peaches win", 30, height: 300) , PageLink(0, 0, 10, 10, 0, 0)),
        ],
        [
            new TestOutline("Introduction", 0, 0, new TestOutline("Background", 0, 100)),
            new TestOutline("Methods", 1),
            new TestOutline("Results", 2, 40),
        ], title);
}
