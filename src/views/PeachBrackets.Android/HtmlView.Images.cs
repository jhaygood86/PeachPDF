using PeachBrackets.Android.Logic;
using System;

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    // The zoom the document's images were last chosen for. A pinch (or a double tap) that has settled asks for denser ones; the document keeps its
    // layout while it loads them, and the tiles are painted again with what changed.
    private double _imagesChosenForZoom = 1;
    private Java.Lang.IRunnable? _upgradeImages;

    private void QueueImageUpgrade()
    {
        if (_document is null || !ImageScale.NeedsUpgrade(_imagesChosenForZoom, _zoom))
            return;

        _upgradeImages ??= new Java.Lang.Runnable(() => _ = UpgradeImagesAsync());
        _handler.RemoveCallbacks(_upgradeImages);
        _handler.PostDelayed(_upgradeImages, ImageScale.SettleMilliseconds);
    }

    private async System.Threading.Tasks.Task UpgradeImagesAsync()
    {
        if (_document is not { } document)
            return;

        var zoom = _zoom;
        document.SetRenderScale(zoom, ImageScale.DevicePixelRatio(_density));
        _imagesChosenForZoom = Math.Max(_imagesChosenForZoom, zoom);

        try
        {
            var result = await document.UpgradeImagesAsync();
            if (ReferenceEquals(document, _document) && result.DirtyRegions.Count > 0)
            {
                UpdateMetrics();
                InvalidateContent();
            }
        }
        catch (Exception exception) when (ReferenceEquals(document, _document))
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
    }
}
