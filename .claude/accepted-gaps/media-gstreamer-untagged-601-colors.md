# GStreamer decodes an untagged Rec. 601 stream a few levels off a browser's colors

`images/pattern.webm`/`.mp4` (20x20, no colorimetry in the stream) decode through `playbin ! videoconvert ! appsink` to red 252-254 on the pure-red patch and 0 on the
blue patch's red channel, where the WPT `createImageBitmap-drawImage`/`-flipY` video subtests (tolerance 3) expect 247 and 28: a browser color-manages Rec. 601
into sRGB and GStreamer's `videoconvert` does not.

Tried and measured, no change: forcing `colorimetry=bt709` with `capssetter` before `videoconvert` (252), and `videoconvert primaries-mode=fast` with an sRGB
appsink caps (254). The first YUV values of the clip (Y=80 U=0 V=241 on the red patch) show chroma outside the limited range, so the decoder's range
handling for this VP9 stream is the next thing to check (read the stream's color_range from the bitstream and force the matching `1:4:7:..` range
colorimetry), or convert the primaries in the engine from a new `MediaColorSpace` for Rec. 601.

Affects: about 40 html-canvas subtests, none of them in the pure wide-gamut set. Documented limitation: a stream with no colorimetry is not color-managed.
