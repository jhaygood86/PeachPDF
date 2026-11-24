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
}
