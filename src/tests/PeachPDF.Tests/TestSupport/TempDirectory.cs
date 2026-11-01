using System;
using System.IO;
using System.Threading;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Deletes a directory tree, retrying briefly on <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>.
    /// A temp directory a test just created under the OS temp path is frequently opened transiently by
    /// antivirus real-time scanning or the search indexer right after its files are written - the same
    /// path Windows reports as "being used by another process" even though nothing in the test process
    /// still holds a handle. That is host-environment noise, not a defect under test, so any test that
    /// creates a temp directory and deletes it in a <c>finally</c> block should route the delete through
    /// here instead of calling <see cref="Directory.Delete(string, bool)"/> directly.
    /// </summary>
    internal static class TempDirectory
    {
        public static void DeleteWithRetry(string path, int maxAttempts = 5, int retryDelayMilliseconds = 100)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(retryDelayMilliseconds);
                }
            }
        }
    }
}
