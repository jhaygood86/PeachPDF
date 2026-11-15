using System;
using System.Threading.Tasks;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    private bool _updatingContentVisibility;
    private bool _contentVisibilityAgain;

    // The contents of an element with `content-visibility: auto` that is away from the view are skipped and those that came near are shown
    // (css-contain-2 §4). The view is moved to where the document says it is looking, so what changed above it does not move what is on screen.
    // One update at a time: a scroll that comes while one runs is looked at again when it is done.
    private async Task UpdateContentVisibilityAsync(HtmlDocument document)
    {
        if (_updatingContentVisibility)
        {
            _contentVisibilityAgain = true;
            return;
        }

        _updatingContentVisibility = true;
        try
        {
            do
            {
                _contentVisibilityAgain = false;
                PeachBrackets.Updates.UpdateResult result;
                using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context!, 1, 1);

                // As for a restyle: the old layout is dropped before the new one is in, and a region painted in between would be left empty.
                _restyling = true;
                try
                {
                    result = await document.UpdateContentVisibilityAsync(measuring.Canvas);
                }
                finally
                {
                    _restyling = false;
                }

                if (!ReferenceEquals(document, _document) || !result.Applied)
                    continue;

                UpdateMetrics();
                ScrollToY(document.Viewport.Y * PixelsPerPoint, animate: false);
                foreach (var region in result.DirtyRegions)
                    InvalidateRegion(region.PageIndex, region.Bounds);

                RefreshAccessibility(document);
            }
            while (_contentVisibilityAgain && ReferenceEquals(document, _document));
        }
        finally
        {
            _updatingContentVisibility = false;
        }
    }
}
