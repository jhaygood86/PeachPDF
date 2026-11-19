# ICCBased profile whose component count disagrees with /N

PDFium's `icc_profile_bad_component`: `/N 1` on a stream holding a three-component sRGB profile, then `1 0 0 sc`. PDFium paints white;
this importer reads the space as DeviceGray (by `/N`), ignores the three-operand `sc` as an arity mismatch and paints the space's initial
color, black. The file is a deliberate fuzz-style probe and the specification does not define the result of a contradictory profile
(ISO 32000-1 8.6.5.5 says /N must match), so neither answer is more correct. Matching PDFium's white would mean reading the profile's own
count and then still discarding the color, which no real producer needs. Not tracked as a spec deviation. Leave the test failing; revisit only
if a real file with a mismatched /N shows up.
