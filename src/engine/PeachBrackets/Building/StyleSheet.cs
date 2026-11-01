using PeachBrackets.Html;
using PeachBrackets.Html.Parse;
using PeachDrawing.Core;
using System;
using System.Threading.Tasks;

namespace PeachBrackets.Building;

/// <summary>
/// A parsed CSS style sheet, ready to apply to a document or to an HTML fragment spliced into one.
/// </summary>
/// <remarks>
/// A style sheet can be built from the built-in defaults plus your rules (the usual case) or from your rules alone, and rules can be added after
/// parsing. It belongs to the <see cref="RenderContext"/> it was parsed with: the context resolves the fonts and resources the rules name.
/// </remarks>
public sealed class StyleSheet
{
    private readonly RenderContext _renderContext;

    private StyleSheet(CssData data, RenderContext renderContext)
    {
        Data = data;
        _renderContext = renderContext;
    }

    internal CssData Data { get; }

    /// <summary>
    /// Parses <paramref name="css"/> into a style sheet.
    /// </summary>
    /// <param name="renderContext">the context that resolves the fonts and resources the rules name</param>
    /// <param name="css">the CSS text</param>
    /// <param name="combineWithDefault">whether the built-in defaults (the user-agent style sheet) are included, so the rules layer over them</param>
    /// <returns>the parsed style sheet</returns>
    public static async Task<StyleSheet> ParseAsync(RenderContext renderContext, string css, bool combineWithDefault = true)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        ArgumentNullException.ThrowIfNull(css);

        return new StyleSheet(await CssData.Parse(renderContext, css, combineWithDefault), renderContext);
    }

    /// <inheritdoc cref="ParseAsync(RenderContext, string, bool)"/>
    public static async Task<StyleSheet> ParseAsync(RenderContext renderContext, ReadOnlyMemory<char> css, bool combineWithDefault = true)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        return new StyleSheet(await CssData.Parse(renderContext, css, combineWithDefault), renderContext);
    }

    /// <summary>
    /// Adds rules to the style sheet, after the ones it already has.
    /// </summary>
    /// <param name="css">the CSS text; empty text adds nothing</param>
    /// <returns>a task that completes when the rules are added</returns>
    public async Task AddAsync(string css)
    {
        if (string.IsNullOrEmpty(css))
            return;

        var parser = new CssParser(_renderContext, null);
        await parser.ParseStyleSheet(Data, css);

        // The selector index is built lazily and cached; adding rules after it has been queried would otherwise leave them invisible to matching.
        Data.InvalidateIndex();
    }

    /// <inheritdoc cref="AddAsync(string)"/>
    public async Task AddAsync(ReadOnlyMemory<char> css)
    {
        if (css.IsEmpty)
            return;

        var parser = new CssParser(_renderContext, null);
        await parser.ParseStyleSheet(Data, css);
        Data.InvalidateIndex();
    }
}
