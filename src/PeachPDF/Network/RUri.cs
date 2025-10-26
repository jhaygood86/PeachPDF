#nullable enable
using System;

namespace PeachPDF.Network
{
    public class RUri
    {
        private Uri? _uri { get; }
        private string? _originalUri { get; }

        public RUri(string uriString)
        {
            ArgumentNullException.ThrowIfNull(uriString, nameof(uriString));

            if (uriString.StartsWith("data:"))
            {
                _originalUri = uriString;
            }
            else
            {
                _uri = new Uri(uriString);
            }
        }

        public RUri(string uriString, UriKind uriKind)
        {
            if (uriString.StartsWith("data:"))
            {
                _originalUri = uriString;
            }
            else
            {
                _uri = new Uri(uriString, uriKind);
            }
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

        public Uri Uri => _uri ?? new Uri(_originalUri!);

        public string Scheme => _uri is not null ? _uri.Scheme : _originalUri!.Split(":")[0];

        public string AbsoluteUri => _uri is not null ? _uri.AbsoluteUri : _originalUri!;

        public string OriginalString => _uri is not null ? _uri.OriginalString : _originalUri!;

        public bool IsAbsoluteUri => _uri?.IsAbsoluteUri ?? true;

        public bool IsFile => _uri?.IsFile ?? false;
    }
}
