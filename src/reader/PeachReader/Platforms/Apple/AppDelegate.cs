using Foundation;
using UIKit;

namespace PeachReader;

/// <summary>The application delegate behind the MAUI app on iOS and Mac Catalyst; it also takes a PDF another app (or Finder) hands it.</summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    // A PDF handed over by another app is read here, so a file that is huge must not be able to run the app out of memory.
    private const long MaxIncomingBytes = 256L * 1024 * 1024;

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <inheritdoc />
    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        Offer(url);
        return true;
    }

    /// <summary>Reads a file the system names and offers it to the window.</summary>
    /// <param name="url">the file's URL</param>
    internal static void Offer(NSUrl url)
    {
        if (!url.IsFileUrl) return;
        _ = Task.Run(() =>
        {
            bool scoped = url.StartAccessingSecurityScopedResource();
            try
            {
                var data = NSData.FromUrl(url);
                if (data is null || (long)data.Length > MaxIncomingBytes) return;
                IncomingDocument.Offer(data.ToArray(), url.LastPathComponent);
            }
            catch (Exception)
            {
                // A file that cannot be read is not opened; the reader stays where it was.
            }
            finally
            {
                if (scoped) url.StopAccessingSecurityScopedResource();
            }
        });
    }
}
