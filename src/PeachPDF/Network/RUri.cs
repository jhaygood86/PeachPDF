#nullable enable
using System;

namespace PeachPDF.Network
{
    /// <summary>
    /// Wraps <see cref="System.Uri"/> for use throughout PeachPDF's resource-loading pipeline, adding
    /// special-case handling for <c>data:</c> URIs and helpers for resolving a relative URI against a
    /// base URI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Before .NET 10</b>, a <c>data:</c> URI is deliberately never handed to
    /// <see cref="System.Uri"/>. Two independent reasons, both measured on net8.0: it cannot
    /// round-trip one correctly through <see cref="AbsoluteUri"/>, and it rejects any URI string
    /// longer than 65,519 characters ("Invalid URI: The Uri string is too long."). That ceiling is
    /// roughly 48 KB of payload once base64 expansion is accounted for, which real embedded logos,
    /// photographs and charts exceed routinely; the <see cref="UriFormatException"/> is caught upstream
    /// and turned into a skipped resource, so the only symptom is an image that silently renders blank.
    /// Holding the original string instead removes the ceiling.
    /// </para>
    /// <para>
    /// <b>On .NET 10 and later</b> neither reason holds: <see cref="System.Uri"/> round-trips a
    /// <c>data:</c> URI exactly and has no length ceiling at all (measured: a 5,000,023-character
    /// payload constructs successfully). It also percent-escapes a non-base64 payload, which holding
    /// the raw string does not — <c>data:text/plain,Hello World!</c> comes back with the space
    /// escaped there and verbatim before .NET 10. So the bypass is gated: .NET 10 keeps using
    /// <see cref="System.Uri"/>, which is both correct and strictly better, and only the older
    /// targets take the string-holding path they actually need.
    /// </para>
    /// </remarks>
    public class RUri
    {
        private Uri? _uri { get; }

        /// <summary>
        /// The verbatim URI string, held instead of <see cref="_uri"/> for <c>data:</c> URIs.
        /// </summary>
        private string? _originalUri { get; }

        /// <summary>
        /// Whether <paramref name="uriString"/> has to bypass <see cref="System.Uri"/> and be held
        /// verbatim in <see cref="_originalUri"/> instead. Constantly <c>false</c> on .NET 10 and
        /// later, where <see cref="System.Uri"/> handles a <c>data:</c> URI correctly and without a
        /// length ceiling — see this type's own remarks. One predicate rather than an <c>#if</c> in
        /// each of the four constructors, so the two targets cannot drift apart in which
        /// constructors they cover: the gap this replaces was exactly that, the base-relative
        /// constructors having no <c>data:</c> handling at all.
        /// </summary>
        private static bool MustBypassSystemUri(string uriString) =>
#if NET10_0_OR_GREATER
            false;
#else
            uriString.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
#endif

        /// <summary>
        /// Parses <paramref name="uriString"/> as either an absolute or relative URI.
        /// </summary>
        /// <param name="uriString">The URI string to parse.</param>
        public RUri(string uriString)
        {
            ArgumentNullException.ThrowIfNull(uriString, nameof(uriString));

            if (MustBypassSystemUri(uriString))
            {
                _originalUri = uriString;
            }
            else
            {
                _uri = new Uri(uriString);
            }
        }

        /// <summary>
        /// Parses <paramref name="uriString"/> as the given <paramref name="uriKind"/>.
        /// </summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="uriKind">Whether the string is known to be absolute, relative, or either.</param>
        public RUri(string uriString, UriKind uriKind)
        {
            if (MustBypassSystemUri(uriString))
            {
                _originalUri = uriString;
            }
            else
            {
                _uri = new Uri(uriString, uriKind);
            }
        }

        /// <summary>
        /// Resolves <paramref name="uri"/> against <paramref name="baseUri"/>, the same way a relative
        /// <c>href</c>/<c>src</c>/<c>url()</c> reference is resolved against a document's base URL.
        /// </summary>
        /// <param name="baseUri">The base URI to resolve against.</param>
        /// <param name="uri">The (typically relative) URI to resolve.</param>
        public RUri(RUri baseUri, RUri uri)
        {
            if (uri._originalUri is not null)
            {
                _originalUri = uri._originalUri;
                return;
            }

            _uri = new Uri(baseUri.Uri, uri.Uri);
        }

        /// <summary>
        /// Resolves <paramref name="uri"/> against <paramref name="baseUri"/>, the same way a relative
        /// <c>href</c>/<c>src</c>/<c>url()</c> reference is resolved against a document's base URL.
        /// </summary>
        /// <param name="baseUri">The base URI to resolve against.</param>
        /// <param name="uri">The (typically relative) URI string to resolve.</param>
        public RUri(RUri baseUri, string uri)
        {
            // RFC 3986 section 5.2.2: a reference that carries its own scheme is already absolute and
            // the base is not consulted. A data: URI always does, so resolving it is a no-op - which
            // is what lets it skip System.Uri here as well as in the string constructors.
            if (MustBypassSystemUri(uri))
            {
                _originalUri = uri;
                return;
            }

            _uri = new Uri(baseUri.Uri, uri);
        }

        /// <summary>
        /// Wraps an existing <see cref="System.Uri"/> instance.
        /// </summary>
        /// <param name="uri">The URI to wrap.</param>
        public RUri(Uri uri)
        {
            _uri = uri;
        }

        /// <summary>
        /// The underlying <see cref="System.Uri"/>.
        /// </summary>
        /// <remarks>
        /// This is the one member the <c>data:</c> bypass does not cover, and only before .NET 10:
        /// reconstructing a <see cref="System.Uri"/> from a held string hits the same 65,519-character
        /// ceiling the bypass exists to avoid, so reading it for an oversized <c>data:</c> URI throws
        /// <see cref="UriFormatException"/> there. No caller reaches it for that scheme today — the
        /// three that read it (<c>HttpClientNetworkLoader</c>, <c>FileUriNetworkLoader</c>,
        /// <c>MimeKitNetworkLoader</c>) are selected by <see cref="Scheme"/> first — but a new one
        /// should read <see cref="AbsoluteUri"/>, which is always exact, rather than this.
        /// </remarks>
        public Uri Uri => _uri ?? new Uri(_originalUri!);

        /// <summary>
        /// The URI scheme (e.g. <c>https</c>, <c>file</c>, <c>data</c>).
        /// </summary>
        /// <remarks>
        /// A held <see cref="_originalUri"/> is only ever a <c>data:</c> URI —
        /// <see cref="MustBypassSystemUri"/> is what put it there.
        /// </remarks>
        public string Scheme => _uri is not null ? _uri.Scheme : "data";

        /// <summary>
        /// The fully escaped absolute URI string.
        /// </summary>
        public string AbsoluteUri => _uri is not null ? _uri.AbsoluteUri : _originalUri!;

        /// <summary>
        /// The original, unescaped URI string as it was parsed.
        /// </summary>
        public string OriginalString => _uri is not null ? _uri.OriginalString : _originalUri!;

        /// <summary>
        /// Whether this instance represents an absolute URI.
        /// </summary>
        public bool IsAbsoluteUri => _uri?.IsAbsoluteUri ?? true;

        /// <summary>
        /// Whether this URI refers to a local file.
        /// </summary>
        public bool IsFile => _uri?.IsFile ?? false;
    }
}
