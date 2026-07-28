using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Defines an xUnit collection for tests that depend on the process's current working directory (CWD).
    /// Tests in this collection are serialized (never run in parallel) to prevent race conditions when
    /// multiple target frameworks (e.g., net8.0 and net10.0) execute concurrently and both manipulate
    /// directories under the shared CWD.
    /// </summary>
    [CollectionDefinition("CWD-Dependent")]
    public class CwdDependentTestsCollection
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
