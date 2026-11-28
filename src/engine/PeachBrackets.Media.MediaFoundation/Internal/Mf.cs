using System;
using System.Threading;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace PeachBrackets.Media.MediaFoundation.Internal;

/// <summary>Starts COM on a thread and Media Foundation in the process, the first time either is needed.</summary>
internal static unsafe class Mf
{
    [ThreadStatic]
    private static bool s_threadReady;

    private static int s_started;
    private static int s_installed; // 0 not asked, 1 yes, 2 no

    /// <summary>Whether this Windows has Media Foundation at all (a server image has it as an optional feature).</summary>
    public static bool IsInstalled
    {
        get
        {
            if (s_installed == 0)
            {
                var found = System.Runtime.InteropServices.NativeLibrary.TryLoad("mfplat.dll", out var handle);
                if (found)
                    System.Runtime.InteropServices.NativeLibrary.Free(handle);

                s_installed = found ? 1 : 2;
            }

            return s_installed == 1;
        }
    }

    /// <summary>Makes the calling thread ready to call Media Foundation (a multithreaded COM apartment) and starts Media Foundation once per process.</summary>
    public static void Ensure()
    {
        if (!s_threadReady)
        {
            // S_FALSE (already initialized) and a changed mode (the thread is a single-threaded apartment already) both leave COM usable.
            PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
            s_threadReady = true;
        }

        if (Interlocked.Exchange(ref s_started, 1) == 0)
            PInvoke.MFStartup(PInvoke.MF_VERSION, 0).ThrowOnFailure();
    }
}
