using Microsoft.Maui.Handlers;
using PeachReader.Views;

namespace PeachReader.Droid;

/// <summary>Connects <see cref="PageViewport"/> to its Android view.</summary>
internal sealed class PageViewportHandler : ViewHandler<PageViewport, PageViewportPlatformView>
{
    private static readonly IPropertyMapper<PageViewport, PageViewportHandler> Map =
        new PropertyMapper<PageViewport, PageViewportHandler>(ViewMapper)
        {
            [nameof(PageViewport.Controller)] = (handler, view) => handler.PlatformView.Connect(view, view.Controller),
        };

    public PageViewportHandler() : base(Map)
    {
    }

    protected override PageViewportPlatformView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(PageViewportPlatformView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Connect(VirtualView, VirtualView.Controller);
    }

    protected override void DisconnectHandler(PageViewportPlatformView platformView)
    {
        platformView.Disconnect();
        base.DisconnectHandler(platformView);
    }
}
