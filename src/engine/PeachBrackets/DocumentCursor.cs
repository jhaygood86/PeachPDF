using PeachDrawing.Core;
using System.Collections.Generic;

namespace PeachBrackets;

/// <summary>
/// The pointer a document asks for at a point (the CSS <c>cursor</c> property, with <c>auto</c> already resolved): a host that shows the
/// document to someone with a pointing device sets its own pointer from it.
/// </summary>
/// <param name="Shape">the pointer to show; the one that stands in when none of <paramref name="Images"/> can be used</param>
/// <param name="Images">images to use as the pointer in order of preference; empty when the document names none</param>
public sealed record DocumentCursor(CursorShape Shape, IReadOnlyList<CursorImage> Images);
