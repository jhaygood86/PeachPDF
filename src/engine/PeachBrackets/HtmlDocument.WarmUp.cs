using PeachDrawing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    private static readonly Lazy<Task> WarmUp = new(() => Task.Run(WarmUpCoreAsync), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Runs a small document through loading, layout and painting on a background thread, so that the code paths a real document takes (the parser, the cascade, the
    /// block, flex and grid layouts, text shaping, painting) are compiled and the fonts of the system are listed before the first document asks for them. A host that
    /// is not ahead-of-time compiled spends most of the first load on the JIT compiler; on a news front page that was about 0.6 s of 1.7 s, and the time this takes
    /// can pass while the document is still being fetched.
    /// </summary>
    /// <remarks>
    /// Safe to call at any time and from any number of places: the work is done once per process, and every call returns the same task. It never throws and nothing
    /// it makes is kept. It uses one thread-pool thread for a few hundred milliseconds; a host that is compiled ahead of time gains little from it.
    /// </remarks>
    /// <returns>a task that completes when the work is done</returns>
    public static Task WarmUpAsync() => WarmUp.Value;

    private const string WarmUpHtml = """
        <!DOCTYPE html><html><head><style>
        body { margin: 0; font: 16px/1.5 sans-serif; color: #222 }
        .row { display: flex; gap: 8px; flex-wrap: wrap } .row > * { flex: 1 1 120px; padding: 4px; border: 1px solid #ccc; border-radius: 6px; background: linear-gradient(#fff, #eee) }
        .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 4px } a { color: #06c; text-decoration: none } a:hover { text-decoration: underline }
        h1, h2 { margin: 0 0 8px } .card { position: relative; overflow: hidden; border-radius: 8px; box-shadow: 0 1px 4px rgba(0,0,0,.3) } .card span { position: absolute; inset: 0 }
        @media (min-width: 600px) { .grid { grid-template-columns: repeat(4, 1fr) } } table { border-collapse: collapse } td, th { border: 1px solid #ccc; padding: 2px 6px }
        </style></head><body>
        <h1>Heading</h1><div class="row"><div>One <b>bold</b> and <i>italic</i> text, with a <a href="#">link</a>.</div><div>Two</div><div>Three</div></div>
        <div class="grid"><div class="card"><span></span><p>Card text that wraps over several lines to exercise line breaking and shaping.</p></div><div class="card">B</div><div class="card">C</div><div class="card">D</div></div>
        <ul><li>One</li><li>Two</li></ul><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
        <p style="text-align:justify">Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>
        <svg width="24" height="24" viewBox="0 0 24 24"><path d="M2 2h20v20H2z" fill="#09f"/></svg></body></html>
        """;

    private static Task WarmUpCoreAsync() => WarmUpDocumentAsync(WarmUpHtml);

    // The work, for a document: a seam for a test to show that a document that cannot be loaded does not escape.
    internal static async Task WarmUpDocumentAsync(string html)
    {
        try
        {
            var context = new RasterRenderContext();
            using var document = await LoadAsync(html, context, new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 720, ViewportHeightPoints = 600 }).ConfigureAwait(false);
            using var measuring = context.CreateCanvas(1, 1);
            await document.LayoutAsync(measuring).ConfigureAwait(false);
            if (document.Pages.Count > 0)
            {
                using var canvas = context.CreateCanvas(200, 200);
                await document.PaintPageAsync(0, canvas).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Warming up is an optimization: whatever went wrong (no fonts, no memory), the first real document does the work itself.
        }
    }
}
