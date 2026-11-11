using System.Threading;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// The one lock that serialises access to the process-wide font caches: the font factory's font sources
    /// and resolver infos, and the typeface cache.
    /// </summary>
    internal static class FontLock
    {
        private static readonly Lock Gate = new();

        public static void Enter() => Gate.Enter();

        public static void Exit() => Gate.Exit();
    }
}
