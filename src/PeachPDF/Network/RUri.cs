#nullable enable
using System;

namespace PeachPDF.Network
{
    /// <summary>
    /// Wraps <see cref="System.Uri"/> for use throughout PeachPDF's resource-loading pipeline, adding
    /// special-case handling for <c>data:</c> URIs (which <see cref="System.Uri"/> can parse but, on older
    /// target frameworks, cannot round-trip correctly through <see cref="AbsoluteUri"/>) and helpers for
    /// resolving a relative URI against a base URI.
    /// </summary>
    public class RUri
    {
        private Uri? _uri { get; }
#if !NET10_0_OR_GREATER
        private string? _originalUri { get; }
#endif

        /// <summary>
        /// Parses <paramref name="uriString"/> as either an absolute or relative URI.
        /// </summary>
        /// <param name="uriString">The URI string to parse.</param>
        public RUri(string uriString)
        {
            ArgumentNullException.ThrowIfNull(uriString, nameof(uriString));

#if NET10_0_OR_GREATER
            _uri = new Uri(uriString);
#else
            if (uriString.StartsWith("data:"))
            {
                _originalUri = uriString;
            }
            else
            {
                _uri = new Uri(uriString);
            }
#endif
        }

        /// <summary>
        /// Parses <paramref name="uriString"/> as the given <paramref name="uriKind"/>.
        /// </summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="uriKind">Whether the string is known to be absolute, relative, or either.</param>
        public RUri(string uriString, UriKind uriKind)
        {
#if NET10_0_OR_GREATER
            _uri = new Uri(uriString, uriKind);
#else
            if (uriString.StartsWith("data:"))
            {
                _originalUri = uriString;
            }
            else
            {
                _uri = new Uri(uriString, uriKind);
            }
#endif
        }

        /// <summary>
        /// Resolves <paramref name="uri"/> against <paramref name="baseUri"/>, the same way a relative
        /// <c>href</c>/<c>src</c>/<c>url()</c> reference is resolved against a document's base URL.
        /// </summary>
        /// <param name="baseUri">The base URI to resolve against.</param>
        /// <param name="uri">The (typically relative) URI to resolve.</param>
        public RUri(RUri baseUri, RUri uri)
        {
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

#if NET10_0_OR_GREATER
        /// <summary>
        /// The underlying <see cref="System.Uri"/>.
        /// </summary>
        public Uri Uri => _uri!;

        /// <summary>
        /// The URI scheme (e.g. <c>https</c>, <c>file</c>, <c>data</c>).
        /// </summary>
        public string Scheme => _uri!.Scheme;

        /// <summary>
        /// The fully escaped absolute URI string.
        /// </summary>
        public string AbsoluteUri => _uri!.AbsoluteUri;

        /// <summary>
        /// The original, unescaped URI string as it was parsed.
        /// </summary>
        public string OriginalString => _uri!.OriginalString;

        /// <summary>
        /// Whether this instance represents an absolute URI.
        /// </summary>
        public bool IsAbsoluteUri => _uri!.IsAbsoluteUri;

        /// <summary>
        /// Whether this URI refers to a local file.
        /// </summary>
        public bool IsFile => _uri!.IsFile;
#else
        /// <summary>
        /// The underlying <see cref="System.Uri"/>.
        /// </summary>
        public Uri Uri => _uri ?? new Uri(_originalUri!);

        /// <summary>
        /// The URI scheme (e.g. <c>https</c>, <c>file</c>, <c>data</c>).
        /// </summary>
        public string Scheme => _uri is not null ? _uri.Scheme : _originalUri!.Split(":")[0];

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
#endif
    }
}
