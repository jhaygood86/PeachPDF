using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.ScriptHosting;

/// <summary>
/// Runs the scripts of a document. Set on <see cref="HtmlDocumentOptions.ScriptHost"/> to turn scripting on; a document with no host ignores
/// its <c>&lt;script&gt;</c> elements, as before. The engine does not run JavaScript itself: the <c>PeachBrackets.Scripting</c> package supplies a host.
/// </summary>
/// <remarks>
/// The host is called once for each document source, after the HTML is parsed and before any style sheet is applied. It changes the document through
/// <see cref="ScriptDocument"/>; what the document is afterwards is what is styled, laid out and painted. A host that plays timers or animation
/// frames does so inside <see cref="RunAsync"/> (a document made for a page has no wall clock of its own), so the document is complete when the call returns.
/// </remarks>
public interface IScriptHost
{
    /// <summary>Runs the scripts of a document, in the order the document gives them, changing it as they ask.</summary>
    /// <param name="document">the parsed document</param>
    /// <param name="cancellationToken">cancels the work</param>
    /// <returns>a task that completes when every script has run and the document is ready to be styled</returns>
    ValueTask RunAsync(ScriptDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the document has been laid out, when the host asked to be (<see cref="ScriptDocument.KeepAlive"/>). The host may change the
    /// document again: if it did, the engine lays it out again and calls this once more, up to <see cref="HtmlDocumentOptions.MaxLayoutPasses"/> times.
    /// </summary>
    /// <param name="document">the document the scripts ran in, still live</param>
    /// <param name="layout">what the layout came to</param>
    /// <param name="cancellationToken">cancels the work</param>
    /// <returns>true to be called again after this pass even if the document did not change (the host has more to do); false when it is done</returns>
    ValueTask<bool> LayoutCompletedAsync(ScriptDocument document, ScriptLayout layout, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

    /// <summary>
    /// Called once, after the last layout pass: the document is final and about to be painted. A change the host makes now is not laid out.
    /// </summary>
    /// <param name="document">the document the scripts ran in</param>
    /// <param name="layout">what the final layout came to</param>
    /// <param name="cancellationToken">cancels the work</param>
    /// <returns>a task that completes when the host is done</returns>
    ValueTask LayoutFinishedAsync(ScriptDocument document, ScriptLayout layout, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Called when the document is released: when it is disposed if it was kept alive, or as soon as its scripts have run if not. The host releases whatever it held for it.</summary>
    /// <param name="document">the document</param>
    void DocumentClosed(ScriptDocument document)
    {
    }
}

/// <summary>What a layout of a scripted document came to, as <see cref="IScriptHost.LayoutCompletedAsync"/> and <see cref="IScriptHost.LayoutFinishedAsync"/> are told.</summary>
public sealed class ScriptLayout
{
    internal ScriptLayout(int pageCount, int pass)
    {
        PageCount = pageCount;
        Pass = pass;
    }

    /// <summary>The number of pages the document came to (a screen document has one continuous sheet).</summary>
    public int PageCount { get; }

    /// <summary>Which layout this is, counting from 1.</summary>
    public int Pass { get; }
}
