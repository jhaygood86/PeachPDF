using Android.App;
using Android.Runtime;

namespace PeachReader;

/// <summary>The Android application behind the MAUI app.</summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>Creates the application; the runtime calls this.</summary>
    /// <param name="handle">the native handle</param>
    /// <param name="ownership">who owns it</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
