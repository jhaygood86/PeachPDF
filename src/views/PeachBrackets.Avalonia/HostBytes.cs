using PeachBrackets;

namespace PeachBrackets.Avalonia;

/// <summary>Reads what an address names through the host's resource loader, so the view's network policy and the engine's shared client, timeouts and cache apply to everything it fetches.</summary>
internal static class HostBytes
{
    /// <summary>
    /// The bytes behind an address: a <c>file:</c> or <c>data:</c> address, or an <c>http</c> or <c>https</c> one when <paramref name="allowNetwork"/> is on.
    /// Null when there is nothing there (or the network is off); throws <see cref="InvalidDataException"/> when there is more than <paramref name="maxBytes"/>.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(Uri address, bool allowNetwork, long maxBytes, CancellationToken cancellation = default)
    {
        var response = await HostResourceLoader.LoadAsync(address, allowNetwork).AsTask().WaitAsync(cancellation);
        if (response?.ResourceStream is not { } stream)
            return null;

        await using (stream)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellation);
                if (read == 0)
                    return buffer.ToArray();

                if (buffer.Length + read > maxBytes)
                    throw new InvalidDataException("The resource is larger than " + maxBytes + " bytes.");

                buffer.Write(chunk, 0, read);
            }
        }
    }
}
