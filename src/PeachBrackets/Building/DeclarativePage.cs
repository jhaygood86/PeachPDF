using PeachBrackets.Css;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Building;

/// <summary>Which of a declarative page's geometry settings the builder chose explicitly, so a style sheet's <c>@page</c> rule does not override them.</summary>
[Flags]
public enum PageGeometryOverrides
{
    /// <summary>Nothing was set explicitly; an <c>@page</c> rule may change any of it.</summary>
    None = 0,

    /// <summary>The page size was set explicitly.</summary>
    Size = 1,

    /// <summary>The top margin was set explicitly.</summary>
    MarginTop = 2,

    /// <summary>The right margin was set explicitly.</summary>
    MarginRight = 4,

    /// <summary>The bottom margin was set explicitly.</summary>
    MarginBottom = 8,

    /// <summary>The left margin was set explicitly.</summary>
    MarginLeft = 16,
}

/// <summary>
/// One page of a document built in code: the tree of nodes that is its content, and the page-level settings that go with it (the content
/// repeated in the page margins, and which geometry the builder fixed).
/// </summary>
/// <remarks>Make one with <see cref="DocumentBuildContext.CreatePage"/> and turn it into a laid-out document with <see cref="HtmlDocument.CreateAsync"/>.</remarks>
public sealed class DeclarativePage
{
    private readonly List<(string Selector, string Content)> _marginBoxes = [];

    internal DeclarativePage(DocumentBuildContext context, DocumentNode root)
    {
        Context = context;
        Root = root;
    }

    internal DocumentBuildContext Context { get; }

    /// <summary>The root of the page's content: a block box that has no parent.</summary>
    public DocumentNode Root { get; }

    /// <summary>Which geometry the builder set explicitly; a style sheet's <c>@page</c> rule leaves those alone. None by default.</summary>
    public PageGeometryOverrides Overrides { get; set; }

    /// <summary>
    /// Puts content into one of the page's margin boxes on every page, as an <c>@page</c> rule's <c>@top-center</c> and the like do.
    /// </summary>
    /// <param name="marginBox">the margin box's name, such as <c>top-center</c> or <c>bottom-right</c></param>
    /// <param name="content">the box's <c>content</c> value, such as <c>element(header)</c></param>
    public void AddMarginBox(string marginBox, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(marginBox);
        ArgumentNullException.ThrowIfNull(content);

        _marginBoxes.Add((marginBox, content));
    }

    internal IReadOnlyList<PageRule> BuildPageRules()
    {
        if (_marginBoxes.Count == 0)
            return [];

        // One shared, unconditional (no selector) PageRule for the whole page - PageRuleResolver's own rule selection treats a selector-less PageRule
        // as "the" base rule, keeping only the last one seen, so a second one per margin box would silently discard the first's.
        var pageRule = new PageRule(new StylesheetParser());
        foreach (var (selector, content) in _marginBoxes)
        {
            var marginRule = new MarginStyleRule(pageRule.Parser) { SelectorText = selector };
            marginRule.Style.Content = content;
            pageRule.AppendChild(marginRule);
        }

        return [pageRule];
    }
}
