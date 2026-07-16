using PeachPDF;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Svg;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Entities
{
    internal abstract record CssImage : IDisposable
    {
        public virtual void Dispose() { }

        internal virtual ValueTask EnsureLoadedAsync(HtmlContainerInt container) => ValueTask.CompletedTask;

        internal sealed record Url(string Href) : CssImage
        {
            private ImageLoadHandler? _handler;

            public RImage? Image => _handler?.Image;

            public SvgDocument? SvgDocument => _handler?.SvgDocument;

            internal override async ValueTask EnsureLoadedAsync(HtmlContainerInt container)
            {
                if (Image != null || SvgDocument != null) return;
                _handler ??= new ImageLoadHandler(container);
                await _handler.LoadImage(Href);
            }

            public override void Dispose() => _handler?.Dispose();
        }

        internal sealed record LinearGradient(ParsedLinearGradient Gradient) : CssImage;
        internal sealed record RadialGradient(ParsedRadialGradient Gradient) : CssImage;
        internal sealed record ConicGradient(ParsedConicGradient Gradient) : CssImage;
    }
}
