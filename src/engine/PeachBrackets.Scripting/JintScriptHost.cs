using PeachBrackets.Scripting.Internal;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting;

/// <summary>
/// An <see cref="IScriptHost"/> that runs a document's JavaScript with the Jint interpreter. Give one to
/// <see cref="HtmlDocumentOptions.ScriptHost"/> (or PeachPDF's <c>PdfGenerateConfig.ScriptHost</c>) to turn scripting on.
/// </summary>
/// <remarks>
/// Each document gets its own JavaScript engine, so one host may be shared by any number of documents, including ones loading at the same time.
/// The scripts see a window, a document and the DOM of the parsed page; timers and animation frames run on a virtual clock until the page settles
/// (see <see cref="JintScriptOptions.MaxVirtualTime"/>). A script that throws is reported through <see cref="JintScriptOptions.Logger"/> and
/// does not stop the document from loading.
/// </remarks>
/// <param name="options">what scripts may do; null for the defaults</param>
public sealed class JintScriptHost(JintScriptOptions? options = null) : IScriptHost
{
    private readonly JintScriptOptions _options = options ?? new JintScriptOptions();

    /// <summary>The options the host runs scripts with.</summary>
    public JintScriptOptions Options => _options;

    /// <inheritdoc />
    public async ValueTask RunAsync(ScriptDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var runtime = new ScriptRuntime(document, _options, cancellationToken);
        document.HostState = runtime;
        await runtime.RunAsync();

        // A script that asked to be called after layout (a post-layout function or a complete listener) keeps the document, and the engine, live.
        document.KeepAlive = runtime.WantsLayoutCallbacks;
    }

    /// <inheritdoc />
    public async ValueTask<bool> LayoutCompletedAsync(ScriptDocument document, ScriptLayout layout, CancellationToken cancellationToken = default) =>
        document.HostState is ScriptRuntime runtime && await runtime.RunPostLayoutAsync(layout);

    /// <inheritdoc />
    public async ValueTask LayoutFinishedAsync(ScriptDocument document, ScriptLayout layout, CancellationToken cancellationToken = default)
    {
        if (document.HostState is ScriptRuntime runtime)
            await runtime.RunCompleteAsync(layout);
    }

    /// <inheritdoc />
    public string? EvaluateFunction(ScriptDocument document, string name, IReadOnlyList<string> arguments) =>
        (document.HostState as ScriptRuntime)?.CallScriptFunction(name, arguments);

    /// <inheritdoc />
    public void DocumentClosed(ScriptDocument document)
    {
        (document.HostState as ScriptRuntime)?.DetachApis();
        (document.HostState as ScriptRuntime)?.DisposeMedia();
        document.HostState = null;
    }
}
