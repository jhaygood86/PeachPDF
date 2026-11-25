using System.Collections.Generic;

namespace PeachBrackets.ScriptHosting;

/// <summary>What a change to a document's tree was.</summary>
public enum ScriptMutationKind
{
    /// <summary>Children were added to or taken out of a node.</summary>
    ChildList,

    /// <summary>An element's attribute was set or removed.</summary>
    Attributes,

    /// <summary>The text of a text node, comment or processing instruction changed.</summary>
    CharacterData,
}

/// <summary>One change to a <see cref="ScriptDocument"/>'s tree, reported by <see cref="ScriptDocument.Mutated"/>.</summary>
/// <param name="Kind">what changed</param>
/// <param name="Target">the node whose children, attribute or text changed</param>
/// <param name="Added">the nodes that were put in (a child-list change)</param>
/// <param name="Removed">the nodes that were taken out (a child-list change)</param>
/// <param name="Previous">the sibling before the added or removed nodes, or null</param>
/// <param name="Next">the sibling after them, or null</param>
/// <param name="AttributeName">the name of the attribute (an attribute change)</param>
/// <param name="OldValue">the attribute's or text's value before the change, or null when there was none</param>
public sealed record ScriptMutation(
    ScriptMutationKind Kind,
    ScriptNode Target,
    IReadOnlyList<ScriptNode>? Added = null,
    IReadOnlyList<ScriptNode>? Removed = null,
    ScriptNode? Previous = null,
    ScriptNode? Next = null,
    string? AttributeName = null,
    string? OldValue = null);
