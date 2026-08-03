namespace PeachPDF.SourceGenerators
{
    /// <summary>Pipeline stage names for <c>IncrementalValueProvider.WithTrackingName</c>, so tests can inspect specific steps' cache behavior via <c>GeneratorRunResult.TrackedSteps</c>.</summary>
    internal static class TrackingNames
    {
        public const string ParsedJson = "ParsedJson";
        public const string TargetTypes = "TargetTypes";
    }
}
