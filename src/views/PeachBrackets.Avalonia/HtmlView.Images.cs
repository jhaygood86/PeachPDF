using Avalonia.Threading;

namespace PeachBrackets.Avalonia;

public sealed partial class HtmlView
{
    // The zoom the document's images were last chosen for. A zoom that has settled asks for denser ones; the document keeps its layout while it loads
    // them, and the tiles are painted again with what changed.
    private double _imagesChosenForZoom = 1;
    private DispatcherTimer? _upgradeTimer;

    private void QueueImageUpgrade()
    {
        if (_document is null || !ImageScale.NeedsUpgrade(_imagesChosenForZoom, _zoom))
            return;

        _upgradeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ImageScale.SettleMilliseconds) };
        _upgradeTimer.Stop();
        _upgradeTimer.Tick -= OnUpgradeTick;
        _upgradeTimer.Tick += OnUpgradeTick;
        _upgradeTimer.Start();
    }

    private void OnUpgradeTick(object? sender, EventArgs e)
    {
        _upgradeTimer?.Stop();
        _ = UpgradeImagesAsync();
    }

    private async Task UpgradeImagesAsync()
    {
        if (_document is not { } document)
            return;

        var zoom = _zoom;
        document.SetRenderScale(zoom, ImageScale.DevicePixelRatio(_dpi));
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
            RaiseDocumentFailed(exception);
        }
    }
}
