using PeachBrackets.Html.Utils;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary><see cref="AsyncGuard"/>: what a document that must not wait does when something it asked for is not done yet.</summary>
    public class AsyncGuardTests
    {
        private static Task Pending() => new TaskCompletionSource().Task;

        [Fact]
        public async Task AValueTaskResult_PassesThroughWhenNothingIsRequired_OrItIsDone()
        {
            Assert.Equal(7, await AsyncGuard.Require(new ValueTask<int>(7), synchronousOnly: true, "load"));
            Assert.Equal(7, await AsyncGuard.Require(new ValueTask<int>(Task.FromResult(7)), synchronousOnly: false, "load"));
        }

        [Fact]
        public void AnUnfinishedValueTaskResult_IsRefusedWhenTheDocumentMustNotWait()
        {
            var pending = new ValueTask<int>(new TaskCompletionSource<int>().Task);

            var exception = Assert.ThrowsAny<InvalidOperationException>(() => AsyncGuard.Require(pending, synchronousOnly: true, "load the image", "https://example.test/a.png"));
            Assert.Contains("load the image 'https://example.test/a.png'", exception.Message);
        }

        [Fact]
        public async Task AValueTaskWithNoResult_IsRefusedOnlyWhenUnfinishedAndTheDocumentMustNotWait()
        {
            await AsyncGuard.Require(new ValueTask(), synchronousOnly: true, "load a font");
            await AsyncGuard.Require(new ValueTask(Task.CompletedTask), synchronousOnly: false, "load a font");

            var pending = new ValueTask(Pending());
            var exception = Assert.ThrowsAny<InvalidOperationException>(() => AsyncGuard.Require(pending, synchronousOnly: true, "load a font"));
            Assert.Contains("load a font", exception.Message);
            Assert.DoesNotContain("'", exception.Message);
        }

        [Fact]
        public async Task ASharedTask_IsRefusedWhenUnfinishedAndTheDocumentMustNotWait_AndOtherwiseAwaited()
        {
            Assert.Equal("done", await AsyncGuard.Require(Task.FromResult("done"), synchronousOnly: true, "read the cache"));

            var source = new TaskCompletionSource<string>();
            var exception = Assert.ThrowsAny<InvalidOperationException>(() => AsyncGuard.Require(source.Task, synchronousOnly: true, "read the cache"));
            Assert.Contains("read the cache", exception.Message);

            var waiting = AsyncGuard.Require(source.Task, synchronousOnly: false, "read the cache");
            source.SetResult("late");
            Assert.Equal("late", await waiting);
        }

        [Fact]
        public void ARefusedTasksFailure_IsObservedSoItDoesNotSurfaceLater()
        {
            var source = new TaskCompletionSource<int>();
            Assert.ThrowsAny<InvalidOperationException>(() => AsyncGuard.Require(source.Task, synchronousOnly: true, "load"));

            source.SetException(new InvalidOperationException("late failure"));
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
