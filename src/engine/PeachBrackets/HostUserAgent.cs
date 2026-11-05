using System;
using System.Runtime.InteropServices;

namespace PeachBrackets;

/// <summary>The kinds of system whose Chromium <c>User-Agent</c> platform token <see cref="HostUserAgent"/> reproduces.</summary>
internal enum HostPlatform
{
    Windows,
    MacOs,
    Android,
    Linux,
}

/// <summary>
/// The <c>User-Agent</c> the host resource loader sends: the string Chromium sends on the same platform, followed by <c>PeachBrackets/&lt;version&gt;</c>.
/// Chromium reduces what it reports (a fixed Windows, macOS and Android version, a fixed Chrome minor version), so the values here are fixed too and
/// do not describe the machine beyond its kind and, on Linux, its CPU.
/// </summary>
internal static class HostUserAgent
{
    /// <summary>The Chromium version the string claims (the major version; the rest is zero, as Chromium reports it).</summary>
    internal const string ChromeVersion = "156.0.0.0";

    /// <summary>The string for the system this runs on.</summary>
    internal static string Default { get; } = Compose(PlatformToken(CurrentPlatform(), RuntimeInformation.ProcessArchitecture,
        Environment.Is64BitProcess, Environment.Is64BitOperatingSystem), PeachBracketsProductInfo.Version);

    private static HostPlatform CurrentPlatform() =>
        OperatingSystem.IsAndroid() ? HostPlatform.Android
        : OperatingSystem.IsWindows() ? HostPlatform.Windows
        : OperatingSystem.IsMacOS() ? HostPlatform.MacOs
        : HostPlatform.Linux;

    /// <summary>The whole string for a platform token (what goes between the parentheses) and a PeachBrackets version.</summary>
    internal static string Compose(string platformToken, string version) =>
        $"Mozilla/5.0 ({platformToken}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromeVersion} Safari/537.36 PeachBrackets/{version}";

    /// <summary>The platform and CPU values Chromium reports in the parentheses on a system of this kind.</summary>
    internal static string PlatformToken(HostPlatform platform, Architecture architecture, bool is64BitProcess, bool is64BitOperatingSystem) => platform switch
    {
        // Frozen at 10.0 on Windows 11 too. A 64-bit process says Win64; x64 (also on Arm, where Chromium runs as x64 or reports the same); a
        // 32-bit one on a 64-bit system says WOW64, and on a 32-bit system says nothing more.
        HostPlatform.Windows => is64BitProcess ? "Windows NT 10.0; Win64; x64" : is64BitOperatingSystem ? "Windows NT 10.0; WOW64" : "Windows NT 10.0",

        // Frozen at 10.15.7 and Intel, whatever the Mac.
        HostPlatform.MacOs => "Macintosh; Intel Mac OS X 10_15_7",

        // Frozen at Android 10 with the model "K".
        HostPlatform.Android => "Linux; Android 10; K",

        _ => "X11; Linux " + LinuxCpu(architecture),
    };

    // What uname reports for the CPU, which is what Chromium puts after "Linux".
    private static string LinuxCpu(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "aarch64",
        Architecture.Arm => "armv8l",
        Architecture.X86 => "i686",
        _ => "x86_64",
    };
}
