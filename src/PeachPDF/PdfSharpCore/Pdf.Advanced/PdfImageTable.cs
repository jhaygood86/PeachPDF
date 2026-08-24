#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PeachPDF.PdfSharpCore.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

#nullable disable warnings

using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Contains all used images of a document.
    /// </summary>
    internal sealed class PdfImageTable : PdfResourceTable
    {
        /// <summary>
        /// Initializes a new instance of this class, which is a singleton for each document.
        /// </summary>
        public PdfImageTable(PdfDocument document)
            : base(document)
        { }

        /// <summary>
        /// Gets a PdfImage from an XImage, sized/embedded for use at the given on-page display size (PDF
        /// points). If no matching PdfImage already exists, a new one is created.
        /// </summary>
        public PdfImage GetImage(XImage image, double widthPt, double heightPt)
        {
            ImageSelector selector;
            int? targetWidth = null;
            int? targetHeight = null;

            if (!Owner.Options.DownscaleImages)
            {
                // No resize ever applies while downscaling is off, so the selector never depends on
                // display size - the original single-selector-per-XImage cache, unconditionally.
                selector = image._selector ??= new ImageSelector(image);
            }
            else if (image._lastSizedSelector != null
                && image._lastSizedSelectorWidthPt == widthPt
                && image._lastSizedSelectorHeightPt == heightPt)
            {
                // Same (image, display size) as the immediately preceding call - e.g. a logo redrawn
                // identically across a repeating header/footer - reuse the selector without recomputing
                // ComputeTargetPixelSize or allocating a new ImageSelector. Safe to skip straight to the
                // dictionary lookup below without targetWidth/targetHeight: this selector, if it needed a
                // resize, is already a key in _images from whichever call first computed it, so the
                // TryGetValue below is guaranteed to hit and a fresh PdfImage is never constructed here.
                selector = image._lastSizedSelector;
            }
            else
            {
                (targetWidth, targetHeight) = ComputeTargetPixelSize(image, widthPt, heightPt);
                selector = targetWidth.HasValue
                    ? new ImageSelector(image, targetWidth, targetHeight)
                    : (image._selector ??= new ImageSelector(image));

                image._lastSizedSelector = selector;
                image._lastSizedSelectorWidthPt = widthPt;
                image._lastSizedSelectorHeightPt = heightPt;
            }

            if (!_images.TryGetValue(selector, out PdfImage pdfImage))
            {
                pdfImage = new PdfImage(Owner, image, targetWidth, targetHeight);
                //pdfImage.Document = _document;
                Debug.Assert(pdfImage.Owner == Owner);
                _images[selector] = pdfImage;
            }
            return pdfImage;
        }

        /// <summary>
        /// Computes the pixel size to resize <paramref name="image"/> to before embedding, or
        /// <c>(null, null)</c> when no resize should happen: downscaling is off
        /// (<see cref="PdfDocumentOptions.DownscaleImages"/>), the display size isn't known/positive, or
        /// the image's natural size is already no larger than the (multiplier-adjusted) display size.
        /// Never upscales - the result is always clamped to the image's own natural pixel dimensions.
        /// </summary>
        private (int? width, int? height) ComputeTargetPixelSize(XImage image, double widthPt, double heightPt)
        {
            if (!Owner.Options.DownscaleImages) return (null, null);
            if (!(widthPt > 0) || !(heightPt > 0)) return (null, null);

            var naturalWidth = image.PixelWidth;
            var naturalHeight = image.PixelHeight;
            if (naturalWidth <= 0 || naturalHeight <= 0) return (null, null);

            var multiplier = Owner.Options.MaximumDownscaleMultiplier;
            var widthPx = widthPt / PeachPDF.CSS.Length.PointsPerPx * multiplier;
            var heightPx = heightPt / PeachPDF.CSS.Length.PointsPerPx * multiplier;

            // Round rather than ceiling: the target size also doubles as the dedup key (see GetImage),
            // so two boxes a document author considers "the same display size" but that resolve to
            // e.g. 99.98px and 100.02px through different layout paths (a percentage width vs. a flex
            // basis, say) round to the same target and so still dedup to one embed, rather than
            // ceiling-splitting them into 100px/101px and two full copies. The up-to-half-pixel
            // softness this can add is well within MaximumDownscaleMultiplier's own headroom margin.
            var targetWidth = Math.Clamp((int)Math.Round(widthPx, MidpointRounding.AwayFromZero), 1, naturalWidth);
            var targetHeight = Math.Clamp((int)Math.Round(heightPx, MidpointRounding.AwayFromZero), 1, naturalHeight);

            if (targetWidth >= naturalWidth && targetHeight >= naturalHeight) return (null, null);

            return (targetWidth, targetHeight);
        }

        /// <summary>
        /// Map from ImageSelector to PdfImage.
        /// </summary>
        readonly Dictionary<ImageSelector, PdfImage> _images = new Dictionary<ImageSelector, PdfImage>();

        /// <summary>
        /// A collection of information that uniquely identifies a particular PdfImage. When a resize
        /// target is in play, the target pixel size is part of the identity - the same source image used
        /// at two different display sizes embeds as two distinct PdfImages, each correctly sized, rather
        /// than one embed at whichever size happened to be requested first.
        /// </summary>
        internal class ImageSelector
        {
            /// <summary>
            /// Initializes a new instance of ImageSelector from an XImage, with no resize target.
            /// </summary>
            public ImageSelector(XImage image)
            {
                // HACK: implement a way to identify images when they are reused
                // TODO 4STLA Implementation that calculates MD5 hashes for images generated for the images can be found here: http://forum.PeachPDF.PdfSharpCore.net/viewtopic.php?p=6959#p6959
                if (image._path == null)
                    image._path = "*" + Guid.NewGuid().ToString("B");

                // HACK: just use full path to identify
                _path = image._path.ToLowerInvariant();
            }

            /// <summary>
            /// Initializes a new instance of ImageSelector from an XImage and the pixel size it will be
            /// resized to before embedding.
            /// </summary>
            public ImageSelector(XImage image, int? targetWidth, int? targetHeight) : this(image)
            {
                _targetWidth = targetWidth;
                _targetHeight = targetHeight;
            }

            public string Path
            {
                get { return _path; }
                set { _path = value; }
            }
            string _path;
            readonly int? _targetWidth;
            readonly int? _targetHeight;

            public override bool Equals(object? obj)
            {
                ImageSelector selector = obj as ImageSelector;
                if (selector == null)
                    return false;
                return _path == selector._path
                    && _targetWidth == selector._targetWidth
                    && _targetHeight == selector._targetHeight;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_path, _targetWidth, _targetHeight);
            }
        }
    }
}
