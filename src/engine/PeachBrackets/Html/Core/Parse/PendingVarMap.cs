using System.Collections.Generic;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// The declarations of one box's cascade whose value uses <c>var()</c>, kept until the box's custom properties are final, with the order
    /// they were written in relative to the plain declarations that were applied at once.
    /// </summary>
    /// <remarks>
    /// A <c>var()</c> reference is substituted at computed-value time, but the cascade still orders declarations by where they were written
    /// (CSS Custom Properties 1 §3.1). Deferring only the substitution loses that order unless it is kept: <c>background: linear-gradient(..., var(--x));
    /// background-clip: text</c> writes the shorthand first, so the later <c>background-clip</c> must win over the <c>border-box</c> the shorthand
    /// resets it to. Applying every deferred declaration after every plain one made the shorthand win, which painted a gradient-text heading as a box.
    /// </remarks>
    internal sealed class PendingVarMap : Dictionary<string, string>
    {
        // Created on the first deferred declaration: most boxes never use var(), and these are one box's whole cost otherwise.
        private Dictionary<string, int>? _writtenAt;
        private Dictionary<string, int>? _plainAt;
        private int _clock;

        /// <summary>Defers a declaration whose value uses <c>var()</c>; a later one for the same property replaces it.</summary>
        internal new string this[string name]
        {
            get => base[name];
            set
            {
                base[name] = value;
                (_writtenAt ??= [])[name] = ++_clock;
            }
        }

        /// <summary>Notes that a plain (resolved at once) declaration for the property was applied, which supersedes an earlier deferred one.</summary>
        internal void NotePlain(string name)
        {
            // With nothing deferred right now there is no deferred declaration to supersede, and a plain write that came before
            // every later deferred one can never be "after" it, so it need not be remembered.
            if (Count == 0) return;

            Remove(name);
            _writtenAt?.Remove(name);
            (_plainAt ??= [])[name] = ++_clock;
        }

        /// <summary>Where the deferred declaration for the property sits in the order the declarations were written; 0 when it has none.</summary>
        internal int OrderOf(string name) => _writtenAt is not null && _writtenAt.TryGetValue(name, out var at) ? at : 0;

        /// <summary>Whether a plain declaration for the property was applied after the position, so a deferred shorthand must not overwrite it.</summary>
        internal bool IsSetPlainAfter(string name, int position) => _plainAt is not null && _plainAt.TryGetValue(name, out var at) && at > position;
    }
}
