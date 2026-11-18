using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;

namespace PeachReader;

/// <summary>The one activity of the app, which also opens a PDF another app hands it (a file manager, a mail attachment, a link in a browser).</summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.KeyboardHidden)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataMimeType = "application/pdf")]
public class MainActivity : MauiAppCompatActivity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Offer(Intent);
    }

    /// <inheritdoc />
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Offer(intent);
    }

    private void Offer(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data is not { } uri) return;
        try
        {
            using var input = ContentResolver?.OpenInputStream(uri);
            if (input is null) return;
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            IncomingDocument.Offer(copy.ToArray(), NameOf(uri));
        }
        catch (Exception)
        {
            // A file that cannot be read is not opened; the reader stays where it was.
        }
    }

    private string? NameOf(Android.Net.Uri uri)
    {
        try
        {
            using var cursor = ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst()) return cursor.GetString(0);
        }
        catch (Exception)
        {
            // Some providers give no name; the title then comes from the document.
        }

        return uri.LastPathSegment;
    }
}
