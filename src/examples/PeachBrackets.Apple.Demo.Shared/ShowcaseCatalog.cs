using System.Text.Json;

namespace PeachBrackets.Apple.Example;

/// <summary>One document the demo can show.</summary>
/// <param name="Slug">the short name of the showcase</param>
/// <param name="Category">the group it belongs to in the list</param>
/// <param name="Title">what the list calls it</param>
/// <param name="Description">what it demonstrates</param>
/// <param name="Path">where its HTML is, or the address of a web page</param>
/// <param name="Inline">the document itself, for the one built into the demo</param>
internal sealed record Showcase(string Slug, string Category, string Title, string Description, string Path, string? Inline = null)
{
    /// <summary>Whether the document is a web address to fetch rather than a file.</summary>
    public bool IsWeb => Path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    /// <summary>The HTML of a document that is not on the web.</summary>
    public string ReadHtml() => Inline ?? File.ReadAllText(Path);
}

/// <summary>
/// The documents of the demo, the same as the Windows demo shows: the sample page that has a control of every kind, the screen-only showcases that ship with the demo, every
/// showcase of the PDF test harness (the build writes them as HTML beside the program, so the demo keeps no copy), and three pages fetched live from the web.
/// </summary>
internal static class ShowcaseCatalog
{
    /// <param name="root">the folder holding <c>screen</c> and <c>showcases</c></param>
    public static IReadOnlyList<Showcase> Load(string root)
    {
        var showcases = new List<Showcase>
        {
            new("sample", "Demo", "Sample page", "A page with a link, a hover card, text that cannot be selected, a touch-action area and a form with a control of every kind.", string.Empty, SamplePage.Html),
        };
        AddFrom(showcases, System.IO.Path.Combine(root, "screen"), "screen-showcases.json");
        AddFrom(showcases, System.IO.Path.Combine(root, "showcases"), "showcases.json");
        showcases.AddRange(
        [
            new("web_peachpdf", "Web", "peachpdf.net", "The PeachPDF site, fetched live and laid out by the view (needs a network connection; turns AllowNetworkAccess on so its images, styles and fonts load).", "https://peachpdf.net/"),
            new("web_peachstate", "Web", "peachstatetechnologies.com", "The Peach State Technologies site, fetched live and laid out by the view (needs a network connection; turns AllowNetworkAccess on).", "https://peachstatetechnologies.com/"),
            new("web_arstechnica", "Web", "arstechnica.com", "Ars Technica, a long server-rendered news front page with many lazy-loaded images, fetched live and laid out by the view (needs a network connection; turns AllowNetworkAccess on).", "https://arstechnica.com/"),
        ]);
        return showcases;
    }

    private static void AddFrom(List<Showcase> into, string directory, string manifest)
    {
        var file = System.IO.Path.Combine(directory, manifest);
        if (!File.Exists(file))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var html = System.IO.Path.Combine(directory, entry.GetProperty("html").GetString()!);
            if (!File.Exists(html))
                continue;

            into.Add(new Showcase(
                entry.GetProperty("slug").GetString()!,
                entry.GetProperty("category").GetString()!,
                entry.GetProperty("title").GetString()!,
                entry.GetProperty("description").GetString()!,
                html));
        }
    }
}
