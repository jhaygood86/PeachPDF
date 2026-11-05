# Win2D text inside a rounded or path clip is grayscale, not ClearType

**What.** In `PeachDrawing.Win2D`, text drawn while a clip made from a geometry is open (`PushClip(GraphicsPath)`, `PushClipExclude`: a rounded corner on an `overflow: hidden`
card, a `clip-path`) is drawn as grayscale-antialiased text even when the user has ClearType on. Text outside such a clip, and inside a rectangular clip, uses ClearType. On
arstechnica.com the article cards (rounded, `overflow: hidden`) therefore have grayscale text beside ClearType text elsewhere.

**Why.** Direct2D draws what is inside a geometry clip into a layer and composites it when the clip is popped, so the pixels the clip's own content has drawn (the card's fill) are
not in the render target yet. ClearType blends each colour channel against the pixels behind the text, which the GPU blend (`Shaders/LcdBlend.hlsl`) samples from the target:
over a geometry layer it sampled the page behind the card and drew a rectangle of it around every word (see `Win2dCanvas.InsideGeometryLayer`). The two ways to read the backdrop
(sampling the target, reading it back) are both wrong inside the layer, so the text takes the plain path.

**Tried / not done.** Giving a geometry clip an offscreen target of its own (initialised from the backdrop, the clip's content and the text blended against it, composited through the
geometry mask at the pop) would make ClearType correct there; it adds a render target allocation per clip and a copy of the backdrop, and needs the lifecycle counters and the
layer/blend/transform state to follow it. Estimated as a day of work for a cosmetic difference, so not done.
