using AngleSharp;
using AngleSharp.Html.Parser;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.WinUI.Example;

/// <summary>
/// Prepares a page fetched from the web for the control, which runs no scripts: it gives the page the base address its relative addresses are
/// resolved against and the classes its own startup script would have put on the root element (a theme, for one), so the page is laid out the way
/// a browser would have shown it after that script ran.
/// </summary>
internal static class PageRewriter
{
    /// <summary>
    /// The page with <c>&lt;base href&gt;</c> as the first thing in its head (unless it has one of its own) and <paramref name="rootClasses"/>
    /// added to the class list of <c>&lt;html&gt;</c>. The page is parsed as a browser with scripting turned off parses it, so the content of
    /// <c>&lt;noscript&gt;</c> is markup, which is how the control renders it. Nothing else is changed.
    /// </summary>
    /// <param name="html">the page as it was fetched</param>
    /// <param name="baseHref">the address the page was fetched from</param>
    /// <param name="rootClasses">the classes to add to <c>&lt;html&gt;</c>; empty ones are ignored and repeats are added once</param>
    public static string Rewrite(string html, string baseHref, string[] rootClasses)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrEmpty(baseHref);
        ArgumentNullException.ThrowIfNull(rootClasses);

        var parser = new HtmlParser(new HtmlParserOptions { IsScripting = false });
        using var document = parser.ParseDocument(html);

        if (document.DocumentElement is { } root)
        {
            foreach (var name in rootClasses.Where(c => !string.IsNullOrWhiteSpace(c)))
                root.ClassList.Add(name.Trim());
        }

        if (document.Head is { } head && document.QuerySelector("base[href]") is null)
        {
            var baseElement = document.CreateElement("base");
            baseElement.SetAttribute("href", baseHref);
            head.InsertBefore(baseElement, head.FirstChild);
        }

        return document.ToHtml();
    }
}

/// <summary>What a site's own startup script puts on the root element, for the sites the demo opens.</summary>
internal static class WebPageTraits
{
    /// <summary>
    /// The classes to add to <c>&lt;html&gt;</c> for the site of a showcase.
    /// </summary>
    /// <param name="slug">the showcase</param>
    /// <param name="dark">whether the demo is showing its dark theme</param>
    public static string[] RootClasses(string slug, bool dark) => slug switch
    {
        // Ars Technica picks "dark" or "dusk" (its light theme: the page is not white but a pale grey) from prefers-color-scheme, and records the
        // reader's text settings (size, link style, column width, where the settings sit) the same way. Its style sheet keys much of its colour and
        // type on these classes, so without them the page is laid out as no reader ever sees it.
        "web_arstechnica" =>
        [
            dark ? "dark" : "dusk",
            "text-settings-size-standard",
            "text-settings-links-standard",
            "text-settings-width-standard",
            "text-settings-position-story",
        ],
        _ => [],
    };
}
