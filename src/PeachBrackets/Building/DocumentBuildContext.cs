using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Building;

/// <summary>
/// What a document built in code (rather than parsed from HTML) is built with: the render context its styles resolve against, and the
/// bookkeeping the pages built from it share.
/// </summary>
/// <remarks>
/// Make one per document, then one <see cref="DeclarativePage"/> per page with <see cref="CreatePage"/>. Nodes made through the context must
/// not be mixed with another context's.
/// </remarks>
public sealed class DocumentBuildContext
{
    /// <summary>Starts a build over <paramref name="renderContext"/>.</summary>
    /// <param name="renderContext">the context the nodes' styles are resolved against</param>
    public DocumentBuildContext(RenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        RenderContext = renderContext;
        Properties = new CssPropertyFactory(renderContext);
    }

    /// <summary>The context the nodes' styles are resolved against.</summary>
    public RenderContext RenderContext { get; }

    internal CssPropertyFactory Properties { get; }

    /// <summary>
    /// Starts a page: a root node and the page-level settings that go with it. Whatever an earlier page left behind (the hidden wrappers of
    /// <c>display: contents</c> elements in the HTML fragments it spliced in) is dropped, since it belongs to a document already laid out.
    /// </summary>
    /// <returns>the new page</returns>
    public DeclarativePage CreatePage()
    {
        Properties.DisplayContentsShells.Clear();
        return new DeclarativePage(this, new DocumentNode(CssBox.CreateBlock(), this));
    }
}
