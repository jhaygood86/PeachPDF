using Microsoft.UI.Xaml;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    // The zoom the document's images were last chosen for, and the display scale they were chosen at: a pinch or a move to a denser display asks for
    // sharper ones, once it has settled, and the document keeps its layout while it loads them.
    private double _imagesChosenForZoom = 1;
    private double _imagesChosenForScale = 1;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _imageTimer;
    private XamlRoot? _watchedRoot;

    private double CurrentDevicePixelRatio() => ImageScale.DevicePixelRatio(XamlRoot?.RasterizationScale ?? 1);

    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the window-free decisions are ImageScale, tested on their own.")]
    private void WatchDisplayScale()
    {
        StopWatchingDisplayScale();
        _watchedRoot = XamlRoot;
        if (_watchedRoot is not null)
            _watchedRoot.Changed += OnXamlRootChanged;

        _imagesChosenForScale = CurrentDevicePixelRatio();
    }

    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the window-free decisions are ImageScale, tested on their own.")]
    private void StopWatchingDisplayScale()
    {
        if (_watchedRoot is not null)
            _watchedRoot.Changed -= OnXamlRootChanged;

        _watchedRoot = null;
        _imageTimer?.Stop();
    }

    // The window moved to a display with another scale (or its scale was changed): the images may need to be denser than they are.
    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the window-free decisions are ImageScale, tested on their own.")]
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (Math.Abs(CurrentDevicePixelRatio() - _imagesChosenForScale) > 0.01)
            StartImageTimer();
    }

    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the window-free decisions are ImageScale, tested on their own.")]
    private void QueueImageUpgrade()
    {
        if (_document is not null && ImageScale.NeedsUpgrade(_imagesChosenForZoom, _scroller.ZoomFactor))
            StartImageTimer();
    }

    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the window-free decisions are ImageScale, tested on their own.")]
    private void StartImageTimer()
    {
        if (_imageTimer is null)
        {
            _imageTimer = DispatcherQueue.CreateTimer();
            _imageTimer.IsRepeating = false;
            _imageTimer.Tick += (_, _) => _ = UpgradeImagesAsync();
        }

        _imageTimer.Interval = TimeSpan.FromMilliseconds(ImageScale.SettleMilliseconds);
        _imageTimer.Stop();
        _imageTimer.Start();
    }

    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the window-free decisions are ImageScale, tested on their own.")]
    private async Task UpgradeImagesAsync()
    {
        if (_document is not { } document)
            return;

        var zoom = Math.Max(_scroller.ZoomFactor, 0.01);
        var scale = CurrentDevicePixelRatio();
        document.SetRenderScale(zoom, scale);
        _imagesChosenForZoom = Math.Max(_imagesChosenForZoom, zoom);
        _imagesChosenForScale = scale;

        try
        {
            var result = await document.UpgradeImagesAsync();
            if (ReferenceEquals(document, _document))
                Repaint(result);
        }
        catch (Exception exception) when (ReferenceEquals(document, _document))
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
    }
}
