using Microsoft.UI.Xaml;

namespace PeachReader.WinUI;

/// <summary>The WinUI application behind the MAUI app.</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Creates the application.</summary>
    public App()
    {
        InitializeComponent();
        // A crash in a native control leaves no managed stack for the report; this keeps one the user can attach to a bug report.
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);
        if (Environment.GetEnvironmentVariable("PEACHREADER_TRACE_EXCEPTIONS") is { Length: > 0 })
            AppDomain.CurrentDomain.FirstChanceException += (_, e) => CrashLog.Write("first chance: " + e.Exception);
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    internal static void LogCrash(Exception? exception) => CrashLog.Write(exception?.ToString() ?? "(unknown)");
}

/// <summary>Appends to the crash log in the app's local data folder.</summary>
internal static class CrashLog
{
    /// <summary>Appends a line.</summary>
    /// <param name="text">what to record</param>
    internal static void Write(string text)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeachReader");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), DateTime.Now.ToString("u") + Environment.NewLine + text + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception)
        {
            // Nothing more can be done about a failure to write the log.
        }
    }
}
