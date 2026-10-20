using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.PDF;

namespace PeachPDF.Layout
{
    /// <summary>The file layers of a document being built, with the document's page count that their target ranges are read against.</summary>
    internal sealed class FileLayerPlan
    {
        private readonly List<(FileLayer Layer, IReadOnlyList<int> Targets)> _layers = [];

        public FileLayerPlan(IEnumerable<FileLayer> layers, int totalPages)
        {
            foreach (var layer in layers)
                _layers.Add((layer, layer.ResolveTargets(totalPages)));
        }

        /// <summary>Draws every layer of the requested kind that targets <paramref name="pageNumber"/> (one-based) onto the page.</summary>
        public void Paint(PdfComposerPage page, int pageNumber, bool underlay)
        {
            foreach (var (layer, targets) in _layers)
            {
                if (layer.IsUnderlay != underlay)
                    continue;

                var ordinal = IndexOf(targets, pageNumber);
                if (ordinal < 0 || layer.SourcePageFor(ordinal) is not { } sourcePage)
                    continue;

                // The canvas works in layout units; the imported page is in points.
                var canvas = page.Canvas;
                var scaled = canvas.PixelsPerPoint != 1.0;
                if (scaled)
                    canvas.PushTransform(Matrix3x2.CreateScale((float)canvas.PixelsPerPoint));

                try
                {
                    layer.Source.Paint(sourcePage, canvas);
                }
                finally
                {
                    if (scaled)
                        canvas.PopTransform();
                }
            }
        }

        private static int IndexOf(IReadOnlyList<int> targets, int pageNumber)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i] == pageNumber)
                    return i;
            }

            return -1;
        }
    }
}
