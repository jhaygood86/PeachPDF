using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.ScriptHosting;

/// <summary>A host that runs nothing: it turns scripting on for the parser and the media features, for a copy of a document the scripts already ran in.</summary>
internal sealed class IdleScriptHost : IScriptHost
{
    public static IdleScriptHost Instance { get; } = new();

    public ValueTask RunAsync(ScriptDocument document, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
