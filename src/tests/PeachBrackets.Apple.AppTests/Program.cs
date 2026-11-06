using AppKit;

namespace PeachBrackets.Apple.AppTests;

/// <summary>
/// A view needs the AppKit run loop (its timers, its main-thread callbacks, the continuations of its async loads), so the application runs on the main thread, as it would
/// in an app, and xunit runs on a worker; the tests hand work that touches a view to the main thread through <see cref="MainThread"/>.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        NSApplication.Init();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Accessory;

        _ = Task.Run(() => args.Any(arg => arg == "--server" || arg == "--internal-msbuild-node")
                ? Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
                : Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args))
            .ContinueWith(result => Environment.Exit(result.IsCompletedSuccessfully ? result.Result : 1));

        app.Run();
        return 0;
    }
}
