# The bottom margin of a block and the margins of an empty block after it are added, not collapsed

`<div style="margin-bottom:14px"></div><div style="height:0;margin-bottom:20px"></div><div>z</div>`: the block after the empty one starts 34px below the first, Chrome has 20px (the adjoining margins collapse through the empty block to the largest, CSS 2.1 §8.3.1; tracking issue #2252). Without the empty block the gap is right. Found while measuring a table cell's content extent, whose tests avoid it by giving the last block no bottom margin.
