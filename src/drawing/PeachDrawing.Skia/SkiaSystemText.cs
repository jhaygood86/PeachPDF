using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PeachDrawing.Skia;

/// <summary>What the operating system says about how text is smoothed on the user's displays.</summary>
/// <param name="Subpixel">whether text is expected to have a coverage per LCD stripe (ClearType, the desktop's <c>rgba</c> smoothing)</param>
/// <param name="BgrStripes">whether the stripes of a pixel run blue, green, red from left to right</param>
internal readonly record struct SystemTextSettings(bool Subpixel, bool BgrStripes);

/// <summary>The seam <see cref="SkiaTextRendering.Auto"/> asks, so a test can say what the system would answer.</summary>
internal interface ISystemTextSettings
{
    /// <summary>What the system says now.</summary>
    SystemTextSettings Get();
}

/// <summary>
/// The default <see cref="ISystemTextSettings"/>: Windows' ClearType settings, the desktop's font smoothing on Linux, and grayscale elsewhere (macOS
/// has had no subpixel text since Apple removed it). Read once per process, on a pool thread started when the first context is made, and never
/// waited for longer than a moment: a failure or a slow answer means grayscale.
/// </summary>
internal sealed partial class SystemTextSettingsProvider : ISystemTextSettings
{
    private static readonly Lazy<Task<SystemTextSettings>> Detected = new(() => Task.Run(Detect));

    internal static SystemTextSettingsProvider Instance { get; } = new();

    /// <summary>Starts reading the system's setting in the background, so the first text drawn does not wait for it.</summary>
    internal static void Prefetch() => _ = Detected.Value;

    /// <inheritdoc />
    public SystemTextSettings Get()
    {
        var task = Detected.Value;
        return task.Wait(TimeSpan.FromMilliseconds(500)) && task.IsCompletedSuccessfully ? task.Result : default;
    }

    internal static SystemTextSettings Detect() => Detect(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), DetectWindows, DetectLinux);

    /// <summary>The detection with the platform and the platform readers passed in, so a test can take a branch the machine it runs on never would.</summary>
    internal static SystemTextSettings Detect(bool isWindows, bool isLinux, Func<SystemTextSettings> windows, Func<SystemTextSettings> linux)
    {
        try
        {
            if (isWindows)
                return windows();

            if (isLinux)
                return linux();
        }
        catch (Exception)
        {
            // Not being able to read the setting is not an error: the text is smoothed in grayscale.
        }

        return default;
    }

    // ---- Windows ------------------------------------------------------------------------------------------

    private const uint GetFontSmoothing = 0x004A;
    private const uint GetFontSmoothingType = 0x200A;
    private const uint GetFontSmoothingOrientation = 0x2012;
    private const uint ClearTypeSmoothing = 2;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static partial int SystemParametersInfo(uint action, uint param, ref uint value, uint winIni);

    private static SystemTextSettings DetectWindows()
    {
        uint smoothing = 0, type = 0, orientation = 1;
        var on = SystemParametersInfo(GetFontSmoothing, 0, ref smoothing, 0) != 0 && smoothing != 0
            && SystemParametersInfo(GetFontSmoothingType, 0, ref type, 0) != 0 && type == ClearTypeSmoothing;

        // 0 is BGR and 1 is RGB.
        var bgr = on && SystemParametersInfo(GetFontSmoothingOrientation, 0, ref orientation, 0) != 0 && orientation == 0;
        return new SystemTextSettings(on, bgr);
    }

    // ---- Linux --------------------------------------------------------------------------------------------

    internal static SystemTextSettings DetectLinux() => FromGsettings(Gsettings("font-antialiasing"), Gsettings);

    /// <summary>The settings the output of <c>font-antialiasing</c> asks for, reading <c>font-rgba-order</c> through <paramref name="readOrder"/> only when subpixel text is on.</summary>
    internal static SystemTextSettings FromGsettings(string? smoothing, Func<string, string?> readOrder) =>
        smoothing is null || !ParseGsettingsSmoothing(smoothing) ? default : new SystemTextSettings(true, ParseGsettingsBgr(readOrder("font-rgba-order")));

    /// <summary>What a started <c>gsettings</c> process offers, so a test can stand in for the process.</summary>
    internal interface IGsettingsProcess : IDisposable
    {
        /// <summary>Reads what the process wrote to its standard output.</summary>
        Task<string> ReadOutputAsync();

        /// <summary>Waits for the process to end, and says whether it did within the time.</summary>
        bool WaitForExit(int milliseconds);

        /// <summary>Ends the process.</summary>
        void Kill();

        /// <summary>The code the process ended with.</summary>
        int ExitCode { get; }
    }

    private sealed class RealGsettingsProcess(Process process) : IGsettingsProcess
    {
        public Task<string> ReadOutputAsync() => process.StandardOutput.ReadToEndAsync();

        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);

        public void Kill() => process.Kill();

        public int ExitCode => process.ExitCode;

        public void Dispose() => process.Dispose();
    }

    private static IGsettingsProcess? StartGsettings(string key)
    {
        var process = Process.Start(new ProcessStartInfo("gsettings", ["get", "org.gnome.desktop.interface", key])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        return process is null ? null : new RealGsettingsProcess(process);
    }

    internal static string? Gsettings(string key) => Gsettings(key, StartGsettings, 1000);

    /// <summary>One key of <c>org.gnome.desktop.interface</c> as <c>gsettings</c> prints it, or null when there is no answer: no such program, a failure, or a wait longer than <paramref name="timeoutMilliseconds"/>.</summary>
    internal static string? Gsettings(string key, Func<string, IGsettingsProcess?> start, int timeoutMilliseconds)
    {
        try
        {
            using var process = start(key);
            if (process is null)
                return null;

            var output = process.ReadOutputAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (Exception)
        {
            // No gsettings (not a GNOME-family desktop, a minimal container): grayscale.
            return null;
        }
    }

    /// <summary>Whether the output of <c>gsettings get org.gnome.desktop.interface font-antialiasing</c> (<c>'rgba'</c>, <c>'grayscale'</c> or <c>'none'</c>) asks for subpixel text.</summary>
    internal static bool ParseGsettingsSmoothing(string output) => output.Trim().Trim('\'', '"').Equals("rgba", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the output of <c>gsettings get org.gnome.desktop.interface font-rgba-order</c> (<c>'rgb'</c>, <c>'bgr'</c>, <c>'vrgb'</c> or <c>'vbgr'</c>) is the horizontal blue-first order.</summary>
    internal static bool ParseGsettingsBgr(string? output) => output?.Trim().Trim('\'', '"').Equals("bgr", StringComparison.OrdinalIgnoreCase) == true;
}
