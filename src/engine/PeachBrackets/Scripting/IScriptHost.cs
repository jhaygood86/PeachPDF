using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Calls a function the document's scripts registered for the CSS <c>prince-script(name, ...)</c> content item, while the document is laid out. The call
    /// must complete without waiting. A host that registers none keeps the default, which answers null.
    /// </summary>
    /// <param name="document">the document the scripts ran in, kept live (<see cref="ScriptDocument.KeepAlive"/>)</param>
    /// <param name="name">the function's name</param>
    /// <param name="arguments">the text of each argument, resolved from the content items the style sheet gave</param>
    /// <returns>the text the function returned, or null when there is no function by that name</returns>
    string? EvaluateFunction(ScriptDocument document, string name, IReadOnlyList<string> arguments) => null;

    /// <summary>Called when the document is released: when it is disposed if it was kept alive, or as soon as its scripts have run if not. The host releases whatever it held for it.</summary>
    /// <param name="document">the document</param>
    void DocumentClosed(ScriptDocument document)
    {
    }
}

/// <summary>What a layout of a scripted document came to, as <see cref="IScriptHost.LayoutCompletedAsync"/> and <see cref="IScriptHost.LayoutFinishedAsync"/> are told.</summary>
public sealed class ScriptLayout
{
    private readonly Func<IReadOnlyList<BoxReport>>? _collect;
    private IReadOnlyList<BoxReport>? _pages;
    private Dictionary<ScriptNode, List<BoxReport>>? _byElement;

    internal ScriptLayout(int pageCount, int pass, Func<IReadOnlyList<BoxReport>>? collect = null)
    {
        PageCount = pageCount;
        Pass = pass;
        _collect = collect;
    }

    /// <summary>
    /// The boxes of the laid-out document, one tree per page: each page's root is a <see cref="BoxReportKind.Page"/> box. Made when first read, from the
    /// layout as it is; a later layout makes a new <see cref="ScriptLayout"/>. Empty when the document gave no way to read its boxes.
    /// </summary>
    public IReadOnlyList<BoxReport> Pages => _pages ??= _collect?.Invoke() ?? [];

    /// <summary>The boxes an element made, one for each page it reaches and one for each piece an inline element is split into.</summary>
    /// <param name="element">the element</param>
    /// <returns>its boxes, in page order; empty when it made none (it is not displayed) or is not in the document</returns>
    public IReadOnlyList<BoxReport> GetBoxes(ScriptNode element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_byElement is null)
        {
            _byElement = new Dictionary<ScriptNode, List<BoxReport>>(ReferenceEqualityComparer.Instance as IEqualityComparer<ScriptNode>);
            var pending = new Stack<BoxReport>(Pages.Reverse());
            while (pending.Count > 0)
            {
                var report = pending.Pop();
                if (report.Element is { } owner)
                {
                    if (!_byElement.TryGetValue(owner, out var list))
                        _byElement[owner] = list = [];

                    list.Add(report);
                }

                for (var i = report.Children.Count - 1; i >= 0; i--)
                    pending.Push(report.Children[i]);
            }
        }

        return _byElement.TryGetValue(element, out var found) ? found : [];
    }

    /// <summary>The number of pages the document came to (a screen document has one continuous sheet).</summary>
    public int PageCount { get; }

    /// <summary>Which layout this is, counting from 1.</summary>
    public int Pass { get; }
}
