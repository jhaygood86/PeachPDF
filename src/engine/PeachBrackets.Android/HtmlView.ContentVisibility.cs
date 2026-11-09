using PeachBrackets.Updates;

namespace PeachBrackets.Android;

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
                if (_context is not { } context)
                    return;

                UpdateResult result;
                using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1, 1)))
                using (var measuring = new PeachDrawing.Skia.SkiaComposer(context, surface, 1, 1))
                    result = await document.UpdateContentVisibilityAsync(measuring.Canvas);

                if (!ReferenceEquals(document, _document) || !result.Applied)
                    continue;

                UpdateMetrics();
                RefreshAccessibility(document);

                // The new top of what is shown, as the document keeps it; the scroll offset is in pixels.
                ScrollTo(_scrollX, document.Viewport.Y * Scale * PixelsPerPoint);
                InvalidateContent();
            }
            while (_contentVisibilityAgain && ReferenceEquals(document, _document));
        }
        finally
        {
            _updatingContentVisibility = false;
        }
    }
}
