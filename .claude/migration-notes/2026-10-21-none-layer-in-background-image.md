# A `none` layer in `background-image` no longer shifts the layers after it

`background-image: none, url(a)` paired `a` with layer 0's `background-position`, `-size`, `-repeat`... because the image list dropped the `none` entry. `a` is layer 1 and now takes the second value of each per-layer longhand, as in a browser. Documents that listed a `none` layer first and relied on the shifted pairing (or wrote their longhands for the filtered list) will see the image move.
