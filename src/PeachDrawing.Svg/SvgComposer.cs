using System;
using System.Collections.Generic;

namespace PeachDrawing.Svg;

/// <summary>
/// Makes SVG documents. <see cref="AddPage"/> starts a page - one SVG document - and hands out the <see cref="SvgCanvas"/> to draw it
/// on; the page then saves itself. Like the PDF composer, it works in points with the origin at the top left and y growing downwards.
/// </summary>
/// <example>
/// <code>
/// var context = new SvgRenderContext();
/// using var composer = new SvgComposer(context);
/// var page = composer.AddPage(400, 200);
/// page.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), 20, 20, 200, 100);
/// page.Save(File.Create("out.svg"));
/// </code>
/// </example>
public sealed class SvgComposer : IDisposable
{
    private readonly List<SvgComposerPage> _pages = [];

    /// <summary>Creates a composer drawing with <paramref name="renderContext"/>.</summary>
    /// <param name="renderContext">the context whose fonts and images the pages use</param>
    /// <param name="options">what to write into the documents, or <see langword="null"/> for the defaults</param>
    public SvgComposer(SvgRenderContext renderContext, SvgComposerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        RenderContext = renderContext;
        Options = options ?? new SvgComposerOptions();
    }

    /// <summary>The context the pages draw with.</summary>
    public SvgRenderContext RenderContext { get; }

    /// <summary>What is written into the documents.</summary>
    public SvgComposerOptions Options { get; }

    /// <summary>The pages added so far, in order.</summary>
    public IReadOnlyList<SvgComposerPage> Pages => _pages;

    /// <summary>The number of pages added so far.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Starts a page.</summary>
    /// <param name="widthPoints">the page width, in points</param>
    /// <param name="heightPoints">the page height, in points</param>
    /// <returns>the page, whose <see cref="SvgComposerPage.Canvas"/> is empty</returns>
    public SvgComposerPage AddPage(double widthPoints, double heightPoints)
    {
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "A page needs a positive, finite size.");

        var page = new SvgComposerPage(this, widthPoints, heightPoints);
        _pages.Add(page);
        return page;
    }

    /// <inheritdoc/>
    public void Dispose() => _pages.Clear();
}
