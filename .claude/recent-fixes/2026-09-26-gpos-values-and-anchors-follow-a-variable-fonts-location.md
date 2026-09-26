# GPOS values and anchors follow a variable font's location

`ReadValueRecord`/`ReadAnchor` used to read and drop the device-table offsets. A device table of the `VariationIndex` kind (delta format
0x8000: the "start size" and "end size" fields are the (outer, inner) pair) is now kept (`DeltaRef`) and `OpenTypeDescriptor.Vary` adds
the `GDEF` item-variation-store delta at the descriptor's location, rounded half up, when a value or anchor is applied
(`GposPositioner` single, pair, mark and cursive paths). Values were checked against fontTools' instancer on a purpose-built font
(`generate_variable_layout_fixture.py`) at six weights.

Traps found by running it:

- **Where a device offset is measured from depends on the subtable format.** In a `PairPos` format 1 subtable the value records sit in
  `PairSet` tables and their device offsets are from the start of the *PairSet*, not the subtable; format 2 measures from the subtable.
  Reading them from the subtable start returned other data and left every kerning value at its default while single adjustments and
  anchors were right.
- Reading a device table moves the shared font cursor, so `ReadValueRecord` saves and restores it around the reads.
- The GDEF 1.3 `itemVarStoreOffset` is an Offset32 after `markGlyphSetsDefOffset` (byte 14 of the header).
- Not done: `FeatureVariations` (see the accepted gap).
