using PeachDrawing.Core;
using System;

namespace PeachBrackets.ScriptHosting;

/// <summary>What a script drew on a <c>canvas</c> element: the bitmap's size and a way to draw it again on any <see cref="Canvas"/>.</summary>
/// <param name="Width">the bitmap's width in CSS pixels</param>
/// <param name="Height">the bitmap's height in CSS pixels</param>
/// <param name="Paint">draws the content with the canvas's top left at the origin and one unit a CSS pixel; it may be called any number of times</param>
public sealed record ScriptCanvasPainting(double Width, double Height, Action<Canvas> Paint);
