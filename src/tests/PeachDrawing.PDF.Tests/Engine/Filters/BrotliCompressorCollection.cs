namespace PeachPDF.Tests.PdfEngineTests.Filters
{
    /// <summary>Holds every test that registers a process-wide Brotli compressor; never runs alongside other test classes.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class BrotliCompressorCollection
    {
        public const string Name = "Brotli compressor";
    }
}
