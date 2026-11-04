using System;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Enforces <see cref="HtmlDocumentOptions.SynchronousOnly"/>: a host that said layout needs nothing asynchronous gets an
    /// <see cref="InvalidOperationException"/> at the point where the document tries something that would really suspend, instead of
    /// a blocked thread or a half-finished layout.
    /// </summary>
    /// <remarks>
    /// An operation that has already completed (a <c>data:</c> URI, a font the host registered, a loader that answers from memory) is passed
    /// through unchanged, so it costs nothing. One that has not is abandoned: its eventual failure is observed so it does not surface as an
    /// unobserved task exception, and nothing waits for it.
    /// </remarks>
    internal static class AsyncGuard
    {
        /// <summary>Returns <paramref name="task"/>, or throws when synchronous layout was requested and it has not completed.</summary>
        /// <param name="task">the operation the pipeline is about to await</param>
        /// <param name="synchronousOnly">whether the document was asked to need nothing asynchronous</param>
        /// <param name="action">what the document was doing, as a verb phrase ("load the image", "load a font")</param>
        /// <param name="resource">what it was doing it to, or null</param>
        public static ValueTask<T> Require<T>(ValueTask<T> task, bool synchronousOnly, string action, object? resource = null)
        {
            if (synchronousOnly && !task.IsCompleted)
                throw Abandon(task.AsTask(), action, resource);

            return task;
        }

        /// <summary><see cref="Require{T}(ValueTask{T}, bool, string, object?)"/> for an operation with no result.</summary>
        public static ValueTask Require(ValueTask task, bool synchronousOnly, string action, object? resource = null)
        {
            if (synchronousOnly && !task.IsCompleted)
                throw Abandon(task.AsTask(), action, resource);

            return task;
        }

        /// <summary><see cref="Require{T}(ValueTask{T}, bool, string, object?)"/> for a <see cref="Task{T}"/> a shared cache holds.</summary>
        public static ValueTask<T> Require<T>(Task<T> task, bool synchronousOnly, string action, object? resource = null)
        {
            if (synchronousOnly && !task.IsCompleted)
                throw Abandon(task, action, resource);

            return new ValueTask<T>(task);
        }

        private static SynchronousOnlyViolationException Abandon(Task pending, string action, object? resource)
        {
            _ = pending.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            var what = resource is null ? action : $"{action} '{resource}'";
            return new SynchronousOnlyViolationException(
                $"The document tried to {what} asynchronously, but HtmlDocumentOptions.SynchronousOnly was set. " +
                "Supply the resource without waiting (a data: URI, or a render context that completes its work immediately), or clear SynchronousOnly.");
        }
    }

    /// <summary>
    /// The <see cref="InvalidOperationException"/> raised when a document asked to need nothing asynchronous tries to suspend. A type of its own so the
    /// places that turn a failed resource into a render error or a blank image can let this one through unchanged: it is the host's mistake to fix,
    /// not a resource to skip.
    /// </summary>
    internal sealed class SynchronousOnlyViolationException(string message) : InvalidOperationException(message);
}
