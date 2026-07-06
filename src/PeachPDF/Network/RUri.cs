#nullable enable
using System;

namespace PeachPDF.Network
{
    public class RUri
    {
        private Uri? _uri { get; }
#if !NET10_0_OR_GREATER
        private string? _originalUri { get; }
#endif

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

        public RUri(RUri baseUri, RUri uri)
        {
            _uri = new Uri(baseUri.Uri, uri.Uri);
        }

        public RUri(RUri baseUri, string uri)
        {
            _uri = new Uri(baseUri.Uri, uri);
        }

        public RUri(Uri uri)
        {
            _uri = uri;
        }

#if NET10_0_OR_GREATER
        public Uri Uri => _uri!;

        public string Scheme => _uri!.Scheme;

        public string AbsoluteUri => _uri!.AbsoluteUri;

        public string OriginalString => _uri!.OriginalString;

        public bool IsAbsoluteUri => _uri!.IsAbsoluteUri;

        public bool IsFile => _uri!.IsFile;
#else
        public Uri Uri => _uri ?? new Uri(_originalUri!);

        public string Scheme => _uri is not null ? _uri.Scheme : _originalUri!.Split(":")[0];

        public string AbsoluteUri => _uri is not null ? _uri.AbsoluteUri : _originalUri!;

        public string OriginalString => _uri is not null ? _uri.OriginalString : _originalUri!;

        public bool IsAbsoluteUri => _uri?.IsAbsoluteUri ?? true;

        public bool IsFile => _uri?.IsFile ?? false;
#endif
    }
}
