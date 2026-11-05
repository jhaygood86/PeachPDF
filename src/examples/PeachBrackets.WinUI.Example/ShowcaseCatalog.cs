using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PeachBrackets.WinUI.Example;

/// <summary>One document the demo can show.</summary>
/// <param name="Slug">the short name of the showcase</param>
/// <param name="Category">the group it belongs to in the list</param>
/// <param name="Title">what the list calls it</param>
/// <param name="Description">what it demonstrates</param>
/// <param name="Path">where its HTML is</param>
internal sealed record Showcase(string Slug, string Category, string Title, string Description, string Path)
{
    /// <summary>Whether the document is a web address to fetch rather than a file.</summary>
    public bool IsWeb => Path.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The documents of the demo: the screen-only ones that ship with it, and every showcase of the PDF test harness, which the build writes as
/// HTML beside the program (<c>--html-only</c>) so the demo never keeps a copy of them.
/// </summary>
internal static class ShowcaseCatalog
{
    public static IReadOnlyList<Showcase> Load()
    {
        var showcases = new List<Showcase>();
        AddFrom(showcases, System.IO.Path.Combine(AppContext.BaseDirectory, "screen"), "screen-showcases.json");
        AddFrom(showcases, System.IO.Path.Combine(AppContext.BaseDirectory, "showcases"), "showcases.json");
        showcases.AddRange(new Showcase[]
        {
            new("web_peachpdf", "Web", "peachpdf.net", "The PeachPDF site, fetched live and laid out by the control (needs a network connection; turns AllowNetworkAccess on so its images, styles and fonts load).", "https://peachpdf.net/"),
            new("web_peachstate", "Web", "peachstatetechnologies.com", "The Peach State Technologies site, fetched live and laid out by the control (needs a network connection; turns AllowNetworkAccess on).", "https://peachstatetechnologies.com/"),
            new("web_arstechnica", "Web", "arstechnica.com", "Ars Technica, a long server-rendered news front page with many lazy-loaded images, fetched live and laid out by the control (needs a network connection; turns AllowNetworkAccess on).", "https://arstechnica.com/"),
        });
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
