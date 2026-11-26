using Jint.Native;
using PeachDrawing.Core;
using PeachBrackets.Scripting.Internal.Canvas;

namespace PeachBrackets.Scripting.Internal;

internal sealed partial class ScriptRuntime
{
    // Drawing that reads a canvas back (pixels, an image, a pattern) cannot be made while one of its layers is open: the layer's content is not on the canvas yet.
    private void RequireNoOpenLayers(CanvasBitmap bitmap)
    {
        if (bitmap.HasOpenLayers)
            throw DomError("InvalidStateError", "The canvas has an open layer.");
    }

    // The effects a layer's (or the context's) filter stands for. Nothing, null and a filter that is not valid are no effects.
    private LayerEffect[] LayerFilterEffects(JsValue value) => [];
}
