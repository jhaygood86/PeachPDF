using System;
using System.Security.Cryptography;
using System.Threading;

namespace PeachDrawing.PDF.Engine.Internal;

/// <summary>
/// The one place the writer asks for randomness and the current time: document and annotation identifiers, font subset tags,
/// modification dates and the salts and IVs of the AES-256 security handler. Production code gets the real sources; a test can
/// install a <see cref="Source"/> for the current async flow (<see cref="Use"/>) so that the bytes a document is saved as are
/// reproducible and can be compared exactly across a refactor. Nothing here is reachable from the public API.
/// </summary>
internal static class PdfEntropy
{
    private static readonly AsyncLocal<Source?> Current = new();

    /// <summary>A replacement for the real random and clock sources.</summary>
    internal abstract class Source
    {
        /// <summary>Returns <paramref name="count"/> bytes standing in for cryptographic randomness.</summary>
        public abstract byte[] GetBytes(int count);

        /// <summary>Returns the instant standing in for "now" (local time, like <see cref="DateTime.Now"/>).</summary>
        public abstract DateTime Now();

        /// <summary>
        /// Converts an instant to the local time a PDF date is written in (the offset it prints is the date's own, so a machine's time zone
        /// would otherwise show in the file). The default is the machine's local time.
        /// </summary>
        public virtual DateTime ToLocal(DateTimeOffset value) => value.LocalDateTime;
    }

    /// <summary>Installs <paramref name="source"/> until the returned scope is disposed.</summary>
    internal static IDisposable Use(Source source)
    {
        var previous = Current.Value;
        Current.Value = source;
        return new Scope(previous);
    }

    /// <summary>Random bytes (cryptographic by default).</summary>
    internal static byte[] GetBytes(int count) => Current.Value?.GetBytes(count) ?? RandomNumberGenerator.GetBytes(count);

    /// <summary>A new GUID (random by default).</summary>
    internal static Guid NewGuid() => Current.Value is { } source ? new Guid(source.GetBytes(16)) : Guid.NewGuid();

    /// <summary>The current local time.</summary>
    internal static DateTime Now() => Current.Value?.Now() ?? DateTime.Now;

    /// <summary>An instant as the local time a PDF date is written in (<see cref="DateTimeOffset.LocalDateTime"/> by default).</summary>
    internal static DateTime ToLocal(DateTimeOffset value) => Current.Value?.ToLocal(value) ?? value.LocalDateTime;

    /// <summary>The current UTC time.</summary>
    internal static DateTimeOffset UtcNow() => Current.Value is { } source ? new DateTimeOffset(source.Now()).ToUniversalTime() : DateTimeOffset.UtcNow;

    private sealed class Scope(Source? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
