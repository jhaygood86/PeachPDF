using PeachBrackets.Updates;

namespace PeachBrackets.Accessibility;

/// <summary>Where <see cref="HtmlDocument.MoveFocusAsync"/> put the focus.</summary>
/// <param name="Node">the node that has the focus now, or null if the focus left the document</param>
/// <param name="LeftDocument">whether the focus moved past the last (or before the first) tab stop, so the host should hand it to whatever comes next (or before) its view; the document then has no focus</param>
/// <param name="Update">what changed in the document's style as the focus moved, and what to repaint</param>
public readonly record struct FocusMoveResult(AccessibilityNode? Node, bool LeftDocument, UpdateResult Update);
