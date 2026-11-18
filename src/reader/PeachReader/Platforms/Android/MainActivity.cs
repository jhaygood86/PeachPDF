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
        // A recreated activity (after a rotation the app handles itself this does not happen, but after the process was killed it does) still holds the intent
        // that started it; the document it named was opened then.
        if (savedInstanceState is null) Offer(Intent);
    }

    /// <inheritdoc />
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Offer(intent);
    }

    // A PDF handed over by another app is read here, so a file that is huge or a provider that is slow must not be able to run the app out of memory.
    private const int MaxIncomingBytes = 256 * 1024 * 1024;

    private void Offer(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data is not { } uri) return;
        var resolver = ContentResolver;
        _ = Task.Run(() =>
        {
            try
            {
                using var input = resolver?.OpenInputStream(uri);
                if (input is null) return;
                using var copy = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (copy.Length + read > MaxIncomingBytes) return;
                    copy.Write(buffer, 0, read);
                }

                IncomingDocument.Offer(copy.ToArray(), NameOf(uri));
            }
            catch (Exception)
            {
                // A file that cannot be read is not opened; the reader stays where it was.
            }
        });
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
