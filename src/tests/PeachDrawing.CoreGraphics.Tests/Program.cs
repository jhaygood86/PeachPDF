namespace PeachDrawing.CoreGraphics.Tests;

/// <summary>
/// The entry point xunit.v3 would generate blocks the main thread on an async call. The main thread of a macOS app has a synchronization
/// context that continuations need it to be free to run, so the tests run on a pool thread and the main thread waits.
/// </summary>
internal static class Program
{
    public static int Main(string[] args) =>
        Task.Run(() => args.Any(arg => arg == "--server" || arg == "--internal-msbuild-node")
            ? Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
            : Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args)).GetAwaiter().GetResult();
}
