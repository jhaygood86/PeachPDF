namespace PeachReader;

/// <summary>Builds the app: its first window and the handlers that draw each platform's page view.</summary>
public static class MauiProgram
{
    /// <summary>Creates the app for the platform it runs on.</summary>
    /// <returns>the app</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureMauiHandlers(handlers =>
        {
#if WINDOWS
            handlers.AddHandler<Views.PageViewport, WinUI.PageViewportHandler>();
            Thumbnails.Renderer = new WinUI.ThumbnailRenderer();
#elif ANDROID
            handlers.AddHandler<Views.PageViewport, Droid.PageViewportHandler>();
            Thumbnails.Renderer = new Droid.ThumbnailRenderer();
#elif IOS || MACCATALYST
            handlers.AddHandler<Views.PageViewport, Apple.PageViewportHandler>();
            Thumbnails.Renderer = new Apple.ThumbnailRenderer();
#endif
        });
        return builder.Build();
    }
}
