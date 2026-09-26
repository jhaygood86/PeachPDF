using System.Threading;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// The one monitor that serialises access to the process-wide font caches: the font factory's font sources
    /// and resolver infos, and the typeface cache.
    /// </summary>
    internal static class FontLock
    {
        private static readonly object Gate = new();

        public static void Enter() => Monitor.Enter(Gate);

        public static void Exit() => Monitor.Exit(Gate);
    }
}
