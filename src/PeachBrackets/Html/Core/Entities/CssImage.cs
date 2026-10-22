using PeachPDF;
using PeachDrawing.Core;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Svg;
using System;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Entities
{
    internal abstract record CssImage : IDisposable
    {
        public virtual void Dispose() { }

        internal virtual ValueTask EnsureLoadedAsync(HtmlContainerInt container) => ValueTask.CompletedTask;

        /// <summary>
        /// The image a descendant takes when it inherits this layer (<c>background-image: inherit</c>). A box disposes the images it owns, so a
        /// layer that holds a loaded resource must be a separate, not-yet-loaded copy; a gradient is an immutable value and is shared.
        /// </summary>
        internal virtual CssImage CopyForInheritance() => this;

        internal sealed record Url(string Href) : CssImage
        {
            private ImageLoadHandler? _handler;

            public Image? Image => _handler?.Image;

            public SvgDocument? SvgDocument => _handler?.SvgDocument;

            internal override async ValueTask EnsureLoadedAsync(HtmlContainerInt container)
            {
                if (Image != null || SvgDocument != null) return;
                _handler ??= new ImageLoadHandler(container);
                await _handler.LoadImage(Href);
            }

            internal override CssImage CopyForInheritance() => new Url(Href);

            public override void Dispose() => _handler?.Dispose();
        }

        internal sealed record LinearGradient(ParsedLinearGradient Gradient) : CssImage;
        internal sealed record RadialGradient(ParsedRadialGradient Gradient) : CssImage;
        internal sealed record ConicGradient(ParsedConicGradient Gradient) : CssImage;
    }
}
