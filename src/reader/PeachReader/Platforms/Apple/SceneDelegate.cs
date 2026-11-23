using Foundation;
using UIKit;

namespace PeachReader;

/// <summary>The scene delegate (iOS 26 and later require one); it passes a PDF the system opens in this scene on to the window.</summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    /// <inheritdoc />
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);
        if (connectionOptions?.UrlContexts is { } contexts)
            foreach (var context in contexts) AppDelegate.Offer(context.Url);
    }

    /// <summary>Takes the files the system opens in a scene that is already running.</summary>
    /// <param name="scene">the scene</param>
    /// <param name="urlContexts">the files</param>
    [Export("scene:openURLContexts:")]
    public void OpenUrlContexts(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        foreach (UIOpenUrlContext context in urlContexts) AppDelegate.Offer(context.Url);
    }
}
