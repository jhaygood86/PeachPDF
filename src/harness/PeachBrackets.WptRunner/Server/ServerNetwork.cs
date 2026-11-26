using PeachBrackets.ScriptHosting;

namespace PeachBrackets.WptRunner.Server;

/// <summary>What a test page's scripts reach the network with: the in-process WPT server, with every method, status and header it answers.</summary>
internal sealed class ServerNetwork(WptServer server) : IScriptNetwork
{
    public ValueTask<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(server.Send(request));
}
