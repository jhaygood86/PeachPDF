namespace PeachBrackets.ScriptHosting;

/// <summary>A picture of an element of the script's document, drawn as it is laid out (see <see cref="ScriptDocument.RenderElement"/>).</summary>
/// <param name="Image">the picture, one pixel to a CSS pixel; owned by the document</param>
/// <param name="Width">the element's width, in CSS pixels</param>
/// <param name="Height">the element's height, in CSS pixels</param>
public sealed record ScriptElementPicture(PeachDrawing.Core.Image Image, double Width, double Height);
