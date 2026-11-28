using PeachDrawing.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PeachBrackets;

/// <summary>The bytes a <c>blob:</c> address stands for, and the media type they were made with.</summary>
/// <param name="Bytes">the content</param>
/// <param name="ContentType">the media type, or an empty string</param>
public sealed record BlobContent(byte[] Bytes, string ContentType);

/// <summary>
/// The <c>blob:</c> addresses a document's script made with <c>URL.createObjectURL</c>, and what they stand for. A resource loader answers a <c>blob:</c> address from it the way it answers a <c>data:</c> one,
/// so an image, a style sheet, a font or a script loads from an object URL like from any other address; <see cref="HostResourceLoader"/> does, and so does the loader PeachPDF puts ahead of the one it was given.
/// </summary>
/// <remarks>
/// An address is kept for as long as the script that made it is alive and has not revoked it. The content is read when something asks for it, so the store holds no copy of it, and it does not keep the script alive.
/// </remarks>
public static class BlobUrlStore
{
    private sealed record Entry(WeakReference<object> Owner, Func<object, string, BlobContent?> Read);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    /// <summary>Makes an address resolve to content.</summary>
    /// <param name="url">the <c>blob:</c> address</param>
    /// <param name="owner">what made the address; once it has been collected the address resolves to nothing</param>
    /// <param name="read">reads the content of the address (given the owner and the address); must not hold the owner itself</param>
    public static void Register(string url, object owner, Func<object, string, BlobContent?> read)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(read);

        foreach (var (key, entry) in Entries)
        {
            if (!entry.Owner.TryGetTarget(out _))
                Entries.TryRemove(key, out _);
        }

        Entries[url] = new Entry(new WeakReference<object>(owner), read);
    }

    /// <summary>Makes an address resolve to nothing (<c>URL.revokeObjectURL</c>).</summary>
    /// <param name="url">the <c>blob:</c> address</param>
    public static void Revoke(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Entries.TryRemove(url, out _);
    }

    /// <summary>The content an address stands for, or null when it was never made, was revoked, or its script is gone.</summary>
    /// <param name="url">the <c>blob:</c> address</param>
    public static BlobContent? TryRead(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Entries.TryGetValue(url, out var entry) && entry.Owner.TryGetTarget(out var owner) ? entry.Read(owner, url) : null;
    }

    /// <summary>Answers a request for a <c>blob:</c> address the way a resource loader does: the content, with its media type as <c>Content-Type</c>.</summary>
    /// <param name="uri">the address</param>
    /// <returns>the response, or null when the address stands for nothing</returns>
    public static RNetworkResponse? Load(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals("blob", StringComparison.OrdinalIgnoreCase))
            return null;

        var content = TryRead(uri.OriginalString) ?? TryRead(uri.AbsoluteUri);
        if (content is null)
            return null;

        var headers = new Dictionary<string, string[]> { ["Content-Type"] = [content.ContentType] };
        return new RNetworkResponse(new MemoryStream(content.Bytes), headers);
    }

    /// <summary>The same as <see cref="Load"/>, as the task a loader returns.</summary>
    /// <param name="uri">the address</param>
    public static ValueTask<RNetworkResponse?> LoadAsync(Uri uri) => new(Load(uri));
}
