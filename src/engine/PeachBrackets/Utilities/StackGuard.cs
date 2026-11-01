using PeachPDF;
using System;
using System.Runtime.CompilerServices;

namespace PeachBrackets.Utilities
{
    /// <summary>
    /// Turns a stack overflow, which ends the whole process and cannot be caught, into an <see cref="HtmlRenderException"/>
    /// the host can. Called at the entry of the recursive walks (layout, fragment emission, the SVG tree builder and
    /// renderer); the parser's depth limit keeps ordinary documents far from it, and this catches what that cannot
    /// see: boxes the cascade adds (anonymous wrappers, table parts), a standalone SVG, a host with a small stack.
    /// </summary>
    internal static class StackGuard
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Ensure()
        {
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                ThrowTooDeep();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTooDeep() =>
            throw new HtmlRenderException(
                "The document is nested too deeply for the renderer's stack.",
                HtmlRenderErrorType.Layout);
    }
}
