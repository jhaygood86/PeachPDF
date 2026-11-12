using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>Warming the engine up is an optimization and must never be the thing that fails.</summary>
    public class WarmUpFailureTests
    {
        [Fact]
        public async System.Threading.Tasks.Task AWarmUpDocumentThatCannotBeLoaded_DoesNotEscape()
        {
            // A null document throws from LoadAsync; the warm-up swallows it, so the real document's own load is what reports a problem.
            await HtmlDocument.WarmUpDocumentAsync(null!);
        }
    }
}
