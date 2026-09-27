"""Builds the variation tables of the TrueType fixtures of the variable-font hinting tests by hand (generate_hinting_variable_fixtures.py).

fontTools writes `gvar`, `cvar`, `avar` (version 1) and `fvar` well, and the generator lets it. What it does not write is what these fixtures are
for: `HVAR`, `VVAR` and `MVAR` tables and an `avar` table of version 2, made of item variation stores with the odd shapes a real font can have (words
and bytes mixed, 32-bit words, delta-set index maps of every entry size), and tables that are wrong in one deliberate way. Everything here is written byte by
byte, so that a fixture says exactly what it holds.

The fonts are CC0: they contain no third-party data (see HintingVariable.LICENSE.txt).
"""
import struct


def f2dot14(value):
    return int(round(value * 16384)) & 0xFFFF


def region_list(axis_count, regions):
    """A VariationRegionList: `regions` is a list of regions, each a list of (start, peak, end) for every axis."""
    out = struct.pack(">HH", axis_count, len(regions))
    for region in regions:
        assert len(region) == axis_count
        for start, peak, end in region:
            out += struct.pack(">HHH", f2dot14(start), f2dot14(peak), f2dot14(end))
    return out


def item_variation_data(data_set):
    """An ItemVariationData subtable. `data_set` is a dict: regions (indexes), words (how many of the deltas of an item are 16 bits wide, or 32 when
    `long`), long (bool), items (a list of lists of deltas, one delta for each region)."""
    indexes = data_set["regions"]
    words = data_set.get("words", 0)
    long_words = data_set.get("long", False)
    items = data_set["items"]
    out = struct.pack(">HHH", len(items), words | (0x8000 if long_words else 0), len(indexes))
    out += b"".join(struct.pack(">H", i) for i in indexes)
    for item in items:
        assert len(item) == len(indexes)
        for k, delta in enumerate(item):
            wide = k < words
            if long_words:
                out += struct.pack(">i" if wide else ">h", delta)
            else:
                out += struct.pack(">h" if wide else ">b", delta)
    return out


def item_variation_store(axis_count, regions, data_sets):
    """An ItemVariationStore (format 1)."""
    rlist = region_list(axis_count, regions)
    datas = [item_variation_data(d) for d in data_sets]
    header_size = 8 + 4 * len(datas)
    out = struct.pack(">HIH", 1, header_size, len(datas))
    offset = header_size + len(rlist)
    offsets = []
    for d in datas:
        offsets.append(offset)
        offset += len(d)
    out += b"".join(struct.pack(">I", o) for o in offsets) + rlist + b"".join(datas)
    return out


def delta_set_index_map(entries, fmt, inner_bits, entry_size):
    """A DeltaSetIndexMap of `entries`, each an (outer, inner) pair (or None: no variation data, all bits set). Format 0 has a 16-bit count and format 1 a 32-bit one;
    an entry is `entry_size` bytes (1 to 4), of which the low `inner_bits` bits are the inner index."""
    entry_format = (inner_bits - 1) | ((entry_size - 1) << 4)
    out = struct.pack(">BB", fmt, entry_format)
    out += struct.pack(">H" if fmt == 0 else ">I", len(entries))
    for entry in entries:
        if entry is None:
            out += b"\xff" * entry_size
        else:
            value = (entry[0] << inner_bits) | entry[1]
            assert value < (1 << (8 * entry_size)) and entry[1] < (1 << inner_bits)
            out += value.to_bytes(entry_size, "big")
    return out


def hvar_table(store, width_map=None):
    """HVAR: the item variation store, and a mapping from glyph to delta set for the advance widths when there is one."""
    header = 20
    body = store
    width_offset = 0
    if width_map is not None:
        width_offset = header + len(store)
        body += width_map
    return struct.pack(">HHIIII", 1, 0, header, width_offset, 0, 0) + body


def vvar_table(store, height_map=None):
    """VVAR: as HVAR, for the advance heights (and no mappings of the side bearings and the vertical origin)."""
    header = 24
    body = store
    height_offset = 0
    if height_map is not None:
        height_offset = header + len(store)
        body += height_map
    return struct.pack(">HHIIIII", 1, 0, header, height_offset, 0, 0, 0) + body


def mvar_table(store, records):
    """MVAR: `records` is a list of (tag, outer, inner)."""
    header = 12 + 8 * len(records)
    out = struct.pack(">HHHHHH", 1, 0, 0, 8, len(records), header)
    for tag, outer, inner in records:
        out += tag.encode("ascii") + struct.pack(">HH", outer, inner)
    return out + store


def avar_table(segments, version=1, axis_map=None, store=None):
    """An avar table. `segments` is a list, one for each axis, of lists of (from, to) pairs. Version 2 adds an axis index map and an item variation store."""
    out = struct.pack(">HHHH", version, 0, 0, len(segments))
    for pairs in segments:
        out += struct.pack(">H", len(pairs))
        for a, b in pairs:
            out += struct.pack(">HH", f2dot14(a), f2dot14(b))
    if version >= 2:
        offset = len(out) + 8
        map_offset = offset if axis_map is not None else 0
        if axis_map is not None:
            offset += len(axis_map)
        store_offset = offset if store is not None else 0
        out += struct.pack(">II", map_offset, store_offset)
        if axis_map is not None:
            out += axis_map
        if store is not None:
            out += store
    return out


def gasp_table(ranges, version=1):
    """A gasp table: `ranges` is a list of (largest ppem, flags)."""
    out = struct.pack(">HH", version, len(ranges))
    for ppem, flags in ranges:
        out += struct.pack(">HH", ppem, flags)
    return out
