# Grid `auto` rows now fill a definite height; a grid flex item is as wide as its tracks

- A grid with a definite `height` and `auto` rows used to leave the rows content-sized with free space below.
  They now share that space (default `align-content: normal`), so items stretch or `align-self` has room.
  Set `align-content: start` to keep the old packing.
- A `display: grid`/`inline-grid` flex item with `width: auto` used to take all the free width of the flex
  container (e.g. 290pt for two 50pt tracks). It now takes the sum of its tracks (100pt).
