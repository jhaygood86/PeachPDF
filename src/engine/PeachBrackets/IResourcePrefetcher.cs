using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PeachDrawing.Core;

namespace PeachBrackets;

/// <summary>
/// Implemented by a render context that can fetch many resources at once and keep what it fetched for the requests that follow (as
/// <see cref="HostResourceLoader"/> does with its response cache). The engine tells it which images the document is about to ask for, one at a time, as
/// layout reaches them, so they can be fetched side by side first instead of one after another.
/// </summary>
public interface IResourcePrefetcher
{
    /// <summary>Fetches the resources in the background of the caller's wait; a failure is not an error, the document asks again when it needs one.</summary>
    /// <param name="addresses">the absolute addresses of the resources the document is about to load</param>
    /// <returns>a task that completes when every fetch has finished</returns>
    ValueTask PrefetchAsync(IReadOnlyList<Uri> addresses);
}
