using PeachPDF;
using PeachDrawing.Core;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Svg;
using System;
using System.Collections.Generic;
using PeachBrackets.Html.Utils;
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

        /// <summary>The options of an <c>image-set()</c> and the resolution (dppx) each declares, so a chosen <see cref="Url"/> can be upgraded.</summary>
        internal sealed class ImageSetOptions(IReadOnlyList<CssImage> sources, IReadOnlyList<double> densities)
        {
            public IReadOnlyList<CssImage> Sources { get; } = sources;

            public IReadOnlyList<double> Densities { get; } = densities;
        }

        internal sealed record Url(string Href) : CssImage
        {
            private ImageLoadHandler? _handler;
            private List<ImageLoadHandler>? _retired;

            /// <summary>The address of the image; moves to a denser option when an <c>image-set()</c> is chosen again.</summary>
            public string Href { get; private set; } = Href;

            /// <summary>The <c>image-set()</c> this image was chosen from, or null for a plain <c>url()</c>.</summary>
            internal ImageSetOptions? Set { get; set; }

            /// <summary>Which option of <see cref="Set"/> this is.</summary>
            internal int ChosenIndex { get; set; }

            /// <summary>The image's declared resolution in dppx (1 for a <c>url()</c>): its natural size is its pixel size over this.</summary>
            internal double Density { get; set; } = 1;

            /// <summary>
            /// Chooses among the options of the <c>image-set()</c> again and loads a denser one when the display wants it; never goes to a less
            /// dense one. The natural size follows <see cref="Density"/>, so the image keeps its size on the page.
            /// </summary>
            /// <returns>whether the image changed</returns>
            internal async ValueTask<bool> ReselectAsync(HtmlContainerInt container)
            {
                if (Set is not { } set || _handler is null) return false;

                var pick = ResponsiveImageSelector.PickDensity(set.Densities, container);
                if (pick < 0 || pick == ChosenIndex || set.Densities[pick] <= set.Densities[ChosenIndex] || set.Sources[pick] is not Url target)
                    return false;

                var handler = new ImageLoadHandler(container);
                await handler.LoadImage(target.Href);

                if (handler.Image is null && handler.SvgDocument is null)
                {
                    handler.Dispose();
                    return false;
                }

                // A cached image another box got by a cache hit may still read from the retired handler's stream, so it lives until this image does.
                (_retired ??= []).Add(_handler);
                _handler = handler;
                Href = target.Href;
                Density = set.Densities[pick];
                ChosenIndex = pick;
                return true;
            }

            public Image? Image => _handler?.Image;

            public SvgDocument? SvgDocument => _handler?.SvgDocument;

            internal override async ValueTask EnsureLoadedAsync(HtmlContainerInt container)
            {
                if (Image != null || SvgDocument != null) return;
                _handler ??= new ImageLoadHandler(container);
                await _handler.LoadImage(Href);
            }

            internal override CssImage CopyForInheritance() => new Url(Href) { Set = Set, ChosenIndex = ChosenIndex, Density = Density };

            public override void Dispose()
            {
                _handler?.Dispose();
                if (_retired is null) return;
                foreach (var retired in _retired) retired.Dispose();
                _retired = null;
            }
        }

        internal sealed record LinearGradient(ParsedLinearGradient Gradient) : CssImage;
        internal sealed record RadialGradient(ParsedRadialGradient Gradient) : CssImage;
        internal sealed record ConicGradient(ParsedConicGradient Gradient) : CssImage;
    }
}
